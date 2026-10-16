using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexa.Services.Setup;

/// <summary>Copies verified local instances into an entirely new, owned game library.</summary>
public sealed partial class LauncherMigrationService : IDisposable
{
    private const string ReceiptName = ".nexa-launcher-migration.json";
    private readonly Func<LauncherMigrationInstallRequest, CancellationToken, Task>? _install;
    private readonly Func<bool> _isIdle;
    private readonly ILauncherMigrationReceiptAuthority? _receiptAuthority;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private sealed record PlannedInstance(LauncherMigrationInstance Preview, LauncherMigrationSourceInstance Source,
        IReadOnlyList<LauncherMigrationFile> Files, IReadOnlyList<string> Directories, string DestinationName, string InstanceId);
    private sealed record MigrationPlan(LauncherMigrationPreview Preview, IReadOnlyList<PlannedInstance> Instances);

    public LauncherMigrationService(Func<LauncherMigrationInstallRequest, CancellationToken, Task>? install = null,
        Func<bool>? isIdle = null, ILauncherMigrationReceiptAuthority? receiptAuthority = null)
    { _install = install; _isIdle = isIdle ?? (() => true); _receiptAuthority = receiptAuthority; }

    /// <summary>Releases the operation gate after the owner has awaited all migration operations.</summary>
    public void Dispose() => _operation.Dispose();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The injected migration port exposes preview and transactional apply through the same service lifetime.")]
    public async Task<LauncherMigrationPreview> PreviewAsync(LauncherMigrationQuery query, CancellationToken token = default) =>
        (await PlanAsync(query, token).ConfigureAwait(false)).Preview;

