using System.Net;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Nexa.Native.Pac;

public static partial class PacEngine
{
    public sealed class LookupRequiredException(string host) : Exception
    { public string Host { get; } = host; }

    private sealed class Cancellation(Func<bool> canceled) : Constraint
    {
        public override void Check() { if (canceled()) throw new OperationCanceledException(); }
        public override void Reset() { }
    }

    public static string Evaluate(string source, Uri destination, Dictionary<string, string?> resolved, Func<bool> canceled)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(canceled);
        if (source.Length > 1024 * 1024) throw new InvalidDataException("PAC source exceeds its limit.");
        if (canceled()) throw new OperationCanceledException();
        using var engine = new Engine(options =>
        {
            options.LimitMemory(16 * 1024 * 1024).MaxStatements(50_000)
                .TimeoutInterval(TimeSpan.FromSeconds(2)).LimitRecursion(64)
                .Constraint(new Cancellation(canceled)).DisableStringCompilation();
            options.Interop.Enabled = false;
            options.AgentCanSuspend = false;
            options.Constraints.RegexTimeout = TimeSpan.FromMilliseconds(100);
        });
        string? Resolve(string host)
        {
            if (canceled()) throw new OperationCanceledException();
            if (host.Length > 253) return null;
            if (IPAddress.TryParse(host, out var address)) return address.ToString();
            if (resolved.TryGetValue(host, out string? cached)) return cached;
            throw new LookupRequiredException(host);
        }
        engine.SetValue("dnsResolve", (JsValue)new ClrFunction(engine, "dnsResolve", (_, args) =>
            Resolve(args.Length == 0 ? "" : args[0].ToString()) is { } address ? (JsValue)address : JsValue.Null));
        engine.SetValue("myIpAddress", (JsValue)new ClrFunction(engine, "myIpAddress", (_, _) =>
            (JsValue)(Resolve(Dns.GetHostName()) ?? "127.0.0.1")));
        engine.Execute(Helpers);
        engine.Execute(source);
        var result = engine.Call("FindProxyForURL", [(JsValue)destination.AbsoluteUri, (JsValue)destination.IdnHost]);
        if (!result.IsString()) throw new InvalidDataException("PAC must return a proxy list.");
        return result.AsString();
    }
}
