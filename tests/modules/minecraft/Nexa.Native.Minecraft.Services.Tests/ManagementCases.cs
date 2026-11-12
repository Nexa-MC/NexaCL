using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Nexa.Native.Minecraft.Client;
using Nexa.Services.Minecraft.Management;

internal static class ManagementCases
{
    internal static async Task RunAsync(NativeMinecraftClient client, string directory, string instance, CancellationToken token)
    {
        int crops = 0, backups = 0;
        var snapshots = new Snapshots(Path.Combine(directory, "world-snapshots"));
        client.AttachManagementHost((source, x, y, width, height) =>
        {
            if (x != 0 || y != 0 || width != 1 || height != 1) throw new InvalidDataException("Unexpected fixture crop.");
            Interlocked.Increment(ref crops); return source.ToArray();
        }, (path, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (!File.Exists(Path.Combine(path, "level.dat"))) throw new InvalidDataException("World backup capability lost its admitted source.");
            Interlocked.Increment(ref backups); return Task.CompletedTask;
        }, snapshots);
        var management = await client.QueryInstanceManagementQueryAsync(new(instance) { IncludeRecoveryStorage = true }, token);
        string game = management.Value!.GameDirectory;
        if (management.Value.RecoveryComparison is null || !Path.IsPathFullyQualified(game)) throw new InvalidDataException("Management lost canonical recovery state.");
        var identity = (await client.QueryInstanceIdentityQueryAsync(new(instance), token)).Value!;
        var changed = new InstanceIdentitySaveCommand(instance, identity.Revision, identity.Fields with { Description = "native identity" }, identity.Server);
        if (!(await client.DispatchInstanceIdentitySaveCommandAsync(changed, token)).IsSuccess
            || (await client.DispatchInstanceIdentitySaveCommandAsync(changed, token)).IsSuccess
            || (await client.QueryInstanceIdentityQueryAsync(new(instance), token)).Value!.Fields.Description != "native identity")
            throw new InvalidDataException("Native identity save lost stale-revision admission.");
        var servers = (await client.QueryInstanceServerListQueryAsync(new(instance), token)).Value!;
        var saveServers = new InstanceServerListSaveCommand(instance, servers.Revision, [new(-1, "Native fixture", "localhost:25565")]);
        if (!(await client.DispatchInstanceServerListSaveCommandAsync(saveServers, token)).IsSuccess
            || (await client.QueryInstanceServerListQueryAsync(new(instance), token)).Value!.Entries.Single().Address != "localhost:25565"
            || (await client.DispatchInstanceServerListSaveCommandAsync(saveServers, token)).IsSuccess)
            throw new InvalidDataException("Native server-list mutation lost revision admission.");

        string config = Path.Combine(game, "config"); Directory.CreateDirectory(config);
        string path = Path.Combine(config, "native.txt"); await File.WriteAllTextAsync(path, "before", token);
        var file = new FileInfo(path);
        var document = (await client.QueryInstanceFileReadQueryAsync(new(instance, "config", file.Name, file.Length, file.LastWriteTimeUtc.Ticks), token)).Value!;
        var preview = (await client.QueryInstanceFileSavePreviewQueryAsync(new(document, "after"), token)).Value!;
        if (!(await client.DispatchInstanceFileSaveCommandAsync(new(preview), token)).IsSuccess || await File.ReadAllTextAsync(path, token) != "after"
            || (await client.DispatchInstanceFileSaveCommandAsync(new(preview), token)).IsSuccess)
            throw new InvalidDataException("Native file editing accepted stale content or lost committed bytes.");

        string screenshots = Path.Combine(game, "screenshots"); Directory.CreateDirectory(screenshots);
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=");
        string screenshot = Path.Combine(screenshots, "native.png"); await File.WriteAllBytesAsync(screenshot, png, token); file = new(screenshot);
        var screenshotQuery = new InstanceScreenshotQuery(instance, file.Name, file.Length, file.LastWriteTimeUtc.Ticks);
        if ((await client.QueryInstanceScreenshotQueryAsync(screenshotQuery, token)).Value!.Image.Width != 1
            || !(await client.DispatchInstanceScreenshotCropCommandAsync(new(screenshotQuery, 0, 0, 1, 1), token)).IsSuccess || crops != 1
            || (await client.DispatchInstanceScreenshotCropCommandAsync(new(screenshotQuery, 0, 0, 2, 1), token)).IsSuccess || crops != 1)
            throw new InvalidDataException("Native screenshot admission bypassed its asynchronous encoder capability.");
        var integrity = (await client.QueryInstanceContentIntegrityQueryAsync(new(instance, "screenshots", file.Name, file.Length, file.LastWriteTimeUtc.Ticks), token)).Value!;
        if (integrity.Sha256 != Convert.ToHexString(SHA256.HashData(png))) throw new InvalidDataException("Native content integrity changed its digest.");
        if (!(await client.DispatchInstanceContentRemoveCommandAsync(new(instance, "screenshots", file.Name, false, file.Length, file.LastWriteTimeUtc.Ticks), token)).IsSuccess)
            throw new InvalidDataException("Native trash rejected an unchanged screenshot.");
        var trash = (await client.QueryInstanceManagementQueryAsync(new(instance) { IncludeTrash = true }, token)).Value!.Trash.Single();
        if (File.Exists(screenshot) || !(await client.DispatchInstanceContentRestoreCommandAsync(new(instance, trash.Id), token)).IsSuccess || !File.Exists(screenshot))
            throw new InvalidDataException("Native trash restore lost its actual file.");

        file = new(screenshot);
        if (!(await client.DispatchInstanceContentRemoveCommandAsync(new(instance, "screenshots", file.Name, false, file.Length, file.LastWriteTimeUtc.Ticks), token)).IsSuccess) throw new InvalidDataException("Native clear-trash setup failed.");
        trash = (await client.QueryInstanceManagementQueryAsync(new(instance) { IncludeTrash = true }, token)).Value!.Trash.Single();
        if ((await client.DispatchInstanceContentClearTrashCommandAsync(new(instance, [Guid.NewGuid().ToString("N")]), token)).IsSuccess
            || !(await client.DispatchInstanceContentClearTrashCommandAsync(new(instance, [trash.Id]), token)).IsSuccess
            || (await client.QueryInstanceManagementQueryAsync(new(instance) { IncludeTrash = true }, token)).Value!.Trash.Count != 0)
            throw new InvalidDataException("Native clear-trash ignored its expected selection.");
        string world = Path.Combine(game, "saves", "NativeWorld"); Directory.CreateDirectory(world);
        await File.WriteAllBytesAsync(Path.Combine(world, "level.dat"), WorldNbt(), token);
        await File.WriteAllTextAsync(Path.Combine(world, "session.lock"), "native session sentinel", token);
        var metadata = (await client.QueryInstanceWorldMetadataQueryAsync(new(instance, "NativeWorld"), token)).Value!;
        if (!(await client.RenameWorldAsync(new(instance, "NativeWorld", "Renamed native world", metadata.Revision), token)).IsSuccess
            || (await client.RenameWorldAsync(new(instance, "NativeWorld", "Stale", metadata.Revision), token)).IsSuccess)
            throw new InvalidDataException("Native world rename did not preserve revision checks.");
        metadata = (await client.QueryInstanceWorldMetadataQueryAsync(new(instance, "NativeWorld"), token)).Value!;
        if (metadata.LevelName != "Renamed native world" || !(await client.SetWorldDataPackOrderAsync(new(instance, "NativeWorld", ["vanilla"], metadata.Revision), token)).IsSuccess)
            throw new InvalidDataException("Native world rename or ordered data-pack edit was lost.");
        metadata = (await client.QueryInstanceWorldMetadataQueryAsync(new(instance, "NativeWorld"), token)).Value!;
        if ((await client.SetWorldDataPackOrderAsync(new(instance, "NativeWorld", [], metadata.Revision), token)).IsSuccess)
            throw new InvalidDataException("Native data-pack edit removed vanilla.");
        if (!(await client.DispatchInstanceWorldCopyCommandAsync(new(instance, "NativeWorld", "NativeCopy", metadata.Revision), token)).IsSuccess
            || File.Exists(Path.Combine(game, "saves", "NativeCopy", "session.lock"))
            || (await client.DispatchInstanceWorldCopyCommandAsync(new(instance, "NativeWorld", "StaleCopy", "stale"), token)).IsSuccess)
            throw new InvalidDataException("Native world copy lost source admission or copied a session lock.");
        if (!(await client.DispatchInstanceWorldBackupCommandAsync(new(instance, "NativeWorld", metadata.Revision), token)).IsSuccess || backups != 1)
            throw new InvalidDataException("Native world backup bypassed its capability.");
        if (!(await client.DispatchInstanceWorldSnapshotCaptureCommandAsync(new(instance, "NativeWorld", metadata.Revision), token)).IsSuccess)
            throw new InvalidDataException("Native world snapshot capture failed.");
        var row = (await client.QueryInstanceWorldSnapshotListQueryAsync(new(instance, "NativeWorld"), token)).Value!.Items.Single();
        if (!(await client.QueryInstanceWorldSnapshotVerifyQueryAsync(new(instance, "NativeWorld", row.Identity), token)).Value!.Ready
            || !(await client.DispatchInstanceWorldSnapshotRestoreCommandAsync(new(instance, "NativeWorld", row.Identity, "NativeRestored", metadata.Revision), token)).IsSuccess
            || !File.Exists(Path.Combine(game, "saves", "NativeRestored", "level.dat")))
            throw new InvalidDataException("Native world snapshot restore lost its scoped immutable storage capability.");
        Console.WriteLine("Native management preserved identity/server/file revisions, screenshot capabilities, trash and locked world snapshots.");
    }

