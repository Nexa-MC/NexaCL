using System.Reflection;
using Nexa.Platform.Updates;
using Nexa.Services.Updates;

namespace Nexa.Desktop;

internal static partial class Program
{
    private static bool SupportsPackagedUpdate() => new AutomaticUpdateHost().IsSystemInstallation
        || (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported && PortableUpdateInstallation.Root is not null);

    private static Action StartupUpdateRestart(string[] args)
    {
        var host = new AutomaticUpdateHost();
        if (host.IsSystemInstallation) return () => host.RestartLauncher(args);
        string root = PortableUpdateInstallation.Root ?? throw new IOException("Portable installation root is unavailable.");
        return () => PortableUpdateInstallation.Restart(root, args);
    }

    private static async Task<bool> UpdateBeforeSetupAsync(string[] args, string dataDirectory, Action<string> stage,
        Action<Exception> onFailure, CancellationToken token)
    {
        var build = ResolveBuildInfo();
        string rid = (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-"
            + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string seed = Path.Combine(dataDirectory, Nexa.Services.Files.FolderNames.Settings, "rollout-seed");
#if NEXA_NATIVE_SERVICES
        using var http = Nexa.Native.Network.Client.NativeHttpClients.CreateDefault(false);
        await using var service = new Nexa.Native.Updates.Client.NativeUpdatesClient(
            new(AppContext.BaseDirectory, SidecarStartup.ReadReleaseKey(), UpdateGpgVerifier.ReleaseKeyFingerprint), http);
        await service.ConfigureDiscoveryAsync(new(seed, build.Channel, rid, true), token).ConfigureAwait(false);
        var control = await CreateNativeUpdateControlAsync(service, build.ProductVersion, rid).ConfigureAwait(false);
#else
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var builder = new Nexa.Xsr.State.XsrStateStoreBuilder();
        Nexa.Services.Rollouts.RolloutStateContract.DeclareState(builder);
        await using var rollouts = new Nexa.Services.Rollouts.RolloutService(http, builder.Build(), seed, build.Channel, rid);
        var service = new NexaUpdateService(http, rollouts);
        var control = CreateUpdateControl(http, build.ProductVersion, rid);
#endif
        var result = await DesktopStartupUpdate.RunAsync(control, new(build.ProductVersion, rid, build.UpdateChannel),
            true, service.CheckAsync, stage, onFailure, token).ConfigureAwait(false);
        if (result.RestartRequired) _restartAfterExit = StartupUpdateRestart(args);
        return result.RestartRequired;
    }

#if NEXA_NATIVE_SERVICES
    private static async Task<IAutomaticUpdateControl> CreateNativeUpdateControlAsync(Nexa.Native.Updates.Client.NativeUpdatesClient client, string version, string rid)
    {
        var host = new AutomaticUpdateHost();
        string? root = host.IsSystemInstallation ? null : PortableUpdateInstallation.Root;
        await client.ConfigureControlAsync(new(root, new(version, rid, "nativeaot-self-contained", "Release"))).ConfigureAwait(false);
        return new NativeAutomaticUpdateControl(client, root is null ? host.RestartLauncher : () => PortableUpdateInstallation.Restart(root));
    }
    private sealed class NativeAutomaticUpdateControl(Nexa.Native.Updates.Client.NativeUpdatesClient client, Action restart) : IAutomaticUpdateControl
    {
        public Task<AutomaticUpdateStatus> ReadAsync(CancellationToken token) => client.ReadControlAsync(token);
        public Task<AutomaticUpdateStatus> InstallAsync(string version, string channel, CancellationToken token) => client.InstallControlAsync(version, channel, token);
        public Task<AutomaticUpdateStatus> RollbackAsync(CancellationToken token) => client.RollbackControlAsync(token);
        public void Restart() => restart();
    }
#else
    private static IAutomaticUpdateControl CreateUpdateControl(HttpClient http, string version, string rid)
    {
        var host = new AutomaticUpdateHost();
        if (host.IsSystemInstallation || PortableUpdateInstallation.Root is not { } root)
            return new AutomaticUpdateControl(host);
        using Stream embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("Nexa.Desktop.SidecarReleaseKey.asc")!;
        using var reader = new StreamReader(embedded);
        return new PortableUpdateControl(root, new(version, rid, "nativeaot-self-contained", "Release"),
            new UpdateGpgVerifier(reader.ReadToEnd()), new GitHubUpdateReleaseSource(http));
    }
#endif
}
