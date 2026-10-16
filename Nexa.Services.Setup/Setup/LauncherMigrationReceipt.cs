using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexa.Services.Setup;

public sealed partial class LauncherMigrationService
{
    private sealed record ReceiptState(LauncherMigrationReceipt Receipt, IReadOnlyList<LauncherMigrationFile> Files,
        IReadOnlyList<string> Directories, string ReceiptSha256, ReadOnlyMemory<byte> ReceiptBytes);

    public async Task<LauncherMigrationReceipt> ReadReceiptAsync(string destination, CancellationToken token = default) =>
        (await ReadReceiptStateAsync(destination, token).ConfigureAwait(false)).Receipt;

    public async Task UndoAsync(LauncherMigrationReceipt receipt, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        string destination = LauncherMigrationFiles.Absolute(receipt.DestinationDirectory);
        await _operation.WaitAsync(token).ConfigureAwait(false);
        string? removal = null;
        try
        {
            EnsureIdle();
            var state = await ReadReceiptStateAsync(destination, token).ConfigureAwait(false);
            if (state.Receipt.Identity != receipt.Identity || !state.Receipt.Instances.SequenceEqual(receipt.Instances))
                throw new IOException("迁移记录已更改，请重新读取后检查。");
            await VerifyReceiptFilesAsync(destination, state, token).ConfigureAwait(false);
            EnsureIdle(); token.ThrowIfCancellationRequested();
            removal = Path.Combine(Path.GetDirectoryName(destination)!, ".nexa-migration-remove-" + Guid.NewGuid().ToString("N"));
            LauncherMigrationFiles.CheckLinks(destination); LauncherMigrationFiles.CheckLinks(removal);
            Directory.Move(destination, removal);
            try
            {
                // Recheck after taking the directory out of its public location; never delete a changed import.
                await VerifyReceiptFilesAsync(removal, state, token).ConfigureAwait(false);
                EnsureIdle(); token.ThrowIfCancellationRequested();
                LauncherMigrationFiles.DeleteOwned(removal); removal = null;
            }
            catch
            {
                if (Directory.Exists(removal) && !Path.Exists(destination))
                { LauncherMigrationFiles.CheckLinks(destination); Directory.Move(removal, destination); removal = null; }
                throw;
            }
        }
        finally { _operation.Release(); }
    }

    private async Task VerifyReceiptFilesAsync(string directory, ReceiptState expected, CancellationToken token)
    {
        var actual = await LauncherMigrationFiles.CaptureAsync(directory, token).ConfigureAwait(false);
        var actualReceipt = actual.Files.SingleOrDefault(file => file.RelativePath == ReceiptName);
        if (actualReceipt is null || actualReceipt.Sha256 != expected.ReceiptSha256)
            throw new IOException("迁移记录已更改，未删除任何文件。");
        var files = actual.Files.Where(file => file.RelativePath != ReceiptName).ToArray();
        if (files.Length != expected.Files.Count || !actual.Directories.SequenceEqual(expected.Directories, StringComparer.Ordinal))
            throw new IOException("迁入目录中有新增或删除的内容，无法安全撤销。");
        for (int index = 0; index < files.Length; index++)
            if (files[index].RelativePath != expected.Files[index].RelativePath || files[index].Bytes != expected.Files[index].Bytes
                || files[index].Sha256 != expected.Files[index].Sha256)
                throw new IOException("迁入文件在导入后发生变化，无法安全撤销。");
        ReceiptAuthority().Verify(Path.Combine(expected.Receipt.DestinationDirectory, ReceiptName), expected.ReceiptBytes);
    }

    private async Task WriteReceiptAsync(string stage, LauncherMigrationReceipt receipt,
        LauncherMigrationSnapshot snapshot, CancellationToken token)
    {
        JsonArray instances = [], files = [], directories = [];
        foreach (var instance in receipt.Instances)
            instances.Add((JsonNode)new JsonObject
            {
                ["id"] = instance.Id,
                ["name"] = instance.Name,
                ["root"] = Path.GetRelativePath(receipt.DestinationDirectory, instance.MinecraftRootDirectory).Replace('\\', '/'),
                ["instanceId"] = instance.InstanceId
            });
        foreach (var file in snapshot.Files)
            files.Add((JsonNode)new JsonObject { ["path"] = file.RelativePath, ["bytes"] = file.Bytes, ["sha256"] = file.Sha256 });
        foreach (string directory in snapshot.Directories) directories.Add((JsonNode?)JsonValue.Create(directory));
        var document = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["identity"] = receipt.Identity,
            ["destination"] = receipt.DestinationDirectory,
            ["instances"] = instances,
            ["files"] = files,
            ["directories"] = directories
        };
        string text = document.ToJsonString();
        if (System.Text.Encoding.UTF8.GetByteCount(text) > 64 * 1024 * 1024) throw new IOException("迁移记录超过大小预算。");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        await using (var output = new FileStream(Path.Combine(stage, ReceiptName), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            await output.WriteAsync(bytes, token).ConfigureAwait(false);
            await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
        }
        await ReceiptAuthority().AuthorizeAsync(Path.Combine(receipt.DestinationDirectory, ReceiptName), bytes, token).ConfigureAwait(false);
    }

