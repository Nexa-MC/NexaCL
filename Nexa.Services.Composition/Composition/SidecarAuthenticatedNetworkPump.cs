using System.Net.Http.Headers;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

/// <summary>A bounded pull bridge for an explicitly granted package and fixed mTLS endpoint.</summary>
public sealed class SidecarAuthenticatedNetworkPump : IAsyncDisposable
{
    public static readonly XsrSemanticId Exchange = XsrSemanticId.Parse("host.network.exchange.v1");
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    public SidecarAuthenticatedNetworkPump(SidecarSupervisor supervisor, string package, string profile,
        Uri endpoint, Func<HttpClient?> client, Func<byte[]> runtimeFacts)
    {
        if (endpoint.Scheme != "https" || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("An HTTPS endpoint without credentials is required.", nameof(endpoint));
        _worker = Run(supervisor, package, profile, endpoint, client, runtimeFacts, _stop.Token);
    }
    private static async Task Run(SidecarSupervisor supervisor, string package, string profile, Uri endpoint,
        Func<HttpClient?> client, Func<byte[]> runtimeFacts, CancellationToken stop)
    {
        string receipt = ""; uint status = 0, retry = 0; byte[] body = []; Guid sessionId = default;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (supervisor.TryGetSession(package, out var session)
                        && session!.Registration?.TryResolve(SidecarRegistrationKind.Query, Exchange)?.CodecId == 6)
                    {
                        if (session.SessionId != sessionId) { receipt = ""; status = 0; body = []; retry = 0; sessionId = session.SessionId; }
                        using var fields = new SidecarPayloadWriter();
                        fields.WriteBytes(1, runtimeFacts()); fields.WriteString(2, receipt); fields.WriteUInt32(3, status);
                        fields.WriteBytes(4, body); fields.WriteUInt32(5, retry);
                        var result = await session.SendBinaryQueryAsync(Exchange, new(6, fields.ToArray()), TimeSpan.FromSeconds(5), stop).ConfigureAwait(false);
                        if (result.IsSuccess)
                        {
                            receipt = ""; status = 0; body = []; retry = 0;
                            if (result.Value.Length > 0)
                            {
                                var request = DecodeRequest(result.Value.ToArray(), profile);
                                receipt = request.Id;
                                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                                try
                                {
                                    HttpClient? transport = client();
                                    if (transport is null) { status = 503; retry = 60; }
                                    else
                                    {
                                        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
                                        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.Bearer);
                                        message.Content = new ByteArrayContent(request.Body);
                                        message.Content.Headers.ContentType = new("application/json");
                                        using var response = await transport.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                                        status = (uint)response.StatusCode;
                                        retry = (uint)Math.Clamp((response.Headers.RetryAfter?.Delta
                                            ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow))?.TotalSeconds ?? 0, 0, 3600);
                                        if (response.Content.Headers.ContentLength > 49152) throw new InvalidDataException("Response budget exceeded.");
                                        using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                                        byte[] buffer = new byte[49153]; int count = 0;
                                        while (count < buffer.Length)
                                        {
                                            int read = await stream.ReadAsync(buffer.AsMemory(count), deadline.Token).ConfigureAwait(false);
                                            if (read == 0) break;
                                            count += read;
                                        }
                                        if (count > 49152) throw new InvalidDataException("Response budget exceeded.");
                                        body = buffer.AsSpan(0, count).ToArray();
                                    }
                                }
                                catch (Exception error) when (!stop.IsCancellationRequested && error is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
                                { status = 0; body = []; retry = 30; }
                            }
                        }
                    }
                }
                catch (Exception error) when (!stop.IsCancellationRequested && error is SidecarProtocolException or InvalidOperationException or ArgumentException)
                { receipt = ""; body = []; status = 0; retry = 30; }
                await Task.Delay(TimeSpan.FromSeconds(2), stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    public static (string Id, string Bearer, byte[] Body) DecodeRequest(byte[] bytes, string allowedProfile)
    {
        if (bytes.Length > 9000) throw new ArgumentException("Request exceeds budget.");
        string profile = "", id = "", bearer = ""; byte[] body = [];
        var reader = new SidecarPayloadReader(bytes);
        while (reader.HasMore)
        {
            var field = reader.ReadNext();
            switch (field.Id)
            {
                case 1: profile = field.ReadString(); break;
                case 2: id = field.ReadString(); break;
                case 3: bearer = field.ReadString(); break;
                case 4: body = field.ReadBytes().ToArray(); break;
            }
        }
        if (profile != allowedProfile || !Guid.TryParseExact(id, "D", out _) || bearer.Length is < 1 or > 4096
            || bearer.Any(c => c <= 32 || c >= 127) || body.Length is < 1 or > 4096)
            throw new ArgumentException("Request does not match its grant.");
        return (id, bearer, body);
    }
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _worker.ConfigureAwait(false); _stop.Dispose();
    }
}
