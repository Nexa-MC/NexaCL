using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.State;

namespace Nexa.Services.Resources;

public sealed class ResourceContentUpdateService(ResourceContentOnlineService content, ResourceDownloadService downloads, XsrStateStore store)
{
    public Task UpdateAsync(ResourceContentUpdateCommand command, CancellationToken token) => command.File.PageId == "mods"
        ? UpdateBatchAsync(new([command]), token) : UpdateOwnedAsync(new([command]), token, singlePack: true);

    public Task UpdateBatchAsync(ResourceContentUpdateBatchCommand command, CancellationToken token) => UpdateOwnedAsync(command, token);

    private async Task UpdateOwnedAsync(ResourceContentUpdateBatchCommand command, CancellationToken token,
        Nexa.Services.Tasks.ITaskCenterTask? retryTask = null, bool singlePack = false)
    {
        if (command.Updates.Count is 0 or > 100 || command.Updates.Any(item => item.File.InstanceDirectory != command.Updates[0].File.InstanceDirectory)
            || command.Updates.DistinctBy(item => (item.File.PageId, item.File.Name)).Count() != command.Updates.Count)
            throw new InvalidDataException("请选择同一实例的 1 至 100 个不同内容项。");
        using var task = retryTask ?? downloads.BeginUpdateTask((retry, cancellation) => UpdateOwnedAsync(command, cancellation, retry, singlePack));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, task.CancellationToken);
        try
        {
            string? notice = singlePack
                ? await UpdatePackCoreAsync(command.Updates[0], task, linked.Token).ConfigureAwait(false)
                : await UpdateBatchCoreAsync(command, task, linked.Token).ConfigureAwait(false);
            task.Complete("更新完成。" + notice);
        }
        catch (OperationCanceledException) { task.Canceled(); throw; }
        catch (Exception e) when (e is not OutOfMemoryException and not AccessViolationException) { task.Fail(e.Message); throw; }
    }

    private async Task<string?> UpdatePackCoreAsync(ResourceContentUpdateCommand command, Nexa.Services.Tasks.ITaskCenterTask task, CancellationToken token)
    {
        var query = command.File;
        if (query.PageId is not ("resourcepacks" or "shaderpacks")) throw new InvalidDataException("此更新入口支持模组、资源包和光影包。");
        var online = await content.ReadAsync(query, token).ConfigureAwait(false);
        var selected = online.Versions.SingleOrDefault(version => version.Id == command.VersionId && version.Provider == command.Source.Provider && version.ProjectId == command.Source.ProjectId);
        if (online.Project is not { } project || !project.Sources.Contains(command.Source) || selected?.File is not { } expected)
            throw new InvalidDataException("目标版本不属于当前文件或不兼容当前 Minecraft。");
        var snapshot = await InstanceManagementService.ReadAsync(new(query.InstanceDirectory), token).ConfigureAwait(false);
        string stage = Path.Combine(snapshot.GameDirectory, ".nexa-resource-" + Guid.NewGuid().ToString("N"));
        for (string? path = snapshot.GameDirectory; path is not null; path = Path.GetDirectoryName(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("不能更新链接目录中的资源。");
        Directory.CreateDirectory(stage);
        try
        {
            await downloads.DownloadInTransactionAsync(new(command.Source.Provider, command.Source.ProjectId, command.VersionId, stage, query.MirrorFirst), task.Report, token).ConfigureAwait(false);
            string downloaded = Directory.EnumerateFiles(stage).Single();
            await VerifySelectedFileAsync(downloaded, expected, token).ConfigureAwait(false);
            task.Report("应用更新", "", .99, 1, 1, 0);
            // Single pack updates retain their established trash/restore receipt contract.
            var result = await InstanceContentTrash.ReplacePackAsync(new(query.InstanceDirectory, query.PageId, query.Name, false,
                query.ExpectedSize, query.ExpectedModifiedUtcTicks), downloaded, Path.GetFileName(downloaded), store, token).ConfigureAwait(false);
            if (!result.IsSuccess) throw new IOException(result.Error?.Message ?? "资源更新未完成。");
            return null;
        }
        finally
        {
            if (Directory.Exists(stage) && (File.GetAttributes(stage) & FileAttributes.ReparsePoint) == 0)
            {
                foreach (string path in Directory.EnumerateFiles(stage)) File.Delete(path);
                Directory.Delete(stage);
            }
        }
    }

    private async Task<string?> UpdateBatchCoreAsync(ResourceContentUpdateBatchCommand command, Nexa.Services.Tasks.ITaskCenterTask task, CancellationToken token)
    {
        var snapshot = await InstanceManagementService.ReadAsync(new(command.Updates[0].File.InstanceDirectory), token).ConfigureAwait(false);
        List<string> stages = []; List<InstanceContentReplacement> replacements = [];
        string? dependencyNotice = null;
        try
        {
            foreach (var update in command.Updates)
            {
                if (update.File.PageId is not ("mods" or "resourcepacks" or "shaderpacks")) throw new InvalidDataException("内容类型不支持更新。");
                var online = await content.ReadAsync(update.File, token).ConfigureAwait(false);
                var selected = online.Versions.SingleOrDefault(version => version.Provider == update.Source.Provider && version.ProjectId == update.Source.ProjectId && version.Id == update.VersionId);
                if (online.Project is not { } project || !project.Sources.Contains(update.Source) || selected?.File is not { } expected)
                    throw new InvalidDataException("目标版本不属于当前文件或不兼容当前 Minecraft。");
                string stage = Path.Combine(snapshot.GameDirectory, ".nexa-resource-" + Guid.NewGuid().ToString("N"));
                for (string? path = snapshot.GameDirectory; path is not null; path = Path.GetDirectoryName(path))
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("不能更新链接目录中的资源。");
                Directory.CreateDirectory(stage); stages.Add(stage);
                int completed = replacements.Count;
                await downloads.DownloadInTransactionAsync(new(update.Source.Provider, update.Source.ProjectId, update.VersionId, stage, update.File.MirrorFirst),
                    (phase, detail, progress, _, _, speed) => task.Report(phase, detail,
                        (completed + progress) / (command.Updates.Count + 1d), completed, command.Updates.Count, speed), token).ConfigureAwait(false);
                string downloaded = Directory.EnumerateFiles(stage).Single();
                await VerifySelectedFileAsync(downloaded, expected, token).ConfigureAwait(false);
                replacements.Add(new(new(update.File.InstanceDirectory, update.File.PageId, update.File.Name, false, update.File.ExpectedSize,
                    update.File.ExpectedModifiedUtcTicks), downloaded, Path.GetFileName(downloaded)));
            }
            task.Report("应用更新", "", command.Updates.Count / (command.Updates.Count + 1d), command.Updates.Count, command.Updates.Count, 0);
            await InstanceContentUpdateTransaction.ApplyAsync(replacements, store,
                validate: async ct =>
                {
                    dependencyNotice = await ValidateUpdatedModsAsync(replacements, snapshot, ct).ConfigureAwait(false);
                    if (dependencyNotice is not null) task.Report("校验", dependencyNotice, .99, command.Updates.Count, command.Updates.Count, 0);
                }, token: token).ConfigureAwait(false);
            return dependencyNotice;
        }
        finally
        {
            foreach (string stage in stages)
                if (Directory.Exists(stage) && (File.GetAttributes(stage) & FileAttributes.ReparsePoint) == 0)
                {
                    foreach (string path in Directory.EnumerateFiles(stage)) File.Delete(path);
                    Directory.Delete(stage);
                }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "CurseForge publishes SHA1 identities; the download remains authenticated by HTTPS metadata.")]
    private static async Task VerifySelectedFileAsync(string path, ResourceFile expected, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (input.Length != expected.Size) throw new IOException("下载期间目标版本文件发生变化，请重新读取在线信息。");
        string? digest = expected.Sha512 is { Length: 128 } sha512 && sha512.All(char.IsAsciiHexDigit) ? sha512
            : expected.Sha1 is { Length: 40 } sha1 && sha1.All(char.IsAsciiHexDigit) ? sha1 : null;
        if (digest is null) throw new InvalidDataException("目标版本缺少可核验的文件摘要。");
        byte[] actual = digest.Length == 128 ? await System.Security.Cryptography.SHA512.HashDataAsync(input, token).ConfigureAwait(false)
            : await System.Security.Cryptography.SHA1.HashDataAsync(input, token).ConfigureAwait(false);
        if (!Convert.ToHexString(actual).Equals(digest, StringComparison.OrdinalIgnoreCase)) throw new IOException("下载期间目标版本文件发生变化，请重新读取在线信息。");
    }

    private static async Task<string?> ValidateUpdatedModsAsync(IReadOnlyList<InstanceContentReplacement> replacements, InstanceManagementSnapshot snapshot, CancellationToken token)
    {
        var mods = replacements.Where(item => item.Original.PageId == "mods").ToArray();
        if (mods.Length == 0) return null;
        foreach (var item in mods) _ = await Nexa.Services.Minecraft.Install.MinecraftLocalJarService.InspectAsync(item.StagedPath, token).ConfigureAwait(false);
        if (mods.All(item => item.Original.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))) return null;
        string validation = Path.Combine(snapshot.GameDirectory, ".nexa-resource-" + Guid.NewGuid().ToString("N")), directory = Path.Combine(validation, "mods");
        Directory.CreateDirectory(directory); long total = 0; int count = 0;
        try
        {
            var sources = Directory.EnumerateFiles(Path.Combine(snapshot.GameDirectory, "mods"))
                .Where(path => !mods.Any(item => Nexa.Core.PathIdentity.Comparer.Equals(Path.GetFileName(path), item.Original.Name)))
                .Select(path => (Path: path, Name: Path.GetFileName(path)))
                .Concat(mods.Select(item => (Path: item.StagedPath, Name: item.NewName + (item.Original.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? ".disabled" : ""))));
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                for (string? path = source.Path; path is not null; path = Path.GetDirectoryName(path))
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("不能核验链接模组。");
                var file = new FileInfo(source.Path); total = checked(total + file.Length);
                if (++count > 4096 || total > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("模组更新依赖核验超过预算。");
                await using var input = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                await using var output = new FileStream(Path.Combine(directory, source.Name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, token).ConfigureAwait(false);
                if (input.Length != file.Length || output.Length != file.Length) throw new IOException("模组核验期间文件变化。");
            }
            var changedNames = mods.Select(item => item.NewName + (item.Original.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? ".disabled" : ""))
                .ToHashSet(Nexa.Core.PathIdentity.Comparer);
            var retainedNames = Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>()
                .Where(name => !changedNames.Contains(name)).ToHashSet(Nexa.Core.PathIdentity.Comparer);
            var changed = await Nexa.Services.Minecraft.Process.LaunchModInventoryReader.ReadSubsetAsync(directory, changedNames, token).ConfigureAwait(false);
            var retained = await Nexa.Services.Minecraft.Process.LaunchModInventoryReader.ReadSubsetAsync(directory, retainedNames, token).ConfigureAwait(false);
            var before = await Nexa.Services.Minecraft.Process.LaunchModInventoryReader.ReadAsync(snapshot.GameDirectory, token).ConfigureAwait(false);
            return ResourceDependencyVerifier.VerifyUpdate(changed with { Mods = changed.Mods.Where(item => item.Enabled).ToArray() },
                retained with { Mods = retained.Mods.Where(item => item.Enabled).ToArray() }, before);
        }
        finally
        {
            foreach (string path in Directory.EnumerateFiles(directory)) File.Delete(path);
            Directory.Delete(directory); Directory.Delete(validation);
        }
    }
}
