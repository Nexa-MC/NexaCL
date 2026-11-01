using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Nexa.Native.Network.Client;
using Nexa.Services.Network;

const int total = 20 * 1024 * 1024;
using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(90));
using TcpListener listener = new(IPAddress.Loopback, 0); listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
Task serve = Task.Run(async () =>
{
    using var peer = await listener.AcceptTcpClientAsync(deadline.Token);
    await using var stream = peer.GetStream();
    using MemoryStream header = new(); byte[] buffer = new byte[65536]; int boundary;
    while (true)
    {
        int count = await stream.ReadAsync(buffer, deadline.Token); if (count == 0) throw new EndOfStreamException();
        header.Write(buffer, 0, count);
        boundary = header.GetBuffer().AsSpan(0, (int)header.Length).IndexOf("\r\n\r\n"u8);
        if (boundary >= 0) break;
        if (header.Length > 16384) throw new InvalidDataException("Request header exceeded fixture budget.");
    }
    string headers = Encoding.ASCII.GetString(header.GetBuffer(), 0, boundary);
    if (!headers.Contains("Content-Length: " + total.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Content length was not preserved.");
    int received = (int)header.Length - boundary - 4;
    if (header.GetBuffer().AsSpan(boundary + 4, received).ContainsAnyExcept((byte)7)) throw new InvalidDataException("Upload bytes changed.");
    while (received < total)
    {
        int count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, total - received)), deadline.Token);
        if (count == 0 || buffer.AsSpan(0, count).ContainsAnyExcept((byte)7)) throw new InvalidDataException("Upload truncated or changed.");
        received += count;
    }
    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + total.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n"), deadline.Token);
    Array.Fill(buffer, (byte)7);
    for (int sent = 0; sent < total; sent += buffer.Length) await stream.WriteAsync(buffer, deadline.Token);
}, deadline.Token);
await using var network = new NativeNetworkClient();
using var http = new HttpClient(network.CreateHandler(() => new() { ProxyMode = 0, DnsOverHttps = false, IpStack = "ipv4", TraceEnabled = true }), disposeHandler: true);
using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{port}/stream"))
{ Content = new ByteArrayContent(Enumerable.Repeat((byte)7, total).ToArray()) };
using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token))
{
    response.EnsureSuccessStatusCode();
    await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
    byte[] bytes = new byte[32768]; int count, received = 0;
    while ((count = await body.ReadAsync(bytes, deadline.Token)) != 0)
    {
        if (bytes.AsSpan(0, count).ContainsAnyExcept((byte)7)) throw new InvalidDataException("Response bytes changed.");
        received += count;
    }
    if (received != total) throw new InvalidDataException("Response truncated.");
}
await serve;
var trace = await network.TraceAsync(new() { TraceEnabled = true }, deadline.Token);
if (trace.Count != 1 || trace[0].Host != "127.0.0.1" || trace[0].StatusCode != 200)
    throw new InvalidDataException("Network trace did not retain the native request outcome.");
if ((await network.TraceAsync(new(), deadline.Token)).Count != 0)
    throw new InvalidDataException("Disabling diagnostics did not clear the native trace.");
var probe = await network.ProbeAsync(new()
{
    OfficialProviderEnabled = false,
    ModrinthProviderEnabled = false,
    CurseForgeProviderEnabled = false,
    MirrorProviderEnabled = false
}, deadline.Token);
if (probe.Endpoints.Count != 3 || probe.Endpoints.Any(endpoint => endpoint.StatusCode is not null || endpoint.ErrorKind != "HttpRequestException"))
    throw new InvalidDataException("Native manual probes did not preserve provider admission.");
await CancellationCases.RunAsync(http, network, deadline.Token);
http.Dispose(); await network.DisposeAsync();
Console.WriteLine("Independent NativeAOT network owner streamed a 20 MiB HTTP upload and response, then physically retired the session.");

