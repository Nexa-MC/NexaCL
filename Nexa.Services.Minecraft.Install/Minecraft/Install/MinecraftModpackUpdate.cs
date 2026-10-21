using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Install;

public sealed partial class MinecraftInstallService
{
    private sealed record ModpackUpdateState(MinecraftModpackUpdatePreview Preview, ModpackManagedManifest Baseline,
        IReadOnlyDictionary<string, string?> Current, MinecraftInstanceMetadata Metadata);

    public async Task<MinecraftModpackUpdatePreview> PreviewModpackUpdateAsync(MinecraftModpackUpdateQuery query, CancellationToken token = default)
        => (await ReadModpackUpdateAsync(query, token).ConfigureAwait(false)).Preview;

    private async Task<ModpackUpdateState> ReadModpackUpdateAsync(MinecraftModpackUpdateQuery query, CancellationToken token)
    {
        string instance = Nexa.Core.PathIdentity.Normalize(query.InstanceDirectory);
        if (Directory.GetParent(instance) is not { Name: "versions", Parent: { } root } || !MinecraftVersionPaths.IsSafeReference(Path.GetFileName(instance)))
            throw new InvalidDataException("请选择实际版本目录。");
        RecoveryBlobStore.CheckLinks(instance);
        var edit = await MinecraftInstallEditService.ReadAsync(new(root.FullName, Path.GetFileName(instance)), token).ConfigureAwait(false);
        var metadata = await _instanceMetadataForPack.LoadAsync(instance, token).ConfigureAwait(false);
        if (!metadata.InstanceIsolation) throw new InvalidDataException("请先为该整合包启用版本隔离，再更新整合包。");
        var pack = await MinecraftModpackArchive.ReadAsync(query.ArchivePath, token).ConfigureAwait(false);
        if (Nexa.Core.PathIdentity.Comparer.Equals(Path.GetFileName(instance), pack.Preview.Game))
            throw new InvalidDataException("当前版本名称与新版 Minecraft 基础版本冲突，请先修改版本名称。");
        var baseline = await ModpackManagedManifestStore.ReadAsync(instance, token).ConfigureAwait(false)
            ?? await ReadOriginalModpackBaselineAsync(instance, query.OriginalArchivePath, metadata.ModpackVersion, token).ConfigureAwait(false);
        if (baseline.ArchiveSha256.Equals(pack.Preview.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("所选整合包与已安装版本相同。");
        var newPaths = (await ResolvePackFilesAsync(pack, query.IncludeOptional, token).ConfigureAwait(false)).Select(file => file.Path)
            .ToHashSet(Nexa.Core.PathIdentity.Comparer);
        using (var archive = ZipFile.OpenRead(pack.Preview.Path))
            foreach (string prefix in pack.OverrideRoots)
                foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0 && entry.FullName.StartsWith(prefix, StringComparison.Ordinal)))
                    newPaths.Add(entry.FullName[prefix.Length..]);
        foreach (string path in newPaths)
        {
            ValidatePackDestination(path, pack.Preview.InstanceId);
            ValidatePackDestination(path, Path.GetFileName(instance));
        }
        newPaths.RemoveWhere(ModpackManagedManifestStore.IsUserOwned);
        var owned = baseline.Files.ToDictionary(file => file.Path, Nexa.Core.PathIdentity.Comparer);
        var current = new Dictionary<string, string?>(Nexa.Core.PathIdentity.Comparer);
        List<string> added = [], updated = [], removed = [], preserved = [], conflicts = [];
        StringBuilder revision = new(edit.Fingerprint + "\n" + pack.Preview.Sha256 + "\n" + baseline.ArchiveSha256 + "\n"
            + query.IncludeOptional + "\n" + query.ReplaceModifiedMods);
        foreach (string path in new[] { Path.GetFileName(instance) + ".json", Path.GetFileName(instance) + ".jar", "Nexa/InstanceMetadata.json", "PCL/InstanceMetadata.json", ModpackManagedManifestStore.RelativePath })
        {
            string? hash = await PackHashAsync(Nexa.Core.PathIdentity.Contained(instance, path), token).ConfigureAwait(false);
            current.Add(path, hash); revision.Append('\n').Append(path).Append('\0').Append(hash);
        }
        foreach (string path in owned.Keys.Concat(newPaths).Distinct(Nexa.Core.PathIdentity.Comparer).Order(StringComparer.Ordinal))
        {
            string target = Nexa.Core.PathIdentity.Contained(instance, path);
            string? hash = await PackHashAsync(target, token).ConfigureAwait(false);
            current.Add(path, hash);
            revision.Append('\n').Append(path).Append('\0').Append(hash).Append('\0').Append(owned.GetValueOrDefault(path)?.Sha256);
            bool wasOwned = owned.TryGetValue(path, out var previous);
            bool modified = hash is not null && (!wasOwned || !hash.Equals(previous!.Sha256, StringComparison.OrdinalIgnoreCase));
            bool binary = path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) && (path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase));
            if (modified && (!binary || !query.ReplaceModifiedMods))
            {
                preserved.Add(path);
                if (binary) conflicts.Add(path);
                continue;
            }
            if (newPaths.Contains(path)) { if (hash is null) added.Add(path); else updated.Add(path); }
            else if (wasOwned && hash is not null) removed.Add(path);
        }
        var preview = new MinecraftModpackUpdatePreview(query with { InstanceDirectory = instance }, pack.Preview,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revision.ToString()))), added.AsReadOnly(), updated.AsReadOnly(),
            removed.AsReadOnly(), preserved.AsReadOnly(), conflicts.AsReadOnly());
        return new(preview, baseline, current, metadata);
    }

    private readonly MinecraftInstanceMetadataStore _instanceMetadataForPack = new();

    public Task<XsrResult<MinecraftInstallResult>> UpdateModpackAsync(MinecraftModpackUpdateCommand command, CancellationToken token = default)
        => UpdateModpackOwnedAsync(command, token);

    private async Task<XsrResult<MinecraftInstallResult>> UpdateModpackOwnedAsync(MinecraftModpackUpdateCommand command, CancellationToken token,
        Nexa.Services.Tasks.ITaskCenterTask? retryTask = null)
    {
        var request = command.Preview.Request;
        string instance = Nexa.Core.PathIdentity.Normalize(request.InstanceDirectory);
        string root = Directory.GetParent(instance)?.Parent?.FullName ?? throw new InvalidDataException("版本目录无效。");
        InstallExecution execution;
        try { execution = RegisterExecution(new(root, command.Preview.Pack.Game, InstanceName: Path.GetFileName(instance))); }
        catch (OperationCanceledException) { return XsrResult.Failure<MinecraftInstallResult>(XsrRuntimeErrors.Cancelled()); }
        using var task = retryTask ?? _tasks.Begin(new("modpack.update:" + Guid.NewGuid().ToString("N"), "更新整合包 " + command.Preview.Pack.Name,
            ["检查变更", "准备新版", "校验", "应用更新"]), (retry, cancellation) => UpdateModpackOwnedAsync(command, cancellation, retry));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, task.CancellationToken, execution.Token);
        token = linked.Token;
        string stage = Path.Combine(root, ".nexa-modify", Guid.NewGuid().ToString("N"));
        bool prepared = false, committed = false;
        try
        {
            using var operation = await InstanceRecoveryOperationGate.EnterRestoreAsync(root, token).ConfigureAwait(false);
            if (_hostStore is null) throw new InvalidOperationException("整合包更新需要活动进程状态。");
            MinecraftInstanceRenamer.EnsureIdle(root, _hostStore, token);
            var state = await ReadModpackUpdateAsync(request, token).ConfigureAwait(false);
            if (state.Preview.Revision != command.Preview.Revision || state.Preview.Pack != command.Preview.Pack)
                throw new IOException("整合包或版本文件已变化，请重新预览更新。");
            if (state.Preview.Conflicts.Count > 0) throw new IOException("部分模组已被修改或与新版同名；请先在预览中确认替换这些模组。");
            RecoveryBlobStore.CheckLinks(stage); Directory.CreateDirectory(stage);
            string scratch = Path.Combine(stage, "prepared"); Directory.CreateDirectory(scratch);
            task.Report("准备新版", state.Preview.Pack.Version, .1, 0, 1, 0);
            var built = await InstallModpackCoreAsync(new(state.Preview.Pack, scratch, request.IncludeOptional), token,
                preparingUpdate: true, parentTask: task, baseReuseRoot: root,
                baseReuseClientPath: Path.Combine(instance, Path.GetFileName(instance) + ".jar")).ConfigureAwait(false);
            if (!built.IsSuccess)
            {
                if (built.Error?.Kind == XsrErrorKind.Cancelled) throw new OperationCanceledException(token);
                throw new IOException(built.Error?.Message ?? "新版整合包准备失败。");
            }
            string source = built.Value!.InstanceDirectory;
            string id = Path.GetFileName(instance), prefix = "versions/" + id + "/";
            var generated = new List<string>();
            var removals = new Dictionary<string, string>(Nexa.Core.PathIdentity.Comparer);
            foreach (string path in state.Preview.Removed.Concat(state.Preview.Updated))
                if (state.Current[path] is { } hash) removals[prefix + path] = hash;
            var selected = state.Preview.Added.Concat(state.Preview.Updated).ToHashSet(Nexa.Core.PathIdentity.Comparer);
            var newManifest = await ModpackManagedManifestStore.ReadAsync(source, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("新版整合包缺少受管文件清单。");
            foreach (string path in selected)
            {
                string from = Nexa.Core.PathIdentity.Contained(source, path);
                if (!File.Exists(from)) throw new IOException("新版整合包缺少预览中的文件：" + path);
                await StagePackFileAsync(from, prefix + path, stage, generated, token).ConfigureAwait(false);
            }
            // Shared dependencies were prepared by the normal, hash-checked install pipeline.
            foreach (string path in ModpackManagedManifestStore.Enumerate(scratch))
            {
                string relative = Path.GetRelativePath(scratch, path).Replace('\\', '/');
                bool inheritedVersion = relative.Split('/') is ["versions", var versionId, var versionFile]
                    && versionId != state.Preview.Pack.InstanceId && (versionFile == versionId + ".json" || versionFile == versionId + ".jar");
                if (!inheritedVersion && !relative.StartsWith("libraries/", StringComparison.Ordinal) && !relative.StartsWith("assets/", StringComparison.Ordinal)
                    && !relative.StartsWith("runtime/", StringComparison.Ordinal)) continue;
                await StagePackFileAsync(path, relative, stage, generated, token).ConfigureAwait(false);
            }
            string originalId = state.Preview.Pack.InstanceId;
            foreach (string path in Directory.EnumerateFiles(source))
            {
                string name = Path.GetFileName(path);
                if (name is not null && (name == originalId + ".json" || name == originalId + ".jar"))
                    await StagePackFileAsync(path, prefix + id + Path.GetExtension(name), stage, generated, token).ConfigureAwait(false);
            }
            string stagedInstance = Path.Combine(stage, "versions", id);
            string manifestPath = Path.Combine(stagedInstance, id + ".json");
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, token).ConfigureAwait(false))!.AsObject();
            manifest["id"] = id;
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), token).ConfigureAwait(false);
            await _instanceMetadataForPack.SaveAsync(stagedInstance, state.Metadata with { ModpackVersion = state.Preview.Pack.Version }, token).ConfigureAwait(false);
            generated.Add(prefix + "Nexa/InstanceMetadata.json");
            await ModpackManagedManifestStore.WriteAsync(stagedInstance, newManifest, token).ConfigureAwait(false);
            generated.Add(prefix + ModpackManagedManifestStore.RelativePath);
            task.Report("校验", "重新确认预览中的原文件", .8, 0, 1, 0);
            var latest = await ReadModpackUpdateAsync(request, token).ConfigureAwait(false);
            if (latest.Preview.Revision != state.Preview.Revision) throw new IOException("准备期间版本文件已变化，请重新预览。");
            MinecraftInstanceRenamer.EnsureIdle(root, _hostStore, token);
            var expectedOriginals = state.Current.ToDictionary(pair => prefix + pair.Key, pair => pair.Value, Nexa.Core.PathIdentity.Comparer);
            var publication = await InstallPublicationJournal.PrepareAsync(root, stage, id, generated, removals, token,
                modpackUpdate: true, expectedOriginals: expectedOriginals).ConfigureAwait(false);
            // A discoverable recovery task always has a complete publication plan. It never
            // falls back to reinstalling the old game when preparation was interrupted.
            var saved = await InstallTaskJournal.CreateAsync(stage, new(root, state.Preview.Pack.Game, state.Preview.Pack.Loader,
                state.Preview.Pack.Build, InstanceName: id)
            { EditFingerprint = state.Preview.Revision, InheritVanilla = false, DefaultIsolationMode = "all" }, token).ConfigureAwait(false);
            prepared = true; ReadyExecution(execution, stage);
            try
            {
                task.Report("应用更新", "", .9, 0, 1, 0);
                await publication.ApplyAsync(token).ConfigureAwait(false);
                committed = true;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException and not AccessViolationException)
            {
                if (_pauseForExit && failure is OperationCanceledException && token.IsCancellationRequested) throw;
                await publication.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                await InstallTaskJournal.WriteStatusAsync(stage, saved, InstallTaskStatus.RollbackRequested, CancellationToken.None).ConfigureAwait(false);
                await InstallTaskJournal.WriteStatusAsync(stage, saved, InstallTaskStatus.RolledBack, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            await InstallTaskJournal.WriteStatusAsync(stage, saved, InstallTaskStatus.Completed, CancellationToken.None).ConfigureAwait(false);
            execution.Completed = true;
            task.Complete("整合包已更新至 " + state.Preview.Pack.Version + (state.Preview.Preserved.Count > 0 ? "，已保留本地修改。" : ""));
            Installed?.Invoke(root);
            return XsrResult.Success(new MinecraftInstallResult(id, instance));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || _stopping)
        { if (_pauseForExit && prepared) task.Paused(); else task.Canceled(); return XsrResult.Failure<MinecraftInstallResult>(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            string message = committed ? "整合包文件已更新，但完成记录未能保存；请通过安装恢复任务确认结果。" + error.Message : error.Message;
            task.Fail(message);
            if (committed) Installed?.Invoke(root);
            return XsrResult.Failure<MinecraftInstallResult>(MinecraftErrors.InvalidRequest(message));
        }
        finally
        {
            FinishExecution(execution);
            try
            {
                RecoveryBlobStore.CheckLinks(stage);
                if (Directory.Exists(stage))
                {
                    if (!prepared) Directory.Delete(stage, recursive: true);
                    else foreach (string directory in Directory.EnumerateDirectories(stage).Where(path => Path.GetFileName(path) is not (".publication" or ".task")))
                    { RecoveryBlobStore.CheckLinks(directory); Directory.Delete(directory, recursive: true); }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<IReadOnlyList<ModpackFile>> ResolvePackFilesAsync(ModpackPlan plan, bool optional, CancellationToken token)
    {
        var result = plan.Files.Where(file => optional || !file.Optional).ToList();
        foreach (var file in plan.CurseFiles.Where(file => optional || !file.Optional))
            result.Add(await ResolveCurseForgePackFileAsync(file, token).ConfigureAwait(false));
        return result;
    }

    private async Task<ModpackManagedManifest> ReadOriginalModpackBaselineAsync(string instance, string? original, string version, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(original)) throw new InvalidDataException("此整合包尚无文件清单，请选择已安装版本的原整合包建立关联。");
        var plan = await MinecraftModpackArchive.ReadAsync(original, token).ConfigureAwait(false);
        if (version.Length > 0 && version != plan.Preview.Version) throw new InvalidDataException("原整合包版本与已安装记录不同。");
        Dictionary<string, ModpackPublishedFile> files = new(Nexa.Core.PathIdentity.Comparer);
        foreach (var file in await ResolvePackFilesAsync(plan, true, token).ConfigureAwait(false))
        {
            if (ModpackManagedManifestStore.IsUserOwned(file.Path)) continue;
            string path = Nexa.Core.PathIdentity.Contained(instance, file.Path); RecoveryBlobStore.CheckLinks(path);
            string hash = File.Exists(path) && await VerifyPackFileAsync(path, file, token).ConfigureAwait(false)
                ? (await PackHashAsync(path, token).ConfigureAwait(false))! : new string('0', 64);
            files[file.Path] = new(file.Path, file.Size, hash);
        }
        using var archive = ZipFile.OpenRead(plan.Preview.Path);
        foreach (string prefix in plan.OverrideRoots)
            foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0 && entry.FullName.StartsWith(prefix, StringComparison.Ordinal)))
            {
                string path = entry.FullName[prefix.Length..]; ValidatePackDestination(path, plan.Preview.InstanceId);
                if (ModpackManagedManifestStore.IsUserOwned(path)) continue;
                await using var input = entry.Open();
                string hash = await RecoveryBlobStore.CopyAndHashAsync(input, Stream.Null, entry.Length,
                    new(RecoveryBlobStore.MaxFileBytes), token).ConfigureAwait(false);
                files[path] = new(path, entry.Length, hash);
            }
        return new(1, plan.Preview.Name, plan.Preview.Version, plan.Preview.Sha256, files.Values.ToArray());
    }

    private static async Task<string?> PackHashAsync(string path, CancellationToken token)
    {
        RecoveryBlobStore.CheckLinks(path);
        if (Directory.Exists(path)) throw new IOException("整合包文件目标已被目录占用：" + Path.GetFileName(path));
        if (!File.Exists(path)) return null;
        await using var input = File.OpenRead(path);
        if (input.Length > MinecraftModpackArchive.MaxFile) throw new InvalidDataException("整合包文件超过单文件限制。");
        return await RecoveryBlobStore.CopyAndHashAsync(input, Stream.Null, input.Length,
            new(RecoveryBlobStore.MaxFileBytes), token).ConfigureAwait(false);
    }

    private static async Task StagePackFileAsync(string source, string relative, string stage, List<string> generated, CancellationToken token)
    {
        RecoveryBlobStore.CheckLinks(source);
        string target = Nexa.Core.PathIdentity.Contained(stage, relative); RecoveryBlobStore.CheckLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, token).ConfigureAwait(false);
        generated.Add(relative);
    }
}
