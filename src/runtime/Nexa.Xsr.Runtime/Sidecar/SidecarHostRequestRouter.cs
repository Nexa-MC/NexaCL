using Nexa.Sidecar.Protocol;

namespace Nexa.Xsr.Runtime;

public sealed record SidecarHostCaller(string PackageName, Guid SessionId);
public sealed record SidecarHostRequestRoute(uint ContractId, bool IsQuery, uint InputCodec, uint OutputCodec,
    TimeSpan Timeout, Func<SidecarHostCaller, bool> Authorize,
    Func<SidecarHostCaller, SidecarBinaryValue, CancellationToken, ValueTask<XsrResult<SidecarBinaryValue>>> Invoke);

/// <summary>Host-installed immutable routes. Registration by a Sidecar cannot grant access.</summary>
public sealed class SidecarHostRequestRouter
{
    private readonly int _maximumWork;
    private int _inflight;
    private readonly IReadOnlyDictionary<uint, SidecarHostRequestRoute> _routes;
    public SidecarHostRequestRouter(IEnumerable<SidecarHostRequestRoute> routes, int maximumConcurrentCalls = 1024)
    {
        ArgumentNullException.ThrowIfNull(routes);
        if (maximumConcurrentCalls is < 1 or > 16384) throw new ArgumentOutOfRangeException(nameof(maximumConcurrentCalls));
        _maximumWork = maximumConcurrentCalls;
        var entries = routes.ToArray();
        foreach (var route in entries)
        {
            if (route.ContractId == 0 || route.Timeout <= TimeSpan.Zero || route.Timeout > TimeSpan.FromMinutes(5))
                throw new ArgumentException("Host request routes need a nonzero ID and a bounded positive deadline.", nameof(routes));
            ArgumentNullException.ThrowIfNull(route.Authorize);
            ArgumentNullException.ThrowIfNull(route.Invoke);
        }
        _routes = entries.ToDictionary(route => route.ContractId);
    }
    internal bool TryEnter()
    {
        while (true)
        {
            int current = Volatile.Read(ref _inflight);
            if (current >= _maximumWork) return false;
            if (Interlocked.CompareExchange(ref _inflight, current + 1, current) == current) return true;
        }
    }
    internal void Exit() => Interlocked.Decrement(ref _inflight);
    internal SidecarHostRequestRoute? Resolve(uint id) => _routes.GetValueOrDefault(id);
}
