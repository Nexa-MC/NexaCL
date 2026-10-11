using System.Net;
using System.Net.Sockets;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Nexa.Services.Network;

/// <summary>Portable PAC interpreter. Only strings cross the JS boundary; CLR access stays disabled.</summary>
internal static partial class PacEvaluator
{
    internal const int MaximumSourceBytes = 1024 * 1024;
    internal static async Task<Uri?[]> EvaluateAsync(string source, Uri destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source.Length > MaximumSourceBytes) throw new InvalidDataException("PAC source exceeds its limit.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        var resolved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            try { return Evaluate(source, destination, resolved, deadline.Token); }
            catch (LookupRequired lookup)
            {
                if (resolved.Count == 8) throw new InvalidDataException("PAC DNS lookup limit exceeded.");
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(lookup.Host, deadline.Token).ConfigureAwait(false);
                    resolved[lookup.Host] = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?.ToString();
                }
                catch (SocketException) { resolved[lookup.Host] = null; }
            }
        }
    }

    // PAC helpers are synchronous JavaScript. Suspend on an uncached DNS name, resolve it
    // asynchronously, then replay this side-effect-free policy with a per-evaluation DNS cache.
    private sealed class LookupRequired(string host) : Exception
    { internal string Host { get; } = host; }

    private static Uri?[] Evaluate(string source, Uri destination, Dictionary<string, string?> resolved, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var engine = new Engine(options =>
        {
            options.LimitMemory(16 * 1024 * 1024).MaxStatements(50_000)
                .TimeoutInterval(TimeSpan.FromSeconds(2)).LimitRecursion(64)
                .CancellationToken(token).DisableStringCompilation();
            options.Interop.Enabled = false;
            options.AgentCanSuspend = false;
            options.Constraints.RegexTimeout = TimeSpan.FromMilliseconds(100);
        });
        string? Resolve(string host)
        {
            token.ThrowIfCancellationRequested();
            if (host.Length > 253) return null;
            if (IPAddress.TryParse(host, out var address)) return address.ToString();
            if (resolved.TryGetValue(host, out string? cached)) return cached;
            throw new LookupRequired(host);
        }
        engine.SetValue("dnsResolve", (JsValue)new ClrFunction(engine, "dnsResolve", (_, args) =>
            Resolve(args.Length == 0 ? "" : args[0].ToString()) is { } address ? (JsValue)address : JsValue.Null));
        engine.SetValue("myIpAddress", (JsValue)new ClrFunction(engine, "myIpAddress", (_, _) =>
            (JsValue)(Resolve(Dns.GetHostName()) ?? "127.0.0.1")));
        engine.Execute(Helpers);
        engine.Execute(source);
        var result = engine.Call("FindProxyForURL", [(JsValue)destination.AbsoluteUri, (JsValue)destination.IdnHost]);
        if (!result.IsString()) throw new InvalidDataException("PAC must return a proxy list.");
        return ParseResult(result.AsString());
    }

    internal static Uri?[] ParseResult(string text)
    {
        if (text.Length is 0 or > 4096) throw new InvalidDataException("Invalid PAC result size.");
        var routes = new List<Uri?>();
        foreach (string raw in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (routes.Count == 16) throw new InvalidDataException("PAC proxy list exceeds its limit.");
            if (raw.Equals("DIRECT", StringComparison.OrdinalIgnoreCase)) { routes.Add(null); continue; }
            var parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string? scheme = parts[0].ToUpperInvariant() switch
            { "PROXY" or "HTTP" => "http", "HTTPS" => "https", "SOCKS" or "SOCKS4" => "socks4", "SOCKS5" => "socks5", _ => null };
            if (parts.Length != 2 || scheme is null
                || !NetworkPreferences.IsValidProxyAddress(scheme + "://" + parts[1]))
                throw new InvalidDataException("PAC returned an invalid proxy endpoint.");
            var endpoint = new Uri(scheme + "://" + parts[1]);
            if (endpoint.Port <= 0) throw new InvalidDataException("PAC endpoint requires a port.");
            routes.Add(endpoint);
        }
        if (routes.Count == 0) throw new InvalidDataException("PAC returned no routes.");
        return routes.ToArray();
    }
}
