using Nexa.Sidecar.Host.Effects.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>Publishes explicitly selected domain capabilities to authenticated Sidecar sessions.</summary>
public sealed partial class SidecarEffectsApi
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (XsrCommandRouter? Commands, XsrQueryRouter? Queries)> _sources = new(StringComparer.Ordinal);
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private bool _ready;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }
    public SidecarHostRequestRouter Router { get; }

    public SidecarEffectsApi(Func<SidecarHostCaller, string, bool> authorize)
    {
        _authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(511, true, SidecarWireCodecs.GeneratedDto,
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromSeconds(10),
            caller => _authorize(caller, "host.effects.catalog"), (caller, input, _) =>
            {
                if (input.Span.Length != 0) throw new InvalidDataException("Catalog request must be empty.");
                lock (_gate)
                {
                    if (!_ready) return ValueTask.FromResult(XsrResult.Failure<SidecarBinaryValue>(new(XsrErrorKind.Unavailable,
                        XsrSemanticId.Parse("xsr.unavailable"), "Host domains are still composing.")));
                    using var writer = new ServiceWireWriter();
                    writer.UInt32(1); writer.String(ServiceWireSchema.Fingerprint + ":" + CatalogFingerprint);
                    var contracts = Available(caller); writer.Count(contracts.Count);
                    foreach (uint id in contracts) writer.UInt32(id);
                    return ValueTask.FromResult(XsrResult.Success(new SidecarBinaryValue(SidecarWireCodecs.GeneratedDto, writer.Finish())));
                }
            })).ToArray());
        Router = new(Routes);
    }

    /// <summary>Bind once after the domain has composed. Unbound domains return route_not_found.</summary>
    public void Bind(string domain, XsrCommandRouter? commands, XsrQueryRouter? queries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        if (commands is null && queries is null) throw new ArgumentException("A domain needs a router.", nameof(commands));
        lock (_gate)
        {
            if (_ready) throw new InvalidOperationException("Host domain publication is sealed.");
            if (!_sources.TryAdd(domain, (commands, queries))) throw new InvalidOperationException("Host domain already bound.");
        }
    }

    public void CompleteComposition() { lock (_gate) _ready = true; }

    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null);
        if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }

    private XsrCommandRouter? Commands(string domain) { lock (_gate) return _sources.GetValueOrDefault(domain).Commands; }
    private XsrQueryRouter? Queries(string domain) { lock (_gate) return _sources.GetValueOrDefault(domain).Queries; }
}
