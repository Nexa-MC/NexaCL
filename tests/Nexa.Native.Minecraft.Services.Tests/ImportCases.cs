using System.IO.Compression;
using System.Security.Cryptography;
using Nexa.Native.Minecraft.Client;
using Nexa.Services.Minecraft.Install;

internal static class ImportCases
{
    internal static async Task RunAsync(NativeMinecraftClient client, string directory, string gameRoot, string instance, CancellationToken token)
    {
        var inspected = await client.InspectFolderAsync(instance, token);
        if (inspected.Kind != MinecraftFolderKind.Version) throw new InvalidDataException("Native folder inspection lost version identity.");
        string destination = Path.Combine(directory, "imported-game");
        Directory.CreateDirectory(destination);
        var imported = await client.ImportFolderAsync(new(instance, destination), token);
        if (!imported.IsSuccess || (await client.InstancesAsync(destination, token: token)).Count != 1
            || await File.ReadAllTextAsync(Path.Combine(destination, "versions", Path.GetFileName(instance), Path.GetFileName(instance) + ".jar"), token) != "hello")
            throw new InvalidDataException("Native folder import lost committed bytes: " + imported.Error?.Message);
        if ((await client.ImportFolderAsync(new(instance, destination), token)).IsSuccess)
            throw new InvalidDataException("Native folder import overwrote an existing version.");

        string jar = Path.Combine(directory, "native-mod.jar");
        WriteZip(jar, ("fixture.txt", "native-mod"));
        string digest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(jar, token)));
        var command = new MinecraftLocalJarCommand(new(jar, digest), gameRoot, Path.GetFileName(instance), LocalJarAction.Mod);
        var added = await client.ImportLocalJarAsync(command, token);
        if (!added.IsSuccess || !File.Exists(Path.Combine(gameRoot, "mods", Path.GetFileName(jar))))
            throw new InvalidDataException("Native local jar import failed: " + added.Error?.Message);
        if ((await client.ImportLocalJarAsync(command, token)).IsSuccess)
            throw new InvalidDataException("Native local jar import overwrote an existing mod.");

        string original = Path.Combine(directory, "native-original.mrpack"), updated = Path.Combine(directory, "native-updated.mrpack");
        WritePack(original, "1.0", "original"); WritePack(updated, "2.0", "updated");
        var pack = await client.InspectModpackAsync(original, token);
        string packRoot = Path.Combine(directory, "native-pack-root");
        Directory.CreateDirectory(packRoot);
        var installed = await client.InstallModpackAsync(new(pack, packRoot), token);
        if (!installed.IsSuccess) throw new InvalidDataException("Native modpack install failed: " + installed.Error?.Message);
        string packInstance = installed.Value!.InstanceDirectory;
        var identity = await client.ReadModpackIdentityAsync(new(packInstance, original), token);
        if (identity is null || identity.ArchiveSha256 != pack.Sha256 || identity.Fingerprint.Length != new FileInfo(original).Length)
            throw new InvalidDataException("Native archive identity lost its content fingerprint.");
        var preview = await client.PreviewModpackUpdateAsync(new(packInstance, updated, original), token);
        if (preview.Conflicts.Count != 0 || !preview.Updated.Contains("config/native.txt"))
            throw new InvalidDataException("Native update preview lost the archive baseline.");
        var applied = await client.UpdateModpackAsync(new(preview), token);
        if (!applied.IsSuccess || await File.ReadAllTextAsync(Path.Combine(packInstance, "config", "native.txt"), token) != "updated")
            throw new InvalidDataException("Native update did not commit the reviewed archive: " + applied.Error?.Message);
        if ((await client.UpdateModpackAsync(new(preview), token)).IsSuccess)
            throw new InvalidDataException("Native update accepted a stale preview.");
        bool refused = false;
        try { await client.PrepareMigrationAsync(packRoot, token); }
        catch (InvalidDataException) { refused = true; }
        if (!refused) throw new InvalidDataException("Migration cleanup accepted an ordinary installation.");
        string migrationRoot = Path.Combine(directory, ".nexa-migration-stage-" + Guid.NewGuid().ToString("N"), "game");
        var migration = await client.InstallMinecraftAsync(new(migrationRoot, "1.20.1"), token);
        if (!migration.IsSuccess) throw new InvalidDataException("Migration fixture install failed: " + migration.Error?.Message);
        await client.PrepareMigrationAsync(migrationRoot, token);
        if (Directory.Exists(Path.Combine(migrationRoot, ".nexa-install-jobs")) || !File.Exists(Path.Combine(migration.Value!.InstanceDirectory, "1.20.1.jar")))
            throw new InvalidDataException("Migration cleanup removed committed content or retained completed receipts.");
        Console.WriteLine("Native installation ports preserved folder/jar conflicts, archive identity, reviewed updates and migration cleanup.");
    }

    private static void WritePack(string path, string version, string value) => WriteZip(path,
        ("modrinth.index.json", "{\"formatVersion\":1,\"game\":\"minecraft\",\"name\":\"Native Pack\",\"versionId\":\"" + version + "\",\"dependencies\":{\"minecraft\":\"1.20.1\"},\"files\":[]}"),
        ("overrides/config/native.txt", value));

    private static void WriteZip(string path, params (string Path, string Text)[] files)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files) { using var writer = new StreamWriter(archive.CreateEntry(file.Path).Open()); writer.Write(file.Text); }
    }
}
