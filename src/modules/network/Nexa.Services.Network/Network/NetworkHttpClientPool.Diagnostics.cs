using System.Diagnostics;

#if NEXA_NETWORK_SERVICE_OWNER
using Nexa.Services.Network;
namespace Nexa.Native.Network.Implementation;
#else
namespace Nexa.Services.Network;
#endif

public sealed partial class NetworkHttpClientPool
{
    private static readonly HttpRequestOptionsKey<bool> ProbeOption = new("Nexa.Network.DiagnosticProbe");
    private readonly Queue<NetworkRequestTrace> _trace = new();
    private readonly CancellationTokenSource _diagnosticStop = new();
    private readonly object _traceGate = new();
    private readonly object _diagnosticGate = new();
    private Task _diagnostics = Task.CompletedTask;
    private Task? _diagnosticRetirement;
    private long _traceSequence, _lastProbeTick;
    private int _probing;
    public IReadOnlyList<NetworkRequestTrace> TraceSnapshot()
    {
        NetworkPreferences policy = _capture();
        lock (_traceGate)
        {
            if (!policy.TraceEnabled && !policy.AutoDiagnose) _trace.Clear();
            return _trace.ToArray();
        }
    }

    private void RecordTrace(NetworkPreferences policy, HttpRequestMessage request, bool probe, long started, int? status, string? error)
    {
        lock (_traceGate)
        {
            if (!policy.TraceEnabled && !policy.AutoDiagnose) { _trace.Clear(); return; }
            if (_trace.Count == 256) _trace.Dequeue();
            _trace.Enqueue(new(++_traceSequence, DateTimeOffset.UtcNow, request.RequestUri!.IdnHost,
                probe ? "Probe" : "Request", status, Stopwatch.GetElapsedTime(started).TotalMilliseconds, error));
        }
    }

    private void StartDiagnosticProbe(Uri uri)
    {
        lock (_diagnosticGate)
        {
            if (_diagnosticRetirement is not null) return;
            long now = Environment.TickCount64;
            long prior = Volatile.Read(ref _lastProbeTick);
            if (prior != 0 && now - prior < 30000 || Interlocked.CompareExchange(ref _probing, 1, 0) != 0) return;
            Volatile.Write(ref _lastProbeTick, now);
            // A probe is a new anonymous request. In a native service the initiating
            // request's AsyncLocal policy must not override the latest committed policy.
            using (ExecutionContext.SuppressFlow())
                _diagnostics = Task.Run(() => ProbeAsync(new Uri(uri.GetLeftPart(UriPartial.Authority))));
        }
    }
    /// <summary>Physical retirement of automatic probes, after the pool is disposed.</summary>
    public Task WhenDrained { get { lock (_diagnosticGate) return _diagnosticRetirement ?? _diagnostics; } }
    private void StopDiagnostics()
    {
        lock (_diagnosticGate)
        {
            if (_diagnosticRetirement is not null) return;
            _diagnosticStop.Cancel();
            _diagnosticRetirement = DrainDiagnosticsAsync();
        }
    }
    private async Task DrainDiagnosticsAsync()
    {
        try { await _diagnostics.ConfigureAwait(false); }
        finally { _diagnosticStop.Dispose(); }
    }
    private async Task ProbeAsync(Uri endpoint)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(_diagnosticStop.Token);
            budget.CancelAfter(TimeSpan.FromSeconds(3));
            using HttpClient client = CreateClient(allowAutoRedirect: false);
            using var request = new HttpRequestMessage(HttpMethod.Head, endpoint);
            request.Options.Set(ProbeOption, true);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or ObjectDisposedException or IOException) { }
        finally { Volatile.Write(ref _probing, 0); }
    }
}
