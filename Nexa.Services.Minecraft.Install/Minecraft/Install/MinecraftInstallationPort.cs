namespace Nexa.Services.Minecraft.Install;

public sealed partial class MinecraftInstallService
{
    public Task<MinecraftModpackPreview> InspectModpackAsync(string path, CancellationToken token = default)
        => MinecraftModpackArchive.InspectAsync(path, token);
    public async Task<MinecraftModpackIdentity?> ReadModpackIdentityAsync(MinecraftModpackIdentityQuery query, CancellationToken token = default)
    {
        var result = await ReadModpackIdentityAsync(query.InstanceDirectory, query.OriginalArchivePath, token).ConfigureAwait(false);
        return result is null ? null : new(result.ArchiveSha256, result.Version, result.Fingerprint, result.OriginalArchive);
    }
    public Task PrepareMigrationAsync(string root, CancellationToken token = default) => PrepareMigrationRootAsync(root, token);
}
