using Nexa.Services.Foundation;
using Nexa.Services.Rollouts;
using Nexa.Services.Telemetry;
using Nexa.Services.Updates;

namespace Nexa.Services.Composition;

/// <summary>Owns anonymous telemetry transport and its shutdown order.</summary>
public sealed class LauncherTelemetryRuntime : IDisposable, IAsyncDisposable
{
    private readonly HttpClient _http;
    public LauncherTelemetrySession? Session { get; }

    public LauncherTelemetryRuntime(FoundationHost host, RolloutService rollouts, NexaUpdateService updates,
        string version)
    {
        _http = host.CreateHttpClient(allowAutoRedirect: false);
        _http.Timeout = TimeSpan.FromSeconds(15);
        Session = new(host.Telemetry, host.Settings, new CloudflareTelemetryTransport(_http,
            () => rollouts.CompactTelemetryBatches, reason => host.Logging.Warn("Telemetry", reason)), host.Logging, version, host.Work);
        host.Logging.AddSink(Session);
        rollouts.Record = Session.Record;
        updates.Record = Session.Record;
    }
    public void Dispose() { Session?.Dispose(); _http.Dispose(); }
    public async ValueTask DisposeAsync()
    {
        if (Session is not null) await Session.DisposeAsync().ConfigureAwait(false);
        _http.Dispose();
    }
}
