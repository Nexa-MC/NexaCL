using Nexa.Services.Files;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Minecraft.Install;

internal sealed record ModpackOnlineIdentity(string ArchiveSha256, string Version, ContentFingerprint Fingerprint, string? OriginalArchive);

public sealed partial class MinecraftInstallService
{
    internal async Task<ModpackOnlineIdentity?> ReadModpackIdentityAsync(string instance, string? original, CancellationToken token)
    {
        instance = Nexa.Core.PathIdentity.Normalize(instance);
        if (Directory.GetParent(instance) is not { Name: "versions", Parent: { } root }
            || !MinecraftVersionPaths.IsSafeReference(Path.GetFileName(instance))) throw new InvalidDataException("请选择实际版本目录。");
        RecoveryBlobStore.CheckLinks(instance);
        var baseline = await ModpackManagedManifestStore.ReadAsync(instance, token).ConfigureAwait(false);
        if (baseline?.ArchiveIdentity is { } fingerprint) return new(baseline.ArchiveSha256, baseline.Version, fingerprint, null);
        if (!string.IsNullOrWhiteSpace(original)) return await Identify(original).ConfigureAwait(false);
        string jobs = Path.Combine(root.FullName, ModpackInstallJournal.DirectoryName);
        RecoveryBlobStore.CheckLinks(jobs);
        if (!Directory.Exists(jobs)) return null;
        foreach (string stage in Directory.EnumerateDirectories(jobs).Take(256))
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(stage), "N", out _)) continue;
            try
            {
                var journal = await ModpackInstallJournal.OpenAsync(root.FullName, stage, token).ConfigureAwait(false);
                if (journal.Complete && (baseline is not null ? journal.Command.Pack.Sha256.Equals(baseline.ArchiveSha256, StringComparison.OrdinalIgnoreCase)
                    : journal.Command.Pack.InstanceId == Path.GetFileName(instance)))
                {
                    string archive = File.Exists(journal.Archive) ? journal.Archive : journal.Command.Pack.Path;
                    if (File.Exists(archive)) return await Identify(archive).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return null;

        async Task<ModpackOnlineIdentity> Identify(string path)
        {
            var pack = await MinecraftModpackArchive.InspectAsync(path, token).ConfigureAwait(false);
            var metadata = await _instanceMetadataForPack.LoadAsync(instance, token).ConfigureAwait(false);
            if (baseline is not null ? !baseline.ArchiveSha256.Equals(pack.Sha256, StringComparison.OrdinalIgnoreCase)
                : metadata.ModpackVersion.Length > 0 && metadata.ModpackVersion != pack.Version)
                throw new InvalidDataException("原整合包与已安装记录不匹配。");
            var identity = await ModpackManagedManifestStore.ReadArchiveIdentityAsync(pack.Path, pack.Sha256, token).ConfigureAwait(false);
            return new(pack.Sha256, pack.Version, identity, pack.Path);
        }
    }
}