    public async Task<LauncherMigrationReceipt> ApplyAsync(LauncherMigrationApplyCommand command,
        IProgress<LauncherMigrationProgress>? progress = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _operation.WaitAsync(token).ConfigureAwait(false);
        string? stage = null;
        try
        {
            _ = ReceiptAuthority();
            EnsureIdle();
            var plan = await PlanAsync(command.Preview.Query, token).ConfigureAwait(false);
            if (plan.Preview.Revision != command.Preview.Revision)
                throw new IOException("来源在预览后发生变化，请重新预览。");
            if (command.SelectedInstanceIds.Count is < 1 or > 256
                || command.SelectedInstanceIds.Distinct(StringComparer.Ordinal).Count() != command.SelectedInstanceIds.Count)
                throw new InvalidDataException("请选择至少一个有效实例，实例不能重复。");
            var selected = command.SelectedInstanceIds.Select(id => plan.Instances.FirstOrDefault(item => item.Preview.Id == id)
                ?? throw new InvalidDataException("所选实例不在当前预览中。")).ToArray();
            if (selected.Any(item => !item.Preview.CanImport)) throw new InvalidDataException("选择包含无法迁移的实例。");
            string destination = plan.Preview.Query.DestinationDirectory;
            if (Path.Exists(destination)) throw new IOException("目标目录已存在，未覆盖任何文件。");
            string parent = Path.GetDirectoryName(destination) ?? throw new IOException("目标不能为文件系统根目录。");
            LauncherMigrationFiles.CheckLinks(parent); Directory.CreateDirectory(parent);
            stage = Path.Combine(parent, ".nexa-migration-stage-" + Guid.NewGuid().ToString("N"));
            LauncherMigrationFiles.CheckLinks(stage); Directory.CreateDirectory(stage);
            long completed = 0, total = selected.Sum(item => item.Preview.Bytes);
            List<LauncherMigrationImportedInstance> imported = [];
            foreach (var item in selected)
            {
                EnsureIdle(); token.ThrowIfCancellationRequested();
                string root = Path.Combine(stage, item.DestinationName); Directory.CreateDirectory(root);
                if (item.Source.NativeVersion is null)
                {
                    progress?.Report(new("准备游戏与加载器（可能需要下载）", item.Preview.Name, completed, total));
                    await (_install ?? throw new IOException("当前环境未连接游戏安装服务。"))(
                        new(root, item.InstanceId, item.Preview.GameVersion, item.Preview.Loader, item.Preview.LoaderBuild), token).ConfigureAwait(false);
                }
                foreach (string directory in item.Directories)
                    Directory.CreateDirectory(LauncherMigrationFiles.Contained(root, directory));
                foreach (var file in item.Files)
                {
                    EnsureIdle(); token.ThrowIfCancellationRequested();
                    await LauncherMigrationFiles.CopyAsync(file, root, token).ConfigureAwait(false);
                    completed = checked(completed + file.Bytes);
                    progress?.Report(new("复制游戏数据", item.Preview.Name, completed, total));
                }
                await ValidateInstalledAsync(root, item.InstanceId, token).ConfigureAwait(false);
                await WriteFreshMetadataAsync(root, item.InstanceId, item.Preview.Name, token).ConfigureAwait(false);
                imported.Add(new(item.Preview.Id, item.Preview.Name, Path.Combine(destination, item.DestinationName), item.InstanceId));
            }
            progress?.Report(new("复核来源", "", completed, total));
            if ((await PlanAsync(plan.Preview.Query, token).ConfigureAwait(false)).Preview.Revision != plan.Preview.Revision)
                throw new IOException("来源在复制期间发生变化，迁移未提交。");
            var snapshot = await LauncherMigrationFiles.CaptureAsync(stage, token).ConfigureAwait(false);
            if (snapshot.Files.Count >= 200_000) throw new IOException("迁移文件数量没有为撤销记录保留空间，请分批迁移。");
            string identity = Guid.NewGuid().ToString("N");
            var receipt = new LauncherMigrationReceipt(identity, destination, Array.AsReadOnly(imported.ToArray()));
            await WriteReceiptAsync(stage, receipt, snapshot, token).ConfigureAwait(false);
            EnsureIdle(); token.ThrowIfCancellationRequested();
            LauncherMigrationFiles.CheckLinks(destination); LauncherMigrationFiles.CheckLinks(stage);
            Directory.Move(stage, destination); stage = null;
            progress?.Report(new("迁移完成，来源已保留", "", total, total));
            return await ReadReceiptAsync(destination, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            try { if (stage is not null && Directory.Exists(stage)) LauncherMigrationFiles.DeleteOwned(stage); }
            finally { _operation.Release(); }
        }
    }

    private void EnsureIdle()
    { if (!_isIdle()) throw new IOException("请等待游戏和其他文件任务结束后再迁移。"); }

    private ILauncherMigrationReceiptAuthority ReceiptAuthority() => _receiptAuthority
        ?? throw new IOException("迁移凭据验证服务不可用，无法导入、恢复或撤销迁移。");

    private static async Task WriteFreshMetadataAsync(string root, string id, string name, CancellationToken token)
    {
        string directory = Path.Combine(root, "versions", id, "Nexa");
        LauncherMigrationFiles.CheckLinks(directory); Directory.CreateDirectory(directory);
        // Foreign launcher commands, JVM arguments, Java executables and credentials never enter this document.
        var metadata = new JsonObject { ["schemaVersion"] = 1, ["displayName"] = name, ["instanceIsolation"] = true };
        await File.WriteAllTextAsync(Path.Combine(directory, "InstanceMetadata.json"), metadata.ToJsonString(), token).ConfigureAwait(false);
    }

    private static async Task ValidateInstalledAsync(string root, string id, CancellationToken token)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string current = id;
        while (true)
        {
            if (!SafeVersion(current) || !visited.Add(current) || visited.Count > 100)
                throw new InvalidDataException("版本继承链无效。");
            string path = Path.Combine(root, "versions", current, current + ".json");
            using var json = await ReadJsonAsync(path, token).ConfigureAwait(false);
            var value = json.RootElement;
            if (Text(value, "id") != current) throw new InvalidDataException("版本描述与实例标识不一致。");
            if (Text(value, "inheritsFrom") is { Length: > 0 } parent) { current = parent; continue; }
            if (Text(value, "mainClass") is not { Length: > 0 }) throw new InvalidDataException("版本未生成可用的启动描述。");
            return;
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(string path, CancellationToken token)
    {
        LauncherMigrationFiles.CheckLinks(path);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (input.Length > 4 * 1024 * 1024) throw new InvalidDataException("迁移描述文件超过 4 MiB。");
        byte[] bytes = new byte[(int)input.Length]; await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (input.ReadByte() != -1) throw new IOException("描述文件读取期间发生变化。");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
    }

    private static string? Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool SafeVersion(string value) => value.Length is > 0 and <= 128 && value is not "." and not ".."
        && !value.Any(character => char.IsControl(character) || "/\\:<>\"|?*".Contains(character))
        && !value.EndsWith('.') && !value.EndsWith(' ');
}
