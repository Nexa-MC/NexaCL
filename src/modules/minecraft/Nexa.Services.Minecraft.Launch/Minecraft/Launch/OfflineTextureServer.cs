using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Nexa.Core.Media;
namespace Nexa.Services.Minecraft.Launch;

/// <summary>Session-local, loopback-only Yggdrasil endpoint. No online credentials or external requests.</summary>
internal sealed class OfflineTextureServer : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _responses = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private RSA? _signer;
    private long _textureBytes;
    private readonly Dictionary<string, (string Server, string Metadata)> _identities = new(StringComparer.Ordinal);
    private TcpListener? _listener;
    private Task? _running;
    private int _profiles;
    private bool _disposed;
    private readonly string _prefix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
    internal (string Server, string Metadata) Register(MinecraftLaunchIdentity identity)
    {
        if (identity.OfflineSkinPng.Length > 1_048_576 || identity.OfflineCapePng.Length > 1_048_576) throw new InvalidDataException("Offline texture byte budget exceeded.");
        identity = identity with { OfflineSkinPng = identity.OfflineSkinPng.ToArray(), OfflineCapePng = identity.OfflineCapePng.ToArray() };
        string identityKey = identity.PlayerUuid + ":" + identity.PlayerName + ":" + identity.OfflineSlim + ":"
            + Convert.ToHexString(SHA256.HashData(identity.OfflineSkinPng)) + ":" + Convert.ToHexString(SHA256.HashData(identity.OfflineCapePng));
        if (!Guid.TryParseExact(identity.PlayerUuid, "N", out _)) throw new InvalidDataException("离线 UUID 无效。");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_identities.TryGetValue(identityKey, out var cached)) return cached;
            if (++_profiles > 128) throw new InvalidDataException("本次启动器会话中的离线材质数量过多，请重启启动器。");
            _signer ??= RSA.Create(2048);
            if (_running is null) { _listener = new(IPAddress.Loopback, 0); _listener.Start(8); _running = Task.Run(ListenAsync); }
            string root = "http://127.0.0.1:" + ((IPEndPoint)_listener!.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + _prefix + "/";
            string session = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)) + "/";
            string server = root + session;
            var textures = new JsonObject();
            AddTexture("SKIN", identity.OfflineSkinPng, identity.OfflineSlim);
            AddTexture("CAPE", identity.OfflineCapePng, false);
            string value = Convert.ToBase64String(Encoding.UTF8.GetBytes(new JsonObject
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["profileId"] = identity.PlayerUuid,
                ["profileName"] = identity.PlayerName,
                ["textures"] = textures
            }.ToJsonString()));
#pragma warning disable CA5350 // Yggdrasil texture properties are signed using the protocol's SHA1withRSA algorithm.
            string signature = Convert.ToBase64String(_signer.SignData(Encoding.UTF8.GetBytes(value), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1));
#pragma warning restore CA5350
            var profile = new JsonObject
            {
                ["id"] = identity.PlayerUuid,
                ["name"] = identity.PlayerName,
                ["properties"] = new JsonArray((JsonNode)new JsonObject { ["name"] = "textures", ["value"] = value, ["signature"] = signature })
            };
            string metadata = new JsonObject
            {
                ["meta"] = new JsonObject { ["serverName"] = "NexaCL Offline", ["feature.enable_profile_key"] = false },
                ["skinDomains"] = new JsonArray((JsonNode)JsonValue.Create("127.0.0.1")!),
                ["signaturePublickey"] = _signer.ExportSubjectPublicKeyInfoPem()
            }.ToJsonString();
            _responses["/" + _prefix + "/" + session] = Encoding.UTF8.GetBytes(metadata);
            _responses["/" + _prefix + "/" + session + "sessionserver/session/minecraft/profile/" + identity.PlayerUuid] = Encoding.UTF8.GetBytes(profile.ToJsonString());
            _identities[identityKey] = (server, metadata);
            return (server, metadata);
            void AddTexture(string kind, byte[] bytes, bool slim)
            {
                if (bytes.Length == 0) return;
                if (bytes.Length > 1_048_576) throw new InvalidDataException("离线材质过大。");
                var image = PngImage.TryCreate(bytes);
                if (image is null || image.Width is < 64 or > 512 || image.Width % 64 != 0 || (kind == "SKIN" ? image.Height != image.Width && image.Height * 2 != image.Width : image.Height * 2 != image.Width)) throw new InvalidDataException("离线材质尺寸无效。");
                string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                string texturePath = "/" + _prefix + "/textures/" + hash;
                if (!_responses.ContainsKey(texturePath))
                {
                    if (_textureBytes + bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("离线材质缓存已达到 32 MiB，请重启启动器。");
                    _textureBytes += bytes.Length; _responses[texturePath] = bytes;
                }
                var texture = new JsonObject { ["url"] = root + "textures/" + hash };
                if (kind == "SKIN" && slim) texture["metadata"] = new JsonObject { ["model"] = "slim" };
                textures[kind] = texture;
            }
        }
    }
    private async Task ListenAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var connection = await _listener!.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await using var stream = connection.GetStream();
                    byte[] header = new byte[8192]; int used = 0;
                    while (used < header.Length)
                    {
                        int count = await stream.ReadAsync(header.AsMemory(used), timeout.Token).ConfigureAwait(false); if (count == 0) break; used += count;
                        if (header.AsSpan(0, used).IndexOf("\r\n\r\n"u8) >= 0) break;
                    }
                    string first = Encoding.ASCII.GetString(header, 0, used).Split('\r')[0];
                    string[] parts = first.Split(' '); byte[]? body = null;
                    if (parts.Length == 3 && parts[0] == "GET")
                    {
                        string target = parts[1].Split('?')[0];
                        lock (_gate) _responses.TryGetValue(target, out body);
                    }
                    bool png = body is { Length: > 8 } && body.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 });
                    string status = body is null ? "404 Not Found" : "200 OK"; body ??= [];
                    byte[] response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {(png ? "image/png" : "application/json")}\r\nContent-Length: {body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response, timeout.Token).ConfigureAwait(false); await stream.WriteAsync(body, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or SocketException) { }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true; _stop.Cancel(); _listener?.Stop(); _responses.Clear(); _identities.Clear(); _signer?.Dispose();
        }
        if (_running is not null) _ = _running.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default); else _stop.Dispose();
    }
}
