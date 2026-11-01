using System.Text.Json.Nodes;

namespace Nexa.Services.Settings;

/// <summary>Compatibility with existing local recovery documents; native transport uses typed records.</summary>
public static class SettingsRecoveryCodec
{
    public static SettingsRecoverySnapshot Decode(string text)
    {
        if (text.Length > 1024 * 1024) throw new InvalidDataException("启动设置快照超过大小限制。");
        var document = JsonNode.Parse(text) as JsonObject;
        if (document?["version"]?.GetValue<int>() != 1 || document["instance"]?.GetValue<string>() is not { } instance
            || document["values"] is not JsonObject values) throw new InvalidDataException("启动设置快照无效。");
        var definitions = Definitions();
        if (values.Count != definitions.Length) throw new InvalidDataException("启动设置快照字段不完整。");
        List<SettingsRecoveryValue> captured = [];
        foreach (var definition in definitions)
        {
            if (values[definition.Key] is not JsonObject item) throw new InvalidDataException("启动设置快照缺少字段。");
            var original = Read(item["override"]); var effective = Read(item["effective"]) ?? throw new InvalidDataException("启动设置快照缺少有效值。");
            if (definition.Validate(effective) is not null || original is not null && (definition.Validate(original) is not null || original != effective))
                throw new InvalidDataException("启动设置快照包含无效值。");
            captured.Add(new(definition.Key, original, effective));
        }
        return new(instance, captured.AsReadOnly());
    }
    public static string Encode(SettingsRecoverySnapshot snapshot)
    {
        var definitions = Definitions();
        if (snapshot.Values.Count != definitions.Length || snapshot.Values.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            throw new InvalidDataException("启动设置快照字段不完整或重复。");
        JsonObject values = new();
        foreach (var definition in definitions)
        {
            var value = snapshot.Values.SingleOrDefault(item => item.Key == definition.Key) ?? throw new InvalidDataException("启动设置快照缺少字段。");
            values[definition.Key] = new JsonObject { ["override"] = value.Override is null ? null : Override(value.Override), ["effective"] = Override(value.Effective) };
        }
        string result = new JsonObject { ["version"] = 1, ["instance"] = snapshot.InstanceDirectory, ["values"] = values }.ToJsonString();
        _ = Decode(result); return result;
    }
    private static SettingsPolicyDefinition[] Definitions() => SettingsPolicySchema.Definitions
        .Where(item => item.InstanceOverride && item.Key != "instance.development-mode" && !item.Key.StartsWith("recovery.", StringComparison.Ordinal)).ToArray();
    private static JsonObject Override(SettingsOverride value) => new() { ["mode"] = value.Mode.ToString(), ["value"] = value.Value };
    private static SettingsOverride? Read(JsonNode? node)
    {
        if (node is null) return null;
        if (node is not JsonObject obj || obj["mode"]?.GetValue<string>() is not ("Auto" or "Custom")
            || !Enum.TryParse<SettingsOverrideMode>(obj["mode"]!.GetValue<string>(), out var mode))
            throw new InvalidDataException("Invalid stored override.");
        return new(mode, obj["value"]?.GetValue<string>());
    }
}
