using Nexa.Sidecar.Host.ThemeActivation.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Sidecar.Host;

/// <summary>Typed clients for the Host's general domain routes, independent of any extension SDK.</summary>
public sealed partial class SidecarThemeActivationClient
{
    private readonly SidecarHostRequestClient _requests;
    private IReadOnlySet<uint>? _available;
    public XsrCommandRouter Commands { get; }
    public XsrQueryRouter Queries { get; }
    public SidecarThemeActivationClient(SidecarHostRequestClient requests, IXsrDispatchObserver observer)
    {
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        XsrCommandRouterBuilder commands = new(); XsrQueryRouterBuilder queries = new();
        Register(commands, queries);
        Commands = commands.Build(observer); Queries = queries.Build(observer);
    }
    /// <summary>Confirm the exact wire schema and discover authorized operations before business calls.</summary>
    public async ValueTask<XsrResult> DiscoverAsync(CancellationToken token = default)
    {
        Volatile.Write(ref _available, null);
        var reply = await _requests.InvokeAsync(2303, true, new(SidecarWireCodecs.GeneratedDto, []),
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
        if (!reply.Success) return XsrResult.Failure(Error(reply.ErrorCode));
        using var reader = new ServiceWireReader(reply.Value.ToArray());
        if (reader.UInt32() != 1 || reader.String() != ServiceWireSchema.Fingerprint + ":" + CatalogFingerprint)
            return XsrResult.Failure(XsrRuntimeErrors.ContractMismatch());
        int count = reader.Count();
        if (count is < 0 or > 4096) throw new InvalidDataException("Invalid Host capability count.");
        HashSet<uint> available = [];
        for (int i = 0; i < count; i++)
            if (!available.Add(reader.UInt32())) throw new InvalidDataException("Duplicate Host capability.");
        reader.End();
        Volatile.Write(ref _available, available);
        return XsrResult.Success();
    }
    private ValueTask<SidecarHostReply> Call(uint id, bool query, byte[] bytes, CancellationToken token)
        => Volatile.Read(ref _available)?.Contains(id) != true
            ? ValueTask.FromResult(new SidecarHostReply(false, new(SidecarWireCodecs.GeneratedDto, []), "xsr.route_not_found"))
            : _requests.InvokeAsync(id, query, new(SidecarWireCodecs.GeneratedDto, bytes),
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromMinutes(5), token);

    private static XsrError Error(string code)
    {
        var kind = code switch
        {
            "xsr.route_not_found" or "xsr.target_not_found" => XsrErrorKind.NotFound,
            "xsr.contract_mismatch" => XsrErrorKind.ContractMismatch,
            "xsr.cancelled" => XsrErrorKind.Cancelled,
            "xsr.timed_out" => XsrErrorKind.TimedOut,
            "xsr.permission_denied" or "xsr.rejected" => XsrErrorKind.Rejected,
            "xsr.backpressure" => XsrErrorKind.Backpressure,
            "xsr.unavailable" => XsrErrorKind.Unavailable,
            _ => XsrErrorKind.Faulted
        };
        return new(kind, XsrSemanticId.Parse(code.Length == 0 ? "xsr.handler_faulted" : code), "Host operation failed.");
    }

    private static XsrError? ReadOutcome(ServiceWireReader reader)
    {
        if (reader.Boolean()) return null;
        var kind = (XsrErrorKind)reader.Int32();
        string code = reader.String() ?? throw new InvalidDataException("Missing Host error code.");
        reader.End();
        if (!Enum.IsDefined(kind)) throw new InvalidDataException("Invalid Host error kind.");
        return new(kind, XsrSemanticId.Parse(code), "Host operation failed.");
    }
}