    private static byte[] WorldNbt()
    {
        using var body = new MemoryStream();
        void Text(string text) { byte[] bytes = Encoding.UTF8.GetBytes(text); Span<byte> length = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)bytes.Length)); body.Write(length); body.Write(bytes); }
        void Header(byte type, string name) { body.WriteByte(type); Text(name); }
        Header(10, ""); Header(10, "Data"); Header(8, "LevelName"); Text("Native fixture");
        Header(3, "DataVersion"); Span<byte> version = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(version, 3955); body.Write(version);
        Header(10, "DataPacks"); Header(9, "Enabled"); body.WriteByte(8); body.Write(new byte[] { 0, 0, 0, 1 }); Text("vanilla"); body.WriteByte(0);
        body.WriteByte(0); body.WriteByte(0);
        using var result = new MemoryStream(); using (var gzip = new GZipStream(result, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(body.ToArray()); return result.ToArray();
    }

    private sealed class Snapshots(string root) : IInstanceWorldSnapshotStore
    {
        private string? _scope;
        private InstanceWorldSnapshot? _snapshot;
        public Task<IReadOnlyList<InstanceWorldSnapshot>> ListAsync(string scope, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<InstanceWorldSnapshot>>(_scope == scope && _snapshot is { } item ? [item] : []); }
        public async Task<InstanceWorldSnapshot> CaptureAsync(string scope, string world, string displayName, CancellationToken token)
        {
            if (scope.Length != 64 || !scope.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Snapshot scope leaked a path.");
            Directory.CreateDirectory(root); byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(world, "level.dat"), token);
            await File.WriteAllBytesAsync(Path.Combine(root, "level.dat"), bytes, token); _scope = scope;
            return _snapshot = new(Convert.ToHexString(SHA256.HashData(bytes)), DateTimeOffset.UtcNow, 1, bytes.Length);
        }
        public Task<InstanceWorldSnapshotVerification> VerifyAsync(string scope, string identity, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Require(scope, identity); return Task.FromResult(new InstanceWorldSnapshotVerification(identity, 1, 1, [])); }
        public Task RestoreAsync(string scope, string identity, string destination, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Require(scope, identity); Directory.CreateDirectory(destination); File.Copy(Path.Combine(root, "level.dat"), Path.Combine(destination, "level.dat")); return Task.CompletedTask; }
        private void Require(string scope, string identity) { if (scope != _scope || identity != _snapshot?.Identity) throw new InvalidDataException("Wrong world scope."); }
    }
}
