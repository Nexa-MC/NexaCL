using Nexa.Services.Files;
using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Install;

public sealed record MinecraftModpackIdentity(string ArchiveSha256, string Version, ContentFingerprint Fingerprint, string? OriginalArchive);
public sealed record MinecraftModpackIdentityQuery(string InstanceDirectory, string? OriginalArchivePath = null);

public interface IMinecraftInstallationPort : IMinecraftInstallExecutor
{
    event Action<string>? Installed;
    event Action<string, string, string>? Renamed;
    Task<XsrResult<MinecraftInstallResult>> InstallModpackAsync(MinecraftModpackCommand command, CancellationToken token = default);
    Task<MinecraftModpackUpdatePreview> PreviewModpackUpdateAsync(MinecraftModpackUpdateQuery query, CancellationToken token = default);
    Task<XsrResult<MinecraftInstallResult>> UpdateModpackAsync(MinecraftModpackUpdateCommand command, CancellationToken token = default);
    Task<MinecraftModpackPreview> InspectModpackAsync(string path, CancellationToken token = default);
    Task<MinecraftModpackIdentity?> ReadModpackIdentityAsync(MinecraftModpackIdentityQuery query, CancellationToken token = default);
    Task PrepareMigrationAsync(string root, CancellationToken token = default);
}
