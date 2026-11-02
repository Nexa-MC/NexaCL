using System.Reflection;
using Nexa.Platform.Updates;
using Nexa.Services.Updates;

namespace Nexa.Desktop;

internal static partial class Program
{
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