    private async Task<ReceiptState> ReadReceiptStateAsync(string destination, CancellationToken token)
    {
        var authority = ReceiptAuthority();
        destination = LauncherMigrationFiles.Absolute(destination);
        string path = Path.Combine(destination, ReceiptName); LauncherMigrationFiles.CheckLinks(path);
        byte[] bytes;
        await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
        {
            if (input.Length > 64 * 1024 * 1024) throw new IOException("迁移记录超过大小预算。");
            bytes = new byte[(int)input.Length]; await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            if (input.ReadByte() != -1) throw new IOException("迁移记录在读取期间发生变化。");
        }
        authority.Verify(path, bytes);
        string sha = Convert.ToHexString(SHA256.HashData(bytes));
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schema)
            || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int schemaVersion) || schemaVersion != 1
            || Text(root, "identity") is not { } identity || !Guid.TryParseExact(identity, "N", out _)
            || Text(root, "destination") is not { } savedDestination
            || !Nexa.Core.PathIdentity.Comparer.Equals(LauncherMigrationFiles.Absolute(savedDestination), destination))
            throw new InvalidDataException("迁移记录不属于该目录。");
        List<LauncherMigrationImportedInstance> instances = [];
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in Elements(root, "instances", 256))
        {
            string id = RequiredText(value, "id"), name = RequiredText(value, "name"), relative = RequiredText(value, "root");
            string instanceId = RequiredText(value, "instanceId");
            if (id.Length != 64 || id.Any(character => !char.IsAsciiHexDigit(character)) || name.Length is < 1 or > 4096 || name.Any(char.IsControl)
                || relative != "instance-" + id[..16].ToLowerInvariant() || !roots.Add(relative) || !SafeVersion(instanceId))
                throw new InvalidDataException("迁移实例记录无效。");
            instances.Add(new(id, name, LauncherMigrationFiles.Contained(destination, relative), instanceId));
        }
        if (instances.Count == 0) throw new InvalidDataException("迁移记录缺少实例。");
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<LauncherMigrationFile> files = [];
        foreach (var value in Elements(root, "files", 200_000))
        {
            string relative = RequiredText(value, "path"), hash = RequiredText(value, "sha256");
            if (relative == ReceiptName || !identities.Add(relative) || hash.Length != 64 || hash.Any(character => !char.IsAsciiHexDigit(character))
                || !value.TryGetProperty("bytes", out var length) || length.ValueKind != JsonValueKind.Number
                || !length.TryGetInt64(out long count) || count < 0)
                throw new InvalidDataException("迁移文件记录无效。");
            string full = LauncherMigrationFiles.Contained(destination, relative);
            files.Add(new(full, relative, count, hash));
        }
        List<string> directories = [];
        foreach (var value in Elements(root, "directories", 50_000))
        {
            string relative = value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidDataException("迁移目录记录无效。");
            if (!identities.Add(relative)) throw new InvalidDataException("迁移记录含有重复路径。");
            _ = LauncherMigrationFiles.Contained(destination, relative); directories.Add(relative);
        }
        files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        directories.Sort(StringComparer.Ordinal);
        return new(new(identity + ":" + sha, destination, instances.AsReadOnly()), files.AsReadOnly(), directories.AsReadOnly(), sha, bytes);
    }

    private static JsonElement.ArrayEnumerator Elements(JsonElement root, string property, int maximum)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum)
            throw new InvalidDataException("迁移记录集合无效：" + property);
        return value.EnumerateArray();
    }

    private static string RequiredText(JsonElement root, string property) => Text(root, property)
        ?? throw new InvalidDataException("迁移记录缺少字段：" + property);
}