static class CancellationCases
{
    internal static async Task RunAsync(HttpClient http, NativeNetworkClient network, CancellationToken deadline)
    {
        await HeadersAsync(http, deadline);
        Console.WriteLine("Canceled native HTTP headers.");
        await ReadAsync(http, deadline);
        Console.WriteLine("Canceled native HTTP body read.");
        await UploadAsync(http, deadline);
        Console.WriteLine("Canceled native HTTP upload.");
        await RedirectAsync(http, deadline);
        await using (var fixture = new PendingServer(headers: false, deadline))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, fixture.Uri) { Content = new ThrowingUpload(fixture.Accepted) };
            try
            {
                using var unexpected = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline);
                throw new InvalidDataException("Upload failure was lost.");
            }
            catch (InvalidOperationException error) when (error.Message == "fixture upload failed") { }
            await fixture.Completed;
        }
        for (int attempt = 0; attempt < 33; attempt++)
        {
            try
            {
                using var unexpected = await network.CreateCertifiedHandlerAsync([1], null,
                    () => new() { DnsOverHttps = false }, deadline);
                throw new InvalidDataException("Malformed PFX was admitted.");
            }
            catch (InvalidOperationException) { }
        }
        using (RSA key = RSA.Create(2048))
        {
            var enrollment = new CertificateRequest("CN=Nexa network native contract", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = enrollment.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            byte[] pfx = certificate.Export(X509ContentType.Pfx, "native-contract-only");
            using var certified = new HttpClient(await network.CreateCertifiedHandlerAsync(pfx, "native-contract-only",
                () => new() { ProxyMode = 0, DnsOverHttps = false }, deadline));
            await using var fixture = new PendingServer(headers: true, deadline, length: 0);
            using var response = await certified.GetAsync(fixture.Uri, HttpCompletionOption.ResponseHeadersRead, deadline);
            if (response.Content.Headers.ContentLength != 0) throw new InvalidDataException("Certified transport lost content headers.");
            await using var body = await response.Content.ReadAsStreamAsync(deadline); await body.DisposeAsync();
            response.Dispose(); await fixture.Completed;
            CryptographicOperations.ZeroMemory(pfx);
        }
        // Exercise more than the handler's total resource allowance through complete
        // retirement; the cancellation cases separately abandon incomplete bodies.
        for (int index = 0; index < 140; index++)
        {
            await using var fixture = new PendingServer(headers: true, deadline, length: 0);
            using var response = await http.GetAsync(fixture.Uri, HttpCompletionOption.ResponseHeadersRead, deadline);
            await using var body = await response.Content.ReadAsStreamAsync(deadline);
            await body.DisposeAsync();
            response.Dispose();
            await fixture.Completed;
        }
        await using (var fixture = new PendingServer(headers: true, deadline))
        {
            using var response = await http.GetAsync(fixture.Uri, HttpCompletionOption.ResponseHeadersRead, deadline);
            await using var body = await response.Content.ReadAsStreamAsync(deadline);
            Task<int> pending = body.ReadAsync(new byte[1], deadline).AsTask();
            await fixture.Accepted;
            await network.DisposeAsync();
            try { await pending; throw new InvalidDataException("Shutdown completed a pending body read successfully."); }
            catch (Exception error) when (error is OperationCanceledException or HttpRequestException or IOException) { }
            await fixture.Completed;
        }
        Console.WriteLine("Native HTTP canceled upload, headers and read; recovered resource budgets; drained a blocked read during shutdown.");
    }
    private static async Task HeadersAsync(HttpClient http, CancellationToken deadline)
    {
        await using var fixture = new PendingServer(headers: false, deadline);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline);
        Task<HttpResponseMessage> response = http.GetAsync(fixture.Uri, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
        await fixture.Accepted; cancel.Cancel();
        try { using var unexpected = await response; throw new InvalidDataException("Pending headers ignored cancellation."); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        await fixture.Completed;
    }
    private static async Task RedirectAsync(HttpClient http, CancellationToken deadline)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var uri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/start");
        Task serve = Task.Run(async () =>
        {
            for (int index = 0; index < 2; index++)
            {
                using var peer = await listener.AcceptTcpClientAsync(deadline);
                await using var stream = peer.GetStream();
                using var header = new MemoryStream(); byte[] bytes = new byte[4096];
                while (header.GetBuffer().AsSpan(0, (int)header.Length).IndexOf("\r\n\r\n"u8) < 0)
                {
                    int count = await stream.ReadAsync(bytes, deadline);
                    if (count == 0 || header.Length + count > 16384) throw new InvalidDataException("Invalid redirect request.");
                    header.Write(bytes, 0, count);
                }
                string firstLine = Encoding.ASCII.GetString(header.GetBuffer(), 0, (int)header.Length).Split("\r\n")[0];
                if (!firstLine.StartsWith(index == 0 ? "POST /start " : "GET /target ", StringComparison.Ordinal))
                    throw new InvalidDataException("Native redirect changed request semantics.");
                string reply = index == 0 ? "HTTP/1.1 302 Found\r\nLocation: /target\r\n" : "HTTP/1.1 200 OK\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "Content-Length: 0\r\nConnection: close\r\n\r\n"), deadline);
            }
        }, deadline);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline);
        if (response.RequestMessage?.RequestUri != new Uri(uri, "/target") || response.RequestMessage.Method != HttpMethod.Get)
            throw new InvalidDataException("Native response lost its final redirect URI or method.");
        await serve;
    }
    private static async Task ReadAsync(HttpClient http, CancellationToken deadline)
    {
        await using var fixture = new PendingServer(headers: true, deadline);
        using var response = await http.GetAsync(fixture.Uri, HttpCompletionOption.ResponseHeadersRead, deadline);
        await using var body = await response.Content.ReadAsStreamAsync(deadline);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline);
        Task<int> pending = body.ReadAsync(new byte[1], cancel.Token).AsTask(); cancel.Cancel();
        try { await pending; throw new InvalidDataException("Pending read ignored cancellation."); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        response.Dispose(); await fixture.Completed;
    }
    private static async Task UploadAsync(HttpClient http, CancellationToken deadline)
    {
        await using var fixture = new PendingServer(headers: false, deadline);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline);
        using var request = new HttpRequestMessage(HttpMethod.Post, fixture.Uri) { Content = new PendingUpload() };
        Task<HttpResponseMessage> response = http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
        await fixture.Accepted; cancel.Cancel();
        try { using var unexpected = await response; throw new InvalidDataException("Pending upload ignored cancellation."); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        await fixture.Completed;
    }
    private sealed class ThrowingUpload(Task headers) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 1024 * 1024; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            await stream.WriteAsync(new byte[65536], token); await headers.WaitAsync(token);
            throw new InvalidOperationException("fixture upload failed");
        }
    }
    private sealed class PendingUpload : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 1024 * 1024; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        { await stream.WriteAsync(new byte[65536], token); await Task.Delay(Timeout.Infinite, token); }
    }
    private sealed class PendingServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop;
        private readonly TaskCompletionSource _accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Uri Uri { get; }
        internal Task Accepted => _accepted.Task;
        internal Task Completed { get; }
        internal PendingServer(bool headers, CancellationToken deadline, int length = 1048576)
        {
            _stop = CancellationTokenSource.CreateLinkedTokenSource(deadline); _listener.Start();
            Uri = new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/pending");
            Completed = ServeAsync(headers, length);
        }
        private async Task ServeAsync(bool headers, int length)
        {
            try
            {
                using var peer = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = peer.GetStream();
                using var received = new MemoryStream(); byte[] buffer = new byte[65536];
                while (received.GetBuffer().AsSpan(0, (int)received.Length).IndexOf("\r\n\r\n"u8) < 0)
                {
                    int count = await stream.ReadAsync(buffer, _stop.Token); if (count == 0) throw new EndOfStreamException();
                    received.Write(buffer, 0, count);
                    if (received.Length > 128 * 1024) throw new InvalidDataException("Fixture header budget exceeded.");
                }
                if (headers) await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n"), _stop.Token);
                _accepted.TrySetResult();
                while (await stream.ReadAsync(buffer, _stop.Token) != 0) { }
            }
            catch (IOException) { _accepted.TrySetResult(); }
            catch (Exception error) { _accepted.TrySetException(error); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel(); _listener.Stop();
            try { await Completed; }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            finally { _stop.Dispose(); }
        }
    }
}
