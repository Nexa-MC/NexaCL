using System.Collections.Immutable;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed class NetworkLease(PluginIdentity owner, NetworkConnection reference, Socket socket, bool listener) : IDisposable
    {
        internal readonly PluginIdentity Owner = owner;
        internal readonly NetworkConnection Reference = reference;
        internal Socket Socket = socket;
        internal Stream? Stream;
        internal bool Listener = listener;
        internal readonly SemaphoreSlim ReadGate = new(1, 1), WriteGate = new(1, 1);
        public void Dispose() { Stream?.Dispose(); Socket.Dispose(); }
    }
    private sealed class ResponseLease(PluginIdentity owner, CancellationTokenSource lifetime) : IDisposable
    {
        internal readonly PluginIdentity Owner = owner;
        internal readonly CancellationTokenSource Lifetime = lifetime;
        internal readonly CancellationToken Token = lifetime.Token;
        private int _disposed;
        internal HttpResponseMessage? Response;
        internal Stream? Stream;
        internal long Read;
        internal readonly SemaphoreSlim Gate = new(1, 1);
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; Lifetime.Cancel(); Stream?.Dispose(); Response?.Dispose(); Lifetime.Dispose(); }
    }
    private readonly object _networkGate = new();
    private readonly Dictionary<EntityId, NetworkLease> _connections = [];
    private readonly Dictionary<EntityId, ResponseLease> _responses = [];
    private HttpClient _pluginHttp = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, MaxResponseHeadersLength = 32, ConnectTimeout = TimeSpan.FromSeconds(15) }) { Timeout = TimeSpan.FromSeconds(60) };
    public void BindNetworkHttp(HttpClient client)
    {
        if (_services is not null) throw new InvalidOperationException("Configure network before activating plugins.");
        _pluginHttp.Dispose(); _pluginHttp = client;
    }
    private NetworkLease Connection(HostInvocation c, NetworkConnection reference)
    {
        lock (_networkGate)
        {
            if (!_connections.TryGetValue(reference.Id, out var lease) || lease.Owner != c.Identity || lease.Reference != reference) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            return lease;
        }
    }
    private ResponseLease Response(HostInvocation c, EntityId id)
    {
        lock (_networkGate)
        {
            if (!_responses.TryGetValue(id, out var lease) || lease.Owner != c.Identity) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            return lease;
        }
    }
    private async ValueTask<NetworkConnection> OpenConnection(HostInvocation c, NetworkConnectionRequest request, bool listen)
    {
        if (request.Port is < 1 or > 65535 || request.Address.Length is < 1 or > 253 || request.Scope.Length is < 1 or > 128 || request.Protocol.Value is not ("nexa:tcp" or "nexa:udp" or "nexa:tls")) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        if (listen && request.Protocol.Value == "nexa:tls") throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        if (listen) await Confirm(c, "network/listen", "Listen on " + request.Address + ":" + request.Port, NetworkConnectionRequest.Write(request), c.Options.Cancellation).ConfigureAwait(false);
        var addresses = await Dns.GetHostAddressesAsync(request.Address, c.Options.Cancellation).ConfigureAwait(false);
        var address = addresses.FirstOrDefault() ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        bool udp = request.Protocol.Value == "nexa:udp";
        var socket = new Socket(address.AddressFamily, udp ? SocketType.Dgram : SocketType.Stream, udp ? ProtocolType.Udp : ProtocolType.Tcp);
        var reference = new NetworkConnection(new(Guid.NewGuid().ToString("N")), request.Protocol, request.Scope, c.Identity.LoadInstanceId);
        var lease = new NetworkLease(c.Identity, reference, socket, listen && !udp);
        try
        {
            var endpoint = new IPEndPoint(address, (int)request.Port);
            if (listen) { socket.Bind(endpoint); if (!udp) socket.Listen(1); }
            else await socket.ConnectAsync(endpoint, c.Options.Cancellation).ConfigureAwait(false);
            if (!udp && !listen)
            {
                lease.Stream = new NetworkStream(socket, false);
                if (request.Protocol.Value == "nexa:tls")
                {
                    var tls = new SslStream(lease.Stream, false); lease.Stream = tls;
                    await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = request.Address }, c.Options.Cancellation).ConfigureAwait(false);
                }
            }
            lock (_networkGate)
            {
                if (_connections.Count >= 32) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                if (!Application.Runtime.Active.Contains(c.Identity)) throw new AbiHostException(AbiErrors.PLUGIN_STOPPED);
                _connections.Add(reference.Id, lease);
            }
            return reference;
        }
        catch { lease.Dispose(); throw; }
    }
    private IEnumerable<HostCapabilityRoute> NetworkRoutes => [
        HostRoutes.Typed<NetworkRequest, NetworkResponse>("Network", "Request", async (c, v) =>
        {
            var uri = new Uri(v.Uri.Value);
            if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || v.Method is not ("GET" or "HEAD" or "POST" or "PUT" or "PATCH" or "DELETE" or "OPTIONS") || v.Headers.Length > 64) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            if (v.Credential is not null) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            using var request = new HttpRequestMessage(new HttpMethod(v.Method), uri);
            if (v.Body is not null) request.Content = new ByteArrayContent(Application.CopyBlob(c.Identity, v.Body));
            foreach (var header in v.Headers)
            {
                string value = DataCodec.ReadString(header.Value);
                if (header.Name.Length > 128 || value.Length > 8192 || header.Name.Any(char.IsControl) || value.Any(char.IsControl) || header.Name.Equals("Host", StringComparison.OrdinalIgnoreCase) || header.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                if (!request.Headers.TryAddWithoutValidation(header.Name, value) && (request.Content is null || !request.Content.Headers.TryAddWithoutValidation(header.Name, value))) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            }
            var id = new EntityId(c.Options.OperationId.ToString("N"));
            var lease = new ResponseLease(c.Identity, CancellationTokenSource.CreateLinkedTokenSource(c.Options.Cancellation, _resourceStop.Token));
            lease.Lifetime.CancelAfter(TimeSpan.FromMinutes(2));
            lock (_networkGate)
            {
                if (_responses.Count >= 32) { lease.Dispose(); throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE); }
                if (!_responses.TryAdd(id, lease)) { lease.Dispose(); throw new AbiHostException(AbiErrors.REQUEST_ID_REUSED); }
            }
            lease.Token.Register(() =>
            {
                lock (_networkGate)
                    if (_responses.TryGetValue(id, out var retained) && ReferenceEquals(retained, lease)) _responses.Remove(id);
                lease.Dispose();
            });
            try
            {
                var response = await _pluginHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lease.Token).ConfigureAwait(false);
                try
                {
                    if (response.Content.Headers.ContentLength > 32 * 1024 * 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                    var stream = await response.Content.ReadAsStreamAsync(lease.Token).ConfigureAwait(false);
                    lock (_networkGate)
                    {
                        if (!_responses.TryGetValue(id, out var current) || current != lease || !Application.Runtime.Active.Contains(c.Identity)) throw new AbiHostException(AbiErrors.PLUGIN_STOPPED);
                        lease.Response = response; lease.Stream = stream;
                    }
                    var headers = response.Headers.Concat(response.Content.Headers).Select(h => new NamedValue(h.Key, new AbiValue.Utf8String(string.Join(", ", h.Value)))).ToImmutableArray();
                    return new(id, (int)response.StatusCode, headers, null);
                }
                catch { response.Dispose(); throw; }
            }
            catch { lock (_networkGate) _responses.Remove(id); lease.Dispose(); throw; }
        }),
        HostRoutes.Typed<EntityRequest, BlobRef>("Network", "OpenResponseStream", async (c, v) =>
        {
            var lease = Response(c, v.Id);
            await lease.Gate.WaitAsync(c.Options.Cancellation).ConfigureAwait(false);
            try
            {
                if (lease.Stream is null) throw new AbiHostException(AbiErrors.CONFLICT);
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(c.Options.Cancellation, lease.Token);
                byte[] buffer = new byte[1024 * 1024];
                int count = await lease.Stream.ReadAsync(buffer, cancellation.Token).ConfigureAwait(false);
                lease.Read += count; if (lease.Read > 32 * 1024 * 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                return Application.AddBlob(c.Identity, buffer.AsSpan(0, count).ToArray());
            }
            finally { lease.Gate.Release(); }
        }),
        HostRoutes.Typed<OperationRequest, Empty>("Network", "Cancel", (c, v) =>
        { var lease = Response(c, v.Operation); lock (_networkGate) _responses.Remove(v.Operation); lease.Dispose(); return ValueTask.FromResult(new Empty()); }),
        HostRoutes.Typed<NetworkConnectionRequest, NetworkConnection>("Network", "OpenConnection", (c, v) => OpenConnection(c, v, false)),
        HostRoutes.Typed<NetworkConnectionRequest, NetworkConnection>("Network", "Listen", (c, v) => OpenConnection(c, v, true)),
        HostRoutes.Typed<NetworkSend, Empty>("Network", "Send", async (c, v) =>
        {
            var lease = Connection(c, v.Connection); byte[] bytes = Application.CopyBlob(c.Identity, v.Data);
            await lease.WriteGate.WaitAsync(c.Options.Cancellation).ConfigureAwait(false);
            try
            {
                if (lease.Reference.Protocol.Value == "nexa:udp")
                {
                    if (bytes.Length > 65507) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                    if (v.Destination is { } destination)
                    {
                        var address = ServerAddress(destination); var ips = await Dns.GetHostAddressesAsync(address.Host, c.Options.Cancellation).ConfigureAwait(false);
                        var ip = ips.FirstOrDefault(a => a.AddressFamily == lease.Socket.AddressFamily) ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
                        await lease.Socket.SendToAsync(bytes, SocketFlags.None, new IPEndPoint(ip, address.Port), c.Options.Cancellation).ConfigureAwait(false);
                    }
                    else await lease.Socket.SendAsync(bytes, SocketFlags.None, c.Options.Cancellation).ConfigureAwait(false);
                }
                else
                {
                    if (v.Destination is not null || lease.Listener || lease.Stream is null) throw new AbiHostException(AbiErrors.CONFLICT);
                    await lease.Stream.WriteAsync(bytes, c.Options.Cancellation).ConfigureAwait(false);
                }
                return new Empty();
            }
            finally { lease.WriteGate.Release(); }
        }),
        HostRoutes.Typed<NetworkReceive, NetworkPacket>("Network", "Receive", async (c, v) =>
        {
            if (v.MaximumBytes is < 1 or > 1024 * 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var lease = Connection(c, v.Connection);
            await lease.ReadGate.WaitAsync(c.Options.Cancellation).ConfigureAwait(false);
            try
            {
                byte[] buffer = new byte[(int)v.MaximumBytes]; int count; string? source; bool eof = false;
                if (lease.Reference.Protocol.Value == "nexa:udp")
                {
                    EndPoint endpoint = new IPEndPoint(lease.Socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
                    var received = await lease.Socket.ReceiveFromAsync(buffer, SocketFlags.None, endpoint, c.Options.Cancellation).ConfigureAwait(false);
                    count = received.ReceivedBytes; source = received.RemoteEndPoint.ToString();
                }
                else
                {
                    if (lease.Listener)
                    {
                        var accepted = await lease.Socket.AcceptAsync(c.Options.Cancellation).ConfigureAwait(false);
                        lease.Socket.Dispose(); lease.Socket = accepted; lease.Stream = new NetworkStream(accepted, false); lease.Listener = false;
                    }
                    count = await lease.Stream!.ReadAsync(buffer, c.Options.Cancellation).ConfigureAwait(false); eof = count == 0; source = lease.Socket.RemoteEndPoint?.ToString();
                }
                return new(Application.AddBlob(c.Identity, buffer.AsSpan(0, count).ToArray()), source, eof);
            }
            finally { lease.ReadGate.Release(); }
        }),
        HostRoutes.Typed<NetworkConnection, Empty>("Network", "Close", (c, v) =>
        { var lease = Connection(c, v); lock (_networkGate) _connections.Remove(v.Id); lease.Dispose(); return ValueTask.FromResult(new Empty()); })
    ];
    private void RetireNetwork(string? plugin)
    {
        lock (_networkGate)
        {
            foreach (var item in _connections.Where(p => plugin is null || p.Value.Owner.PluginId == plugin).ToArray()) { _connections.Remove(item.Key); item.Value.Dispose(); }
            foreach (var item in _responses.Where(p => plugin is null || p.Value.Owner.PluginId == plugin).ToArray()) { _responses.Remove(item.Key); item.Value.Dispose(); }
        }
    }
}
