using System.Net;
using System.Net.Sockets;
using System.Text;
using Nexa.Services.Network;
using Nexa.Services.Settings;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static (string, Func<ValueTask>)[] RegisterProxy() =>
    [
        ("PAC helpers constraints and route validation", PacHelpersConstraintsAndRouteValidation),
        ("proxy settings and transport schemes agree", Sync(ProxySettingsAndTransportSchemesAgree)),
        ("PAC bootstrap bounds actual bytes and honors cancellation", PacBootstrapBoundsActualBytesAndCancellation),
        ("PAC redirect selects routes and strips credentials", PacRedirectSelectsRoutesAndStripsCredentials),
        ("PAC fallback never replays posts or TLS failures", PacFallbackNeverReplaysPostsOrTlsFailures),
        ("SOCKS authenticated socket handshakes", SocksAuthenticatedSocketHandshakes),
        ("PAC real socket pipeline follows committed routing", PacRealSocketPipelineFollowsCommittedRouting),
    ];

    private static async ValueTask PacHelpersConstraintsAndRouteValidation()
    {
        var destination = new Uri("https://example.test/path?q=1");
        var routes = await PacEvaluator.EvaluateAsync("""
            function FindProxyForURL(url, host) {
                if (typeof System !== 'undefined' || typeof importNamespace !== 'undefined') throw 'CLR exposed';
                if (!dnsDomainIs(host, '.test') || !shExpMatch(url, '*path?q=?') || isPlainHostName(host)) throw 'helpers';
                if (!localHostOrDomainIs('example', host) || dnsDomainLevels(host) !== 1) throw 'hosts';
                if (!isResolvable('localhost')) throw 'async DNS';
                if (!isInNet('127.0.0.1', '127.0.0.0', '255.0.0.0') || !isResolvable('127.0.0.1')) throw 'network';
                if (!weekdayRange('SUN','SAT','GMT') || !timeRange(0,23,'GMT') || !dateRange('JAN','DEC','GMT')) throw 'time';
                return 'PROXY localhost:8080; SOCKS5 localhost:1080; DIRECT';
            }
            """, destination, default);
        AssertEqual(3, routes.Length); AssertEqual("http", routes[0]!.Scheme); AssertEqual("socks5", routes[1]!.Scheme); AssertTrue(routes[2] is null);
        foreach (string invalid in new[] { "", "BROKEN host:80", "PROXY user:password@host:80", "PROXY host:80/path", "DIRECT garbage", "SOCKS5 host", "PROXY localhost:0" })
        { bool rejected = false; try { PacEvaluator.ParseResult(invalid); } catch (InvalidDataException) { rejected = true; } AssertTrue(rejected, invalid); }
        foreach (string script in new[] { "function FindProxyForURL() { while(true) {} }", "function FindProxyForURL() { return eval('123'); }", "function FindProxyForURL() { return 123; }" })
        {
            bool rejected = false;
            try { await PacEvaluator.EvaluateAsync(script, destination, default); }
            catch (Exception error) when (error is not OutOfMemoryException) { rejected = true; }
            AssertTrue(rejected);
        }
        using var stop = new CancellationTokenSource(); stop.Cancel();
        bool canceled = false; try { await PacEvaluator.EvaluateAsync("function FindProxyForURL(){return 'DIRECT';}", destination, stop.Token); }
        catch (OperationCanceledException) { canceled = true; }
        AssertTrue(canceled);
    }

    private static void ProxySettingsAndTransportSchemesAgree()
    {
        var definition = SettingsPolicySchema.Definitions.Single(item => item.Key == "network.proxy-address");
        foreach (string address in new[] { "http://localhost:7897", "https://localhost:443", "socks4://localhost:1080", "socks5://localhost:1080", "pac+https://example.test/proxy.pac?v=1", "pac+http://127.0.0.1:8080/proxy.pac" })
        { AssertTrue(NetworkPreferences.IsValidProxyAddress(address)); AssertTrue(definition.Validate(new(SettingsOverrideMode.Custom, address)) is null); }
        foreach (string address in new[] { "file:///tmp/test.pac", "pac+file:///tmp/test.pac", "pac+https://user:secret@host/test", "socks4://host:1080/path", "http://host:80#fragment", "pac+https://host/test#fragment" })
        { AssertFalse(NetworkPreferences.IsValidProxyAddress(address)); AssertTrue(definition.Validate(new(SettingsOverrideMode.Custom, address)) is not null); }
        foreach (string scheme in new[] { "http", "https", "socks4", "socks5" })
        {
            using var handler = NetworkHttpClientPool.CreateSocketsHandler(new() { ProxyMode = 2, ProxyAddress = scheme + "://localhost:7897", ProxyUser = "user", ProxyPassword = "secret" }, false);
            AssertEqual(scheme, handler.Proxy!.GetProxy(new Uri("https://example.test"))!.Scheme);
            AssertEqual("secret", handler.Proxy.Credentials!.GetCredential(new Uri("http://localhost:7897"), "Basic")!.Password);
        }
        using var system = NetworkHttpClientPool.CreateSocketsHandler(new(), false);
        using var direct = NetworkHttpClientPool.CreateSocketsHandler(new() { ProxyMode = 0 }, false);
        AssertTrue(system.UseProxy && system.Proxy is null); AssertFalse(direct.UseProxy);
    }

    private static async ValueTask PacBootstrapBoundsActualBytesAndCancellation()
    {
        int requests = 0;
        using var resolver = new PacResolver(new("https://pac.test/config"), new PacTestHandler((request, _) =>
        {
            requests++; AssertFalse(request.Headers.Contains("Authorization")); AssertFalse(request.Headers.Contains("Proxy-Authorization"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("function FindProxyForURL(url,host){return 'DIRECT';}") });
        }));
        AssertTrue((await resolver.ResolveAsync(new("https://example.test/a"), default))[0] is null);
        await resolver.ResolveAsync(new("https://example.test/b"), default); AssertEqual(1, requests);
        using var oversized = new PacResolver(new("https://pac.test/config"), new PacTestHandler((_, _) =>
        {
            var content = new StreamContent(new MemoryStream(new byte[PacEvaluator.MaximumSourceBytes + 1]));
            content.Headers.ContentLength = 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        bool rejected = false; try { await oversized.ResolveAsync(new("https://example.test"), default); } catch (HttpRequestException) { rejected = true; }
        AssertTrue(rejected);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        bool canceled = false; try { await resolver.ResolveAsync(new("https://example.test"), stop.Token); } catch (OperationCanceledException) { canceled = true; }
        AssertTrue(canceled);
    }

    private static async ValueTask PacRedirectSelectsRoutesAndStripsCredentials()
    {
        List<string> used = [];
        var resolver = ScriptResolver("function FindProxyForURL(url,host) { return host === 'a.test' ? 'PROXY first.test:80' : 'SOCKS5 second.test:1080'; }");
        using var client = new HttpClient(new PacTransportHandler(resolver, endpoint => new PacTestHandler((request, _) =>
        {
            used.Add(endpoint!.Host);
            if (used.Count == 1) { AssertTrue(request.Headers.Authorization is not null); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://b.test/next") } }); }
            AssertTrue(request.Headers.Authorization is null); AssertFalse(request.Headers.Contains("Cookie"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("done") });
        }), true));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://a.test/start");
        request.Headers.Authorization = new("Bearer", "destination-secret"); request.Headers.Add("Cookie", "session=private");
        using var response = await client.SendAsync(request); AssertEqual("done", await response.Content.ReadAsStringAsync());
        AssertEqual("first.test,second.test", string.Join(',', used));
    }

    private static async ValueTask PacFallbackNeverReplaysPostsOrTlsFailures()
    {
        foreach (var (post, tls) in new[] { (false, false), (true, false), (false, true) })
        {
            int calls = 0;
            using var client = new HttpClient(new PacTransportHandler(ScriptResolver("function FindProxyForURL(){return 'PROXY first.test:80; DIRECT';}"), endpoint => new PacTestHandler((_, _) =>
            {
                calls++; if (endpoint is not null) throw new HttpRequestException(tls ? HttpRequestError.SecureConnectionError : HttpRequestError.ConnectionError, "fixture");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }), true));
            bool failed = false;
            try { using var response = post ? await client.PostAsync("http://example.test", new StringContent("body")) : await client.GetAsync("http://example.test"); }
            catch (HttpRequestException) { failed = true; }
            AssertEqual(post || tls, failed); AssertEqual(post || tls ? 1 : 2, calls);
        }
    }

    private static async ValueTask PacRealSocketPipelineFollowsCommittedRouting()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                using var socket = await listener.AcceptTcpClientAsync(stop.Token); await using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                string first = (await reader.ReadLineAsync(stop.Token))!;
                var headers = new List<string>();
                string? header;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(stop.Token))) headers.Add(header);
                if (i < 2) AssertFalse(headers.Any(value => value.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase)));
                if (i == 1)
                {
                    await stream.WriteAsync("HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=fixture\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), stop.Token);
                    continue;
                }
                if (i == 2) AssertTrue(headers.Contains("Proxy-Authorization: Basic dXNlcjpzZWNyZXQ="));
                string body = i == 0 ? $"function FindProxyForURL(){{return 'PROXY 127.0.0.1:{port}';}}" : "proxied";
                AssertTrue(i == 0 ? first.StartsWith("GET /proxy.pac ", StringComparison.Ordinal) : first.StartsWith("GET http://unresolvable.invalid/value ", StringComparison.Ordinal));
                byte[] response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                await stream.WriteAsync(response, stop.Token);
            }
        }, stop.Token);
        using var pool = new NetworkHttpClientPool(() => new() { ProxyMode = 2, ProxyAddress = $"pac+http://127.0.0.1:{port}/proxy.pac", ProxyUser = "user", ProxyPassword = "secret", DnsOverHttps = false });
        using var client = pool.CreateClient();
        AssertEqual("proxied", await client.GetStringAsync("http://unresolvable.invalid/value", stop.Token));
        await serving;
    }

    private static PacResolver ScriptResolver(string script) => new(new("https://pac.test/config"), new PacTestHandler((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(script) })));
    private sealed class PacTestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
}
