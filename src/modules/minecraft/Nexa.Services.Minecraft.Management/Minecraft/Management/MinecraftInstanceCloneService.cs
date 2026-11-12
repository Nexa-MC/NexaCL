using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Minecraft.Management;

/// <summary>Copies an admitted instance and its shared runtime inputs without replacing destination files.</summary>
public static class MinecraftInstanceCloneService
{
    public static Task<XsrResult> CloneAsync(MinecraftLibraryCloneCommand command, XsrStateStore store, Func<bool> validate, CancellationToken token = default)
        => CloneCoreAsync(command, store, validate, true, token);
    /// <summary>The caller must hold exclusive recovery leases on both roots before acquiring its library mutation lock.</summary>
    public static Task<XsrResult> CloneLeasedAsync(MinecraftLibraryCloneCommand command, XsrStateStore store, Func<bool> validate, CancellationToken token = default)
        => CloneCoreAsync(command, store, validate, false, token);
    private static async Task<XsrResult> CloneCoreAsync(MinecraftLibraryCloneCommand command, XsrStateStore store, Func<bool> validate, bool acquireLeases, CancellationToken token)
    {
        string? stage = null; var leases = new List<IDisposable>();
        try
        {
            if (!MinecraftVersionPaths.IsSafeReference(command.SourceId) || !MinecraftVersionPaths.IsSafeReference(command.Name)
                || command.Name.StartsWith(".nexa-", StringComparison.Ordinal) || !Path.IsPathFullyQualified(command.SourceRoot) || !Path.IsPathFullyQualified(command.DestinationRoot)) throw new InvalidDataException("Invalid clone request.");
            if (acquireLeases) foreach (string root in new[] { command.SourceRoot, command.DestinationRoot }.Distinct(Nexa.Core.PathIdentity.Comparer).Order(Nexa.Core.PathIdentity.Comparer))
                leases.Add(await InstanceRecoveryOperationGate.EnterRestoreAsync(root, token).ConfigureAwait(false));
            if (!validate()) throw new IOException("Library changed after clone preview.");
            MinecraftInstanceRenamer.EnsureIdle(command.SourceRoot, store, token); MinecraftInstanceRenamer.EnsureIdle(command.DestinationRoot, store, token);
            string source = Path.Combine(command.SourceRoot, "versions", command.SourceId), target = Path.Combine(command.DestinationRoot, "versions", command.Name);
            RecoveryBlobStore.CheckLinks(source); RecoveryBlobStore.CheckLinks(target);
            if (Path.Exists(target)) throw new IOException("Clone destination already exists.");
            var snapshot = await InstanceManagementService.ReadAsync(new(source), token).ConfigureAwait(false);
            var metadataStore = new MinecraftInstanceMetadataStore(); var metadata = await metadataStore.LoadAsync(source, token).ConfigureAwait(false);
            stage = Path.Combine(command.DestinationRoot, "versions", ".nexa-clone-" + Guid.NewGuid().ToString("N"));
            RecoveryBlobStore.CheckLinks(stage); Directory.CreateDirectory(stage);
            string instanceStage = Path.Combine(stage, "instance"), sharedStage = Path.Combine(stage, "shared");
            var observations = new List<(string Path, long Size, long Stamp)>(); int nodes = 0; long bytes = 0;
            async Task Copy(string from, string to, int depth = 0)
            {
                token.ThrowIfCancellationRequested(); RecoveryBlobStore.CheckLinks(from); RecoveryBlobStore.CheckLinks(to);
                if (++nodes > 100000 || depth > 64) throw new InvalidDataException("Clone exceeds its file or depth budget.");
                if (Directory.Exists(from))
                {
                    Directory.CreateDirectory(to);
                    foreach (string child in Directory.EnumerateFileSystemEntries(from))
                    {
                        string name = Path.GetFileName(child);
                        if (name.StartsWith(".nexa-", StringComparison.Ordinal) || name == "session.lock") continue;
                        await Copy(child, Path.Combine(to, name), depth + 1).ConfigureAwait(false);
                    }
                    return;
                }
                var info = new FileInfo(from);
                if (info.Length > 2147483648 || (bytes = checked(bytes + info.Length)) > 8589934592) throw new InvalidDataException("Clone exceeds its byte budget.");
                long size = info.Length, stamp = info.LastWriteTimeUtc.Ticks;
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                await using (var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
                await using (var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    byte[] buffer = new byte[65536]; long written = 0; int count;
                    while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                    { if ((written += count) > size) throw new IOException("Source changed while cloning."); await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); }
                    if (written != size) throw new IOException("Source changed while cloning.");
                }
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(to, File.GetUnixFileMode(from) & (UnixFileMode)0x1FF);
                observations.Add((from, size, stamp));
            }
            await Copy(source, instanceStage).ConfigureAwait(false);
            if (!metadata.InstanceIsolation)
            {
                foreach (string name in new[] { "mods", "resourcepacks", "shaderpacks", "saves", "screenshots", "schematics", "config", "defaultconfigs", "options.txt", "optionsof.txt", "servers.dat" })
                {
                    string from = Path.Combine(snapshot.GameDirectory, name), to = Path.Combine(instanceStage, name);
                    if (!Path.Exists(from)) continue;
                    DeleteOwnedTree(to); await Copy(from, to).ConfigureAwait(false);
                }
            }
            string manifest = Path.Combine(instanceStage, command.SourceId + ".json");
            if (new FileInfo(manifest).Length > 4 * 1024 * 1024) throw new InvalidDataException("Instance manifest is too large.");
            var json = JsonNode.Parse(await File.ReadAllBytesAsync(manifest, token).ConfigureAwait(false)) as JsonObject ?? throw new InvalidDataException("Invalid instance manifest.");
            var original = (JsonObject)json.DeepClone();
            json["id"] = command.Name; json["_minecraftVersion"] = snapshot.GameVersion;
            if (json["jar"]?.GetValue<string>() == command.SourceId) json["jar"] = command.Name;
            await File.WriteAllTextAsync(manifest, json.ToJsonString(), token).ConfigureAwait(false);
            if (!Nexa.Core.PathIdentity.Comparer.Equals(command.SourceId, command.Name))
            {
                File.Move(manifest, Path.Combine(instanceStage, command.Name + ".json"), false);
                string jar = Path.Combine(instanceStage, command.SourceId + ".jar"); if (File.Exists(jar)) File.Move(jar, Path.Combine(instanceStage, command.Name + ".jar"), false);
            }
            await metadataStore.SaveAsync(instanceStage, metadata with { DisplayName = command.Name, InstanceIsolation = true }, token).ConfigureAwait(false);
            if (!Nexa.Core.PathIdentity.Comparer.Equals(command.SourceRoot, command.DestinationRoot))
            {
                foreach (string name in new[] { "libraries", "assets" })
                    if (Directory.Exists(Path.Combine(command.SourceRoot, name))) await Copy(Path.Combine(command.SourceRoot, name), Path.Combine(sharedStage, name)).ConfigureAwait(false);
                var visited = new HashSet<string>(StringComparer.Ordinal) { command.SourceId };
                async Task Parents(JsonObject descriptor, int depth)
                {
                    if (depth > 32) throw new InvalidDataException("Version inheritance is too deep.");
                    foreach (string key in new[] { "inheritsFrom", "jar" })
                    {
                        if (descriptor[key]?.GetValue<string>() is not { } id || !visited.Add(id)) continue;
                        if (!MinecraftVersionPaths.IsSafeReference(id)) throw new InvalidDataException("Invalid inherited version.");
                        string directory = Path.Combine(command.SourceRoot, "versions", id), copy = Path.Combine(sharedStage, "versions", id);
                        string sourceJson = Path.Combine(directory, id + ".json");
                        await Copy(sourceJson, Path.Combine(copy, id + ".json")).ConfigureAwait(false);
                        if (File.Exists(Path.Combine(directory, id + ".jar"))) await Copy(Path.Combine(directory, id + ".jar"), Path.Combine(copy, id + ".jar")).ConfigureAwait(false);
                        if (new FileInfo(sourceJson).Length > 4 * 1024 * 1024) throw new InvalidDataException("Parent manifest is too large.");
                        await Parents(JsonNode.Parse(await File.ReadAllBytesAsync(sourceJson, token).ConfigureAwait(false)) as JsonObject ?? throw new InvalidDataException("Invalid parent manifest."), depth + 1).ConfigureAwait(false);
                    }
                }
                await Parents(original, 0).ConfigureAwait(false);
            }
            foreach (var observed in observations)
            {
                token.ThrowIfCancellationRequested(); RecoveryBlobStore.CheckLinks(observed.Path); var now = new FileInfo(observed.Path);
                if (!now.Exists || now.Length != observed.Size || now.LastWriteTimeUtc.Ticks != observed.Stamp) throw new IOException("Source changed before clone publication.");
            }
            if (!validate()) throw new IOException("Library changed before clone publication.");
            MinecraftInstanceRenamer.EnsureIdle(command.SourceRoot, store, token); MinecraftInstanceRenamer.EnsureIdle(command.DestinationRoot, store, token);
            if (Directory.Exists(sharedStage))
                foreach (string file in Directory.EnumerateFiles(sharedStage, "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested(); string destination = Path.Combine(command.DestinationRoot, Path.GetRelativePath(sharedStage, file)); RecoveryBlobStore.CheckLinks(destination);
                    if (File.Exists(destination))
                    {
                        if (new FileInfo(destination).Length != new FileInfo(file).Length) throw new IOException("A destination runtime dependency conflicts with the source.");
                        await using var a = File.OpenRead(file); await using var b = File.OpenRead(destination);
                        byte[] sourceHash = await SHA256.HashDataAsync(a, token).ConfigureAwait(false);
                        byte[] destinationHash = await SHA256.HashDataAsync(b, token).ConfigureAwait(false);
                        if (!sourceHash.AsSpan().SequenceEqual(destinationHash)) throw new IOException("A destination runtime dependency conflicts with the source.");
                    }
                    else { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Move(file, destination, false); }
                }
            token.ThrowIfCancellationRequested(); RecoveryBlobStore.CheckLinks(target); Directory.Move(instanceStage, target);
            return XsrResult.Success();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException or System.Text.Json.JsonException)
        { return XsrResult.Failure(MinecraftErrors.InvalidRequest(error.Message)); }
        finally { try { if (stage is not null) DeleteOwnedTree(stage); } finally { for (int i = leases.Count - 1; i >= 0; i--) leases[i].Dispose(); } }
    }
    public static void DeleteOwnedTree(string path)
    {
        if (!Path.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { if (Directory.Exists(path)) Directory.Delete(path); else File.Delete(path); return; }
        if (!Directory.Exists(path)) { File.Delete(path); return; }
        foreach (string child in Directory.EnumerateFileSystemEntries(path)) DeleteOwnedTree(child);
        Directory.Delete(path);
    }
}
