using System.Text.Json.Nodes;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Install;

public sealed partial class InstanceModpackExportService
{
    public static async Task<InstanceExportOptions> LoadOptionsAsync(InstanceExportOptionsQuery query, CancellationToken token)
    {
        RequireOptionsPath(query.Path);
        await using var stream = File.OpenRead(query.Path);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("导出配置过大。");
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.ReadByte() != -1) throw new InvalidDataException("导出配置读取期间已变化。");
        var root = JsonNode.Parse(bytes);
        if (root?["schema"]?.GetValue<int>() != 1) throw new InvalidDataException("导出配置格式不受支持。");
        var options = new InstanceExportOptions(root["name"]!.GetValue<string>(), root["version"]!.GetValue<string>(), root["summary"]!.GetValue<string>(),
            root["categories"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray(), root["excluded"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray())
        {
            Format = root["format"]?.GetValue<string>() switch { null or "mrpack" => InstanceExportFormat.Mrpack, "zip" => InstanceExportFormat.Zip, _ => throw new InvalidDataException("导出格式无效。") },
            IncludeGameCore = root["gameCore"]?.GetValue<bool>() ?? false,
            IncludeLibraries = root["libraries"]?.GetValue<bool>() ?? false,
            IncludeAssets = root["assets"]?.GetValue<bool>() ?? false
        };
        ValidateOptions(options); return options;
    }

    public static async Task<XsrResult> SaveOptionsAsync(InstanceExportOptionsSave command, CancellationToken token)
    {
        RequireOptionsPath(command.Path); ValidateOptions(command.Options);
        var options = command.Options;
        var root = new JsonObject
        {
            ["schema"] = 1,
            ["format"] = options.Format == InstanceExportFormat.Zip ? "zip" : "mrpack",
            ["gameCore"] = options.IncludeGameCore,
            ["libraries"] = options.IncludeLibraries,
            ["assets"] = options.IncludeAssets,
            ["name"] = options.Name,
            ["version"] = options.Version,
            ["summary"] = options.Summary,
            ["categories"] = new JsonArray(options.Categories.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()),
            ["excluded"] = new JsonArray(options.ExcludedPaths.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray())
        };
        var bytes = System.Text.Encoding.UTF8.GetBytes(root.ToJsonString());
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("导出配置过大。");
        string temporary = command.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await stream.WriteAsync(bytes, token).ConfigureAwait(false); stream.Flush(true); }
            token.ThrowIfCancellationRequested(); RecoveryBlobStore.CheckLinks(command.Path);
            File.Move(temporary, command.Path, false);
            return XsrResult.Success();
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RequireOptionsPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择完整的 JSON 配置文件路径。");
        RecoveryBlobStore.CheckLinks(path);
    }
    private static void ValidateOptions(InstanceExportOptions options)
    {
        if (!Enum.IsDefined(options.Format) || (options.Format != InstanceExportFormat.Zip && (options.IncludeGameCore || options.IncludeLibraries || options.IncludeAssets)))
            throw new InvalidDataException("导出格式与附加内容选项不匹配。");
        if (options.Name.Length > 256 || options.Version.Length > 128 || options.Summary.Length > 4096
            || options.Categories.Count > 512 || options.ExcludedPaths.Count > 50000) throw new InvalidDataException("导出配置超过限制。");
        foreach (string path in options.Categories.Concat(options.ExcludedPaths)) MinecraftModpackArchive.SafeRelative(path);
    }
}
