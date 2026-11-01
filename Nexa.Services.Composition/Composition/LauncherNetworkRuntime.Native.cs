#if NEXA_NATIVE_SERVICES
using Nexa.Native.Updates.Client;
using Nexa.Services.Foundation;
using Nexa.Services.Updates;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

public sealed class LauncherNetworkRuntime : IAsyncDisposable
{
    public HttpClient Http { get; }
    public string RuntimeId { get; }
    public NativeUpdatesClient Updates { get; }
    public NativeLauncherRollouts Rollouts { get; }
    public XsrQueryRouter Queries { get; }
    public Task Initialization { get; }

    public LauncherNetworkRuntime(FoundationHost host, string settingsFolder, LauncherBuildIdentity build, string pinnedPublicKey)
    {
        Http = host.CreateHttpClient(allowAutoRedirect: false);
        Http.Timeout = TimeSpan.FromSeconds(20);
        RuntimeId = (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-"
            + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        Updates = new(new(AppContext.BaseDirectory, pinnedPublicKey, UpdateGpgVerifier.ReleaseKeyFingerprint), Http, host.Work,
            error => host.Logging.Warn("Update", error.Message));
        Updates.AttachRolloutReplicas(host.StateStore);
        Initialization = Updates.ConfigureDiscoveryAsync(new(Path.Combine(settingsFolder, "rollout-seed"), build.Channel, RuntimeId, true));
        Rollouts = new(Updates, Initialization);
        Queries = NexaUpdateRuntimeComposer.Compose(Updates);
    }
    public async ValueTask DisposeAsync()
    {
        try { await Updates.DisposeAsync().ConfigureAwait(false); }
        finally { Http.Dispose(); }
    }
}

public sealed class NativeLauncherRollouts(NativeUpdatesClient client, Task initialization) : IUpdateTelemetrySource
{
    public bool CompactTelemetryBatches => client.CompactTelemetryBatches;
    public Action<string, string>? Record { get => client.Record; set => client.Record = value; }
    public async Task StartAsync(CancellationToken token = default)
    {
        await initialization.WaitAsync(token).ConfigureAwait(false);
        await client.StartRolloutsAsync(token).ConfigureAwait(false);
    }
}
#endif
