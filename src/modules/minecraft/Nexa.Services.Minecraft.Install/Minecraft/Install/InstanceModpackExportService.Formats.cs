using System.Text.Json.Nodes;
using Nexa.Services.Minecraft.Management;
namespace Nexa.Services.Minecraft.Install;

public sealed partial class InstanceModpackExportService
{
    private static (string Name, JsonObject Manifest, string Prefix) FormatManifest(InstanceModpackExportCommand command, JsonObject dependencies)
    {
        var components = dependencies.Where(p => p.Key != "minecraft").ToArray();
        if (components.Length > 1) throw new InvalidDataException("此格式只能导出一个基础加载器。");
        string game = dependencies["minecraft"]!.GetValue<string>();
        string Id(string key) => key switch { "fabric-loader" => "fabric", "quilt-loader" => "quilt", _ => key };
        switch (command.Format)
        {
            case InstanceExportFormat.CurseForge:
                return ("manifest.json", new()
                {
                    ["manifestType"] = "minecraftModpack",
                    ["manifestVersion"] = 1,
                    ["name"] = command.Name,
                    ["version"] = command.Version,
                    ["author"] = "",
                    ["overrides"] = "overrides",
                    ["minecraft"] = new JsonObject { ["version"] = game, ["modLoaders"] = new JsonArray(components.Select(c => (JsonNode)new JsonObject { ["id"] = Id(c.Key) + "-" + c.Value!.GetValue<string>(), ["primary"] = true }).ToArray()) },
                    ["files"] = new JsonArray()
                }, "overrides/");
            case InstanceExportFormat.Mcbbs:
                var addons = new JsonArray { (JsonNode)new JsonObject { ["id"] = "game", ["version"] = game } };
                foreach (var component in components) addons.Add((JsonNode)new JsonObject { ["id"] = Id(component.Key), ["version"] = component.Value!.GetValue<string>() });
                return ("mcbbs.packmeta", new()
                {
                    ["manifestType"] = "minecraftModpack",
                    ["manifestVersion"] = 2,
                    ["name"] = command.Name,
                    ["version"] = command.Version,
                    ["description"] = command.Summary,
                    ["author"] = "",
                    ["addons"] = addons,
                    ["files"] = new JsonArray(),
                    ["libraries"] = new JsonArray()
                }, "overrides/");
            case InstanceExportFormat.MultiMC:
                var mmc = new JsonArray { (JsonNode)new JsonObject { ["uid"] = "net.minecraft", ["version"] = game } };
                foreach (var component in components) mmc.Add((JsonNode)new JsonObject { ["uid"] = component.Key switch { "forge" => "net.minecraftforge", "neoforge" => "net.neoforged", "fabric-loader" => "net.fabricmc.fabric-loader", "quilt-loader" => "org.quiltmc.quilt-loader", _ => throw new InvalidDataException("不支持此 MultiMC 组件。") }, ["version"] = component.Value!.GetValue<string>() });
                return ("mmc-pack.json", new() { ["formatVersion"] = 1, ["components"] = mmc }, ".minecraft/");
            default: throw new InvalidDataException("不支持此导出格式。");
        }
    }
}
