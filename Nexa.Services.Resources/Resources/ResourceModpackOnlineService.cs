using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Minecraft.Install;

namespace Nexa.Services.Resources;

public sealed class ResourceModpackOnlineService(MinecraftInstallService installer, ResourceInstanceService instances,
    IResourceCatalogSource catalog, ResourceDownloadService downloader, ResourceOnlineInformationCache information, string archiveDirectory)
{

    public async Task<ResourceModpackOnline> ReadAsync(ResourceModpackOnlineQuery query, CancellationToken token = default)
    {
        var identity = await ReadIdentityAsync(query, token).ConfigureAwait(false);
        if (identity is null) return new(query, null, null, null, [], "请选择已安装版本的原整合包，以识别在线来源。");
        var hash = identity.Fingerprint;
        var matches = await instances.IdentifyAsync(new("source.pack", hash.Length, 0, hash.Sha512, hash.Sha1, hash.CurseForge, true),
            query.MirrorFirst, token, query.Refresh, waitForRefresh: query.Refresh).ConfigureAwait(false);
        foreach (var match in matches.Files)
        {
            try
            {
                var request = new ResourceDetailQuery(match.Source.ProjectId)
                { Sources = [match.Source], MirrorFirst = query.MirrorFirst, Refresh = query.Refresh, WaitForRefresh = query.Refresh };
                var detail = await catalog.DetailAsync(request, token).ConfigureAwait(false);
                // Old persistent detail entries did not retain the project kind.
                if (detail.Project.Kind != ResourceKind.Modpack && !query.Refresh && !detail.IsStale)
                    detail = await catalog.DetailAsync(request with { Refresh = true, WaitForRefresh = true }, token).ConfigureAwait(false);
                if (detail.Project.Kind != ResourceKind.Modpack || !detail.Project.Sources.Contains(match.Source)) continue;
                var installed = detail.Versions.FirstOrDefault(version => SameSource(version, match.Source) && version.Id == match.VersionId);
                if (installed is null && catalog is IResourceFileSource files)
                    installed = await files.ReadVersionAsync(new(match.Source.Provider, match.Source.ProjectId, match.VersionId, "", query.MirrorFirst)
                    { Refresh = query.Refresh }, token).ConfigureAwait(false);
                if (installed is not null && (!SameSource(installed, match.Source) || installed.Id != match.VersionId)) throw new InvalidDataException("已安装整合包的在线版本身份不匹配。");
                bool stale = matches.IsStale || detail.IsStale;
                var newer = installed is not null && Publication(installed) is { } published
                    ? detail.Versions.Where(version => SameSource(version, match.Source) && version.Id != installed.Id && PackFile(version.File)
                        && Publication(version) is { } date && date > published && !SameBytes(version.File!, hash))
                        .OrderByDescending(Publication).Take(50).ToArray() : [];
                string? notice = detail.Notice;
                if (stale) notice = ResourceOnlineInformationCache.Notice(notice);
                else if (installed is null || Publication(installed) is null) notice = "已识别整合包来源，但尚不能确认已安装版本的发布时间。";
                return new(query, identity.ArchiveSha256, detail.Project, installed, Array.AsReadOnly(newer), notice)
                { IsStale = stale, UpdateVersion = stale ? null : newer.FirstOrDefault(), OriginalArchivePath = identity.OriginalArchive ?? query.OriginalArchivePath };
            }
            catch (Exception error) when (!token.IsCancellationRequested && ResourceOnlineInformationCache.Recoverable(error)) { }
        }
        return new(query, identity.ArchiveSha256, null, null, [], matches.Files.Length > 0
            ? "已识别整合包来源，项目资料暂时不可用，请稍后刷新。" : matches.Complete
                ? "资源站尚未收录此整合包，可继续使用本地压缩包更新。" : "暂时无法连接整合包来源，请稍后刷新。")
        { IsStale = matches.IsStale, OriginalArchivePath = identity.OriginalArchive ?? query.OriginalArchivePath };
    }

    public async Task<ResourceModpackPreview> PreviewAsync(ResourceModpackPreviewQuery query, CancellationToken token = default)
    {
        var (online, version) = await ReadSelectionAsync(query, token).ConfigureAwait(false);
        string archive = await DownloadArchiveAsync(version, query.Instance.MirrorFirst, token).ConfigureAwait(false);
        var installation = await installer.PreviewModpackUpdateAsync(new(query.Instance.InstanceDirectory, archive,
            online.OriginalArchivePath, query.IncludeOptional, query.ReplaceModifiedMods), token).ConfigureAwait(false);
        return new(query, version, installation);
    }

    public async Task<Nexa.Xsr.XsrResult<MinecraftInstallResult>> UpdateAsync(ResourceModpackUpdateCommand command, CancellationToken token = default)
    {
        var preview = command.Preview;
        var (_, version) = await ReadSelectionAsync(preview.Request, token).ConfigureAwait(false);
        if (!SameArtifact(version.File, preview.Version.File)) throw new IOException("在线版本文件已变化，请重新预览。");
        string expected = ArchivePath(version);
        if (!Nexa.Core.PathIdentity.Comparer.Equals(expected, preview.Installation.Request.ArchivePath)
            || !Nexa.Core.PathIdentity.Comparer.Equals(Nexa.Core.PathIdentity.Normalize(preview.Request.Instance.InstanceDirectory), preview.Installation.Request.InstanceDirectory)
            || preview.Installation.Request.IncludeOptional != preview.Request.IncludeOptional
            || preview.Installation.Request.ReplaceModifiedMods != preview.Request.ReplaceModifiedMods)
            throw new InvalidDataException("在线更新预览不属于当前选择。");
        if (!await VerifiedAsync(expected, version.File!, token).ConfigureAwait(false)) throw new IOException("在线整合包缓存已变化，请重新预览。");
        return await installer.UpdateModpackAsync(new(preview.Installation), token).ConfigureAwait(false);
    }

    private async Task<(ResourceModpackOnline, ResourceVersion)> ReadSelectionAsync(ResourceModpackPreviewQuery query, CancellationToken token)
    {
        var online = await ReadAsync(query.Instance with { Refresh = true }, token).ConfigureAwait(false);
        if (online.IsStale || online.InstalledArchiveSha256 != query.InstalledArchiveSha256 || online.Project?.Sources.Contains(query.Source) != true)
            throw new IOException("已安装整合包或在线来源已变化，请刷新在线信息。");
        var candidate = online.Versions.FirstOrDefault(version => SameSource(version, query.Source) && version.Id == query.VersionId)
            ?? throw new IOException("所选版本不再是可用的新版本，请刷新在线信息。");
        if (catalog is not IResourceFileSource files) throw new IOException("此来源不支持在线更新。");
        var version = await files.ReadVersionAsync(new(query.Source.Provider, query.Source.ProjectId, query.VersionId, "", query.Instance.MirrorFirst)
        { Refresh = true }, token).ConfigureAwait(false);
        if (version is null || !SameSource(version, query.Source) || version.Id != query.VersionId || !PackFile(version.File)
            || !SameArtifact(candidate.File, version.File)) throw new IOException("所选整合包的下载信息已变化，请刷新后重试。");
        return (online, version);
    }

    private async Task<ModpackOnlineIdentity?> ReadIdentityAsync(ResourceModpackOnlineQuery query, CancellationToken token)
    {
        string instance = Nexa.Core.PathIdentity.Normalize(query.InstanceDirectory);
        if (Directory.GetParent(instance) is not { Name: "versions", Parent: not null }) throw new InvalidDataException("请选择实际版本目录。");
        var baseline = await ModpackManagedManifestStore.ReadAsync(instance, token).ConfigureAwait(false);
        if (baseline?.ArchiveIdentity is { } fingerprint) return new(baseline.ArchiveSha256, baseline.Version, fingerprint, null);
        string original = query.OriginalArchivePath ?? "";
        string stamp = "";
        if (original.Length > 0)
        {
            ResourceOnlineInformationCache.CheckLinks(original);
            var file = new FileInfo(original); stamp = file.Length + ":" + file.LastWriteTimeUtc.Ticks;
        }
        var result = await information.ReadAsync("modpack-archive-identity", ResourceOnlineInformationCache.Identity(instance, baseline?.ArchiveSha256 ?? "", original, stamp),
            ResourceOnlineInformationCache.MetadataFresh, ResourceOnlineInformationCache.MetadataRetain, 2048,
            ResourceMetadataJsonContext.Default.ResourceMetadataEnvelopeResourceModpackIdentityMetadata,
            async ct => new ResourceModpackIdentityMetadata(await installer.ReadModpackIdentityAsync(instance, query.OriginalArchivePath, ct).ConfigureAwait(false)),
            value => value.Identity is { Fingerprint: { Sha512.Length: 128, Sha1.Length: 40 } } id && id.Fingerprint.Sha256 == id.ArchiveSha256,
            value => value, value => value, allowStale: false, token: token).ConfigureAwait(false);
        return result.Identity;
    }

    private string ArchivePath(ResourceVersion version)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ResourceOnlineInformationCache.Identity(
            version.Provider.ToString(), version.ProjectId, version.Id, version.File!.Name, version.File.Sha512?.ToUpperInvariant() ?? "", version.File.Sha1?.ToUpperInvariant() ?? ""))));
        return Nexa.Core.PathIdentity.Contained(Path.GetFullPath(archiveDirectory), key + "/" + version.File.Name);
    }

    private async Task<string> DownloadArchiveAsync(ResourceVersion version, bool mirror, CancellationToken token)
    {
        var result = await information.ReadAsync("modpack-archive", ArchivePath(version), ResourceOnlineInformationCache.CatalogFresh,
            ResourceOnlineInformationCache.CatalogRetain, 2048, ResourceMetadataJsonContext.Default.ResourceMetadataEnvelopeResourceModpackArchiveMetadata,
            async ct => new ResourceModpackArchiveMetadata(await DownloadArchiveCoreAsync(version, mirror, ct).ConfigureAwait(false)),
            value => value.Path == ArchivePath(version), value => value, value => value, refresh: true, persistent: false,
            allowStale: false, token: token).ConfigureAwait(false);
        return result.Path;
    }
    private async Task<string> DownloadArchiveCoreAsync(ResourceVersion version, bool mirror, CancellationToken token)
    {
        string? temporary = null;
        try
        {
            string destination = ArchivePath(version); ResourceOnlineInformationCache.CheckLinks(destination);
            if (await VerifiedAsync(destination, version.File!, token).ConfigureAwait(false)) return destination;
            string root = Path.GetFullPath(archiveDirectory); ResourceOnlineInformationCache.CheckLinks(root); Directory.CreateDirectory(root);
            temporary = Path.Combine(root, "staging-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
            await downloader.DownloadAsync(new(version.Provider, version.ProjectId, version.Id, temporary, mirror) { Refresh = true }, token).ConfigureAwait(false);
            string downloaded = Path.Combine(temporary, version.File!.Name);
            if (!await VerifiedAsync(downloaded, version.File, token).ConfigureAwait(false)) throw new IOException("在线整合包与预览版本不一致，请重新预览。");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); ResourceOnlineInformationCache.CheckLinks(destination);
            File.Move(downloaded, destination, overwrite: true);
            PruneArchives(destination);
            return destination;
        }
        finally
        {
            if (temporary is not null) { try { ResourceOnlineInformationCache.CheckLinks(temporary); Directory.Delete(temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
    private void PruneArchives(string keep)
    {
        try
        {
            var files = Directory.EnumerateDirectories(Path.GetFullPath(archiveDirectory)).Where(path => Path.GetFileName(path) is { Length: 64 } name && name.All(char.IsAsciiHexDigit))
                .SelectMany(path => { ResourceOnlineInformationCache.CheckLinks(path); return Directory.EnumerateFiles(path); })
                .Where(path => !Nexa.Core.PathIdentity.Comparer.Equals(path, keep)).Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
            long bytes = new FileInfo(keep).Length;
            for (int i = 0; i < files.Length; i++)
            { bytes += files[i].Length; if (i >= 7 || bytes > 8L * 1024 * 1024 * 1024) { ResourceOnlineInformationCache.CheckLinks(files[i].FullName); files[i].Delete(); } }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "CurseForge publishes SHA1 identities. Prefer SHA512 when available, and compare the exact length and archive SHA256 at application.")]
    private static async Task<bool> VerifiedAsync(string path, ResourceFile file, CancellationToken token)
    {
        ResourceOnlineInformationCache.CheckLinks(path);
        if (!File.Exists(path)) return false;
        await using var input = File.OpenRead(path);
        if (input.Length != file.Size) return false;
        string? digest = file.Sha512 is { Length: 128 } ? file.Sha512 : file.Sha1;
        if (digest is null || digest.Length is not (40 or 128) || !digest.All(char.IsAsciiHexDigit)) return false;
        var actual = digest.Length == 128 ? await SHA512.HashDataAsync(input, token).ConfigureAwait(false) : await SHA1.HashDataAsync(input, token).ConfigureAwait(false);
        return Convert.ToHexString(actual).Equals(digest, StringComparison.OrdinalIgnoreCase);
    }
    private static bool SameSource(ResourceVersion version, ResourceReference source) => version.Provider == source.Provider && version.ProjectId == source.ProjectId;
    private static bool PackFile(ResourceFile? file) => file is { Size: > 0 and <= 2L * 1024 * 1024 * 1024 }
        && Nexa.Services.Minecraft.MinecraftVersionPaths.IsSafeReference(file.Name)
        && (file.Sha512 is { Length: 128 } sha512 && sha512.All(char.IsAsciiHexDigit) || file.Sha1 is { Length: 40 } sha1 && sha1.All(char.IsAsciiHexDigit))
        && Path.GetExtension(file.Name).ToLowerInvariant() is ".mrpack" or ".zip";
    private static bool SameArtifact(ResourceFile? a, ResourceFile? b) => a is not null && b is not null && a.Name == b.Name && a.Size == b.Size
        && string.Equals(a.Sha512, b.Sha512, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Sha1, b.Sha1, StringComparison.OrdinalIgnoreCase);
    private static bool SameBytes(ResourceFile file, Nexa.Services.Files.ContentFingerprint fingerprint) =>
        file.Sha512?.Equals(fingerprint.Sha512, StringComparison.OrdinalIgnoreCase) == true || file.Sha1?.Equals(fingerprint.Sha1, StringComparison.OrdinalIgnoreCase) == true;
    private static DateTimeOffset? Publication(ResourceVersion version) => DateTimeOffset.TryParse(version.Published,
        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date) ? date : null;
}
