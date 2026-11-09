using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexa.Services.Files;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Minecraft.Management;

public static class InstanceResourcePackService
{
    private const int MaximumOptionsBytes = 1024 * 1024;
    private const int MaximumPackEntries = 4096;
    internal static async Task<InstanceResourcePackConfiguration> ReadSafelyAsync(string instance, string game, string version, IReadOnlyList<InstanceContentEntry> entries, CancellationToken token)
    {
        try { return await ReadAsync(instance, game, version, entries, token).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or DecoderFallbackException)
        { return new("", [], "", "Cannot read resource pack settings: " + e.Message[..Math.Min(512, e.Message.Length)]); }
    }
    internal static async Task<InstanceResourcePackConfiguration> ReadAsync(string instance, string game, string version, IReadOnlyList<InstanceContentEntry> entries, CancellationToken token)
    {
        var options = await OptionsAsync(game, token).ConfigureAwait(false);
        var selected = ReadList(options.Text, "resourcePacks");
        var target = await TargetAsync(instance, version, token).ConfigureAwait(false);
        List<InstanceResourcePack> packs = [];
        ArchiveReadBudget metadataBudget = new(32 * 1024 * 1024);
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if (!entry.IsDirectory && !entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            string path = Path.Combine(game, "resourcepacks", entry.Name), problem = "", range = ""; bool? compatible = null;
            try
            {
                JsonObject? pack = await MetadataAsync(path, metadataBudget, token).ConfigureAwait(false);
                if (pack is not null)
                {
                    var single = Version(pack["pack_format"]);
                    var supported = pack["supported_formats"];
                    var minimum = Version(pack["min_format"]) ?? (supported is JsonArray array ? Version(array.FirstOrDefault()) : supported is JsonObject obj ? Version(obj["min_inclusive"]) : Version(supported)) ?? single;
                    var maximum = Version(pack["max_format"]) ?? (supported is JsonArray upperArray ? Version(upperArray.LastOrDefault()) : supported is JsonObject upperObject ? Version(upperObject["max_inclusive"]) : Version(supported)) ?? single;
                    if (minimum is { } min && maximum is { } max && min.CompareTo(max) <= 0)
                    { range = Format(min) + "–" + Format(max); compatible = target is { } t ? t.CompareTo(min) >= 0 && t.CompareTo(max) <= 0 : null; }
                    else problem = "Resource pack format range is missing or invalid.";
                }
                else problem = "pack.mcmeta is missing.";
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            { problem = "Cannot read resource pack metadata: " + e.Message[..Math.Min(e.Message.Length, 512)]; }
            int order = Array.FindIndex(selected, name => name == "file/" + entry.Name || name == entry.Name);
            packs.Add(new(entry.Name, order >= 0, order, range, compatible, problem));
        }
        return new(options.Revision, packs.OrderBy(p => p.Enabled ? p.Order : int.MaxValue).ThenBy(p => p.Name, StringComparer.Ordinal).ToArray(), target is { } format ? Format(format) : "", "");
    }

    public static async Task<XsrResult> SetAsync(InstanceResourcePackCommand command, XsrStateStore store, CancellationToken token = default)
    {
        string? stage = null;
        try
        {
            if (!MinecraftVersionPaths.IsSafeReference(command.Name) || command.Order is < 0 or > 4096) throw new InvalidDataException("Invalid resource pack selection.");
            var snapshot = await InstanceManagementService.ReadAsync(new(command.InstanceDirectory), token).ConfigureAwait(false);
            using var lease = await InstanceRecoveryOperationGate.EnterOperationAsync(Directory.GetParent(snapshot.InstanceDirectory)!.Parent!.FullName, token).ConfigureAwait(false);
            InstanceContentTrash.RejectRunning(snapshot, store, token);
            var options = await OptionsAsync(snapshot.GameDirectory, token).ConfigureAwait(false);
            if (options.Revision != command.ExpectedRevision) throw new IOException("Game options changed; refresh resource packs before editing.");
            var pack = snapshot.ResourcePacks?.Packs.SingleOrDefault(p => p.Name == command.Name) ?? throw new IOException("Resource pack is no longer present.");
            if (command.Enabled && pack.Compatible == false && !command.AllowIncompatible) throw new InvalidDataException("Resource pack is incompatible with this game version.");
            var selected = ReadList(options.Text, "resourcePacks").Where(p => p != "file/" + command.Name && p != command.Name).ToList();
            string name = "file/" + command.Name;
            if (command.Enabled) selected.Insert(Math.Max(selected.TakeWhile(p => !p.StartsWith("file/", StringComparison.Ordinal)).Count(), Math.Min(selected.Count, command.Order)), name);
            var incompatible = ReadList(options.Text, "incompatibleResourcePacks").Where(p => p != name && p != command.Name).ToList();
            if (command.Enabled && pack.Compatible == false) incompatible.Add(name);
            string text = SetList(SetList(options.Text, "resourcePacks", selected), "incompatibleResourcePacks", incompatible);
            if (Encoding.UTF8.GetByteCount(text) > MaximumOptionsBytes)
                throw new InvalidDataException("Updated game options exceed the 1 MiB limit.");
            string path = Path.Combine(snapshot.GameDirectory, "options.txt"); RecoveryBlobStore.CheckLinks(path);
            stage = Path.Combine(snapshot.GameDirectory, ".nexa-options-" + Guid.NewGuid().ToString("N"));
            await File.WriteAllTextAsync(stage, text, new UTF8Encoding(false), token).ConfigureAwait(false);
            if ((await OptionsAsync(snapshot.GameDirectory, token).ConfigureAwait(false)).Revision != options.Revision) throw new IOException("Game options changed during the edit.");
            token.ThrowIfCancellationRequested(); RecoveryBlobStore.CheckLinks(path); File.Move(stage, path, true);
            return XsrResult.Success();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException)
        { return XsrResult.Failure(MinecraftErrors.InvalidRequest(e.Message)); }
        finally { if (stage is not null) File.Delete(stage); }
    }

    private static async Task<(string Text, string Revision)> OptionsAsync(string game, CancellationToken token)
    {
        string path = Path.Combine(game, "options.txt"); RecoveryBlobStore.CheckLinks(path);
        byte[] bytes = [];
        if (File.Exists(path))
        {
            await using var input = File.OpenRead(path); using var output = new MemoryStream();
            await ArchiveReadBudget.CopyAsync(input, output, input.Length, MaximumOptionsBytes, new(MaximumOptionsBytes), token).ConfigureAwait(false); bytes = output.ToArray();
        }
        return (new UTF8Encoding(false, true).GetString(bytes), Convert.ToHexString(SHA256.HashData(bytes)));
    }
    private static string[] ReadList(string text, string key)
    {
        if (text.StartsWith('\uFEFF')) text = text[1..];
        var lines = text.Split('\n').Where(line => line.StartsWith(key + ":", StringComparison.Ordinal)).ToArray();
        if (lines.Length > 1) throw new InvalidDataException("Duplicate game option: " + key);
        if (lines.Length == 0) return [];
        var list = JsonNode.Parse(lines[0][(key.Length + 1)..].Trim()) as JsonArray ?? throw new InvalidDataException("Invalid game option: " + key);
        if (list.Count > MaximumPackEntries || list.Any(p => p is not JsonValue value || !value.TryGetValue<string>(out var name) || name.Length > 4096)) throw new InvalidDataException("Resource pack list exceeds its budget.");
        return list.Select(p => p!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToArray();
    }
    private static string SetList(string text, string key, List<string> values)
    {
        if (values.Count > MaximumPackEntries) throw new InvalidDataException("Resource pack list exceeds its budget.");
        bool hasBom = text.StartsWith('\uFEFF');
        if (hasBom) text = text[1..];
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        string replacement = key + ":" + new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()).ToJsonString();
        int index = lines.FindIndex(line => line.StartsWith(key + ":", StringComparison.Ordinal));
        if (index >= 0) lines[index] = replacement; else lines.Insert(Math.Max(0, lines.Count - 1), replacement);
        return (hasBom ? "\uFEFF" : "") + string.Join(newline, lines);
    }
    private static async Task<JsonObject?> MetadataAsync(string path, ArchiveReadBudget budget, CancellationToken token)
    {
        if (budget.Remaining <= 0) throw new InvalidDataException("Resource metadata budget exhausted.");
        RecoveryBlobStore.CheckLinks(path);
        if (Directory.Exists(path))
        {
            string metadata = Path.Combine(path, "pack.mcmeta"); RecoveryBlobStore.CheckLinks(metadata);
            if (!File.Exists(metadata)) return null;
            await using var file = File.OpenRead(metadata); return await ParseAsync(file, file.Length, budget, token).ConfigureAwait(false);
        }
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 50000) throw new InvalidDataException("Resource archive has too many entries.");
        var entry = archive.GetEntry("pack.mcmeta"); if (entry is null) return null;
        await using var input = entry.Open(); return await ParseAsync(input, entry.Length, budget, token).ConfigureAwait(false);
    }
    private static async Task<JsonObject?> ParseAsync(Stream stream, long length, ArchiveReadBudget budget, CancellationToken token)
    {
        using var output = new MemoryStream(); await ArchiveReadBudget.CopyAsync(stream, output, length, 256 * 1024, budget, token).ConfigureAwait(false);
        return JsonNode.Parse(output.ToArray())?["pack"] as JsonObject;
    }
    private static (int Major, int Minor)? Version(JsonNode? value)
    {
        if (value is JsonValue scalar && scalar.TryGetValue<int>(out int number) && number >= 0) return (number, 0);
        if (value is JsonArray { Count: 2 } pair && pair[0] is JsonValue major && major.TryGetValue<int>(out int a) && pair[1] is JsonValue minor && minor.TryGetValue<int>(out int b) && a >= 0 && b >= 0) return (a, b);
        return null;
    }
    private static string Format((int Major, int Minor) value) => value.Major.ToString(CultureInfo.InvariantCulture) + "." + value.Minor.ToString(CultureInfo.InvariantCulture);
    private static async Task<(int Major, int Minor)?> TargetAsync(string instance, string gameVersion, CancellationToken token)
    {
        string root = Directory.GetParent(instance)!.Parent!.FullName;
        string safeGame = MinecraftVersionPaths.IsSafeReference(gameVersion) ? gameVersion : "_";
        foreach (string jar in new[] { Path.Combine(instance, Path.GetFileName(instance) + ".jar"), Path.Combine(root, "versions", safeGame, safeGame + ".jar") })
        {
            RecoveryBlobStore.CheckLinks(jar); if (!File.Exists(jar)) continue;
            try
            {
                using var archive = ZipFile.OpenRead(jar); var entry = archive.GetEntry("version.json"); if (entry is null) continue;
                await using var input = entry.Open(); using var output = new MemoryStream(); await ArchiveReadBudget.CopyAsync(input, output, entry.Length, 256 * 1024, new(256 * 1024), token).ConfigureAwait(false);
                if (Version(JsonNode.Parse(output.ToArray())?["pack_version"]?["resource"]) is { } version) return version;
            }
            catch (Exception e) when (e is InvalidDataException or JsonException or InvalidOperationException) { }
        }
        if (!System.Version.TryParse(gameVersion, out var game)) return null;
        int number = game.Major != 1 ? 0 : game.Minor switch
        {
            <= 5 => 1,
            <= 8 => 1,
            <= 10 => 2,
            <= 12 => 3,
            <= 14 => 4,
            15 => 5,
            16 => game.Build >= 2 ? 6 : 5,
            17 => 7,
            18 => 8,
            19 => game.Build >= 4 ? 13 : game.Build >= 3 ? 12 : 9,
            20 => game.Build >= 5 ? 32 : game.Build >= 3 ? 22 : game.Build >= 2 ? 18 : 15,
            21 => game.Build >= 11 ? 75 : game.Build >= 9 ? 69 : game.Build >= 7 ? 64 : game.Build >= 6 ? 63 : game.Build >= 5 ? 55 : game.Build >= 4 ? 46 : game.Build >= 2 ? 42 : 34,
            _ => 0
        };
        return number == 0 ? null : (number, 0);
    }
}
