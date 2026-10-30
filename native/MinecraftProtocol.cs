using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
#if NEXA_NATIVE_ENGINE
using Nexa.Native.Minecraft;
using Nexa.Native.Minecraft.Assets;
using Nexa.Native.Minecraft.Libraries;
#else
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Assets;
using Nexa.Services.Minecraft.Libraries;
#endif

namespace Nexa.Native.Minecraft;

internal static class MinecraftProtocol
{
    internal const int InputLimit = 64 * 1024 * 1024;
    internal const int OutputLimit = 128 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static void Text(BinaryWriter writer, string? text)
    {
        if (text is null) { writer.Write(-1); return; }
        int size = Utf8.GetByteCount(text);
        if (size > InputLimit) throw new InvalidDataException("Minecraft field exceeds the wire budget.");
        writer.Write(size); writer.Write(Utf8.GetBytes(text));
    }
    internal static string? Text(BinaryReader reader)
    {
        int size = reader.ReadInt32();
        if (size == -1) return null;
        if (size < 0 || size > InputLimit || size > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid Minecraft field length.");
        return Utf8.GetString(reader.ReadBytes(size));
    }
    internal static string RequiredText(BinaryReader reader) => Text(reader) ?? throw new InvalidDataException("Missing Minecraft field.");
    internal static void Json(BinaryWriter writer, JsonNode? node) => Text(writer, node?.ToJsonString());
    internal static JsonNode? Json(BinaryReader reader) => Text(reader) is { } text
        ? JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { MaxDepth = 256 }) : null;
    internal static int Count(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > 1_000_000 || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid Minecraft collection length.");
        return count;
    }
    internal static void End(BinaryReader reader)
    {
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing Minecraft wire data.");
    }
    internal static void Features(BinaryWriter writer, IReadOnlyDictionary<string, bool>? features, JsonNode? source, int shape)
    {
        // Snapshot the exact rule keys, retaining the caller dictionary's comparer semantics.
        IEnumerable<JsonObject> rules = shape switch
        {
            1 => source is JsonObject single ? [single] : [],
            2 => source is JsonObject manifest && manifest["libraries"] is JsonArray libraries
                ? libraries.OfType<JsonObject>().SelectMany(library => library["rules"] is JsonArray items ? items.OfType<JsonObject>() : []) : [],
            _ => source is JsonArray items ? items.OfType<JsonObject>() : [],
        };
        var names = rules.SelectMany(rule => rule["features"] is JsonObject values ? values.Select(entry => entry.Key) : [])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        writer.Write(names.Length);
        foreach (string name in names)
        {
            Text(writer, name); writer.Write(features is not null && features.TryGetValue(name, out bool value) && value);
        }
    }
    internal static Dictionary<string, bool> Features(BinaryReader reader)
    {
        int count = Count(reader); var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++) result.Add(RequiredText(reader), reader.ReadBoolean());
        return result;
    }
    internal static void Context(BinaryWriter writer, MinecraftRuleContext context, JsonNode? rules, int shape = 0)
    {
        writer.Write((int)context.OperatingSystem); Text(writer, context.OperatingSystemVersion);
        writer.Write(context.Is64BitArchitecture); writer.Write(context.IsArm64Architecture);
        Features(writer, context.Features, rules, shape);
    }
    internal static MinecraftRuleContext Context(BinaryReader reader) => new(
        (MinecraftLibraryOperatingSystem)reader.ReadInt32(), Text(reader)!, reader.ReadBoolean(), reader.ReadBoolean(), Features(reader));
    internal static void Libraries(BinaryWriter writer, IReadOnlyList<MinecraftLibraryToken> libraries)
    {
        writer.Write(libraries.Count);
        foreach (var library in libraries)
        {
            Text(writer, library.OriginalName); Text(writer, library.NameWithoutVersion); Text(writer, library.Url);
            Text(writer, library.LocalPath); Text(writer, library.Sha1); writer.Write(library.Size);
            writer.Write(library.IsNatives); writer.Write(library.IncludeOnClasspath); writer.Write(library.IsLocal);
        }
    }
    internal static MinecraftLibraryToken[] Libraries(BinaryReader reader)
    {
        var result = new MinecraftLibraryToken[Count(reader)];
        for (int i = 0; i < result.Length; i++) result[i] = new()
        {
            OriginalName = Text(reader), NameWithoutVersion = Text(reader), Url = Text(reader),
            LocalPath = RequiredText(reader), Sha1 = Text(reader), Size = reader.ReadInt64(),
            IsNatives = reader.ReadBoolean(), IncludeOnClasspath = reader.ReadBoolean(), IsLocal = reader.ReadBoolean(),
        };
        return result;
    }
    internal static void Strings(BinaryWriter writer, IReadOnlyList<string> values)
    {
        writer.Write(values.Count); foreach (string value in values) Text(writer, value);
    }
    internal static string[] Strings(BinaryReader reader)
    {
        var result = new string[Count(reader)];
        for (int i = 0; i < result.Length; i++) result[i] = Text(reader)!;
        return result;
    }
    internal static void Assets(BinaryWriter writer, IReadOnlyList<MinecraftAssetToken> assets)
    {
        writer.Write(assets.Count);
        foreach (var asset in assets)
        {
            Text(writer, asset.LocalPath); Text(writer, asset.SourcePath); Text(writer, asset.Hash); writer.Write(asset.Size);
        }
    }
    internal static MinecraftAssetToken[] Assets(BinaryReader reader)
    {
        var result = new MinecraftAssetToken[Count(reader)];
        for (int i = 0; i < result.Length; i++) result[i] = new()
        {
            LocalPath = RequiredText(reader), SourcePath = RequiredText(reader), Hash = RequiredText(reader), Size = reader.ReadInt64(),
        };
        return result;
    }
}

internal sealed class MinecraftWireBuffer(int limit) : MemoryStream
{
    private void Check(int count)
    {
        if (count < 0 || Position > limit - (long)count) throw new InvalidDataException("Minecraft wire budget exceeded.");
    }
    public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
    public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
}
