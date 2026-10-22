using System.Reflection;
using Nexa.Platform.Updates;
using Nexa.Services.Updates;

namespace Nexa.Desktop;

internal static partial class Program
{
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
}
