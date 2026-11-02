using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Nexa.Services.Files;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop;

internal static partial class Program
{
    private const int ThemeStartupCacheMaximumBytes = 768 * 1024;
    private const int ThemeStartupPngMaximumBytes = 256 * 1024;
    internal static string ThemeStartupCachePath(string root) => Path.Combine(root, FolderNames.Settings, "ntp-startup-cache.json");

    internal static async Task SaveThemeStartupCacheAsync(string root, string sourceIdentity, AvaloniaUiThemeStartupSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ValidStartupSourceIdentity(sourceIdentity) || snapshot.Generation < 0 || !Enum.IsDefined(snapshot.Motion)
            || snapshot.BrandLightPng.IsEmpty != snapshot.BrandDarkPng.IsEmpty
            || !ValidStartupPng(snapshot.BrandLightPng.Span) || !ValidStartupPng(snapshot.BrandDarkPng.Span))
            throw new InvalidDataException("Invalid finite theme startup snapshot.");
        using MemoryStream bytes = new();
        using (Utf8JsonWriter writer = new(bytes))
        {
            writer.WriteStartObject(); writer.WriteNumber("version", 1); writer.WriteString("sourceIdentity", sourceIdentity);
            writer.WriteNumber("generation", snapshot.Generation);
            writer.WriteBase64String("lightPng", snapshot.BrandLightPng.Span); writer.WriteBase64String("darkPng", snapshot.BrandDarkPng.Span);
            writer.WriteString("motion", snapshot.Motion.ToString());
            if (snapshot.Colors is { } colors)
            {
                writer.WriteStartObject("colors");
                WriteStartupColor(writer, "background", colors.Background); WriteStartupColor(writer, "foreground", colors.Foreground);
                WriteStartupColor(writer, "accent", colors.Accent); writer.WriteEndObject();
            }
            else writer.WriteNull("colors");
            writer.WriteEndObject(); writer.Flush();
        }
        if (bytes.Length > ThemeStartupCacheMaximumBytes) throw new InvalidDataException("Theme startup cache exceeds its budget.");
        string path = ThemeStartupCachePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes.GetBuffer().AsMemory(0, (int)bytes.Length)).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteStartupColor(Utf8JsonWriter writer, string key, XsrUiColor color)
    {
        writer.WriteStartArray(key); writer.WriteNumberValue(color.Red); writer.WriteNumberValue(color.Green);
        writer.WriteNumberValue(color.Blue); writer.WriteNumberValue(color.Alpha); writer.WriteEndArray();
    }

    internal static AvaloniaUiThemeStartupSnapshot? ReadThemeStartupCache(string root, string sourceIdentity)
    {
        if (!ValidStartupSourceIdentity(sourceIdentity)) return null;
        try
        {
            using FileStream stream = new(ThemeStartupCachePath(root), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > ThemeStartupCacheMaximumBytes) return null;
            using MemoryStream snapshot = new((int)Math.Min(stream.Length, 64 * 1024));
            byte[] buffer = new byte[8192]; int remaining = ThemeStartupCacheMaximumBytes;
            while (true)
            {
                int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining + 1));
                if (read == 0) break;
                if (read > remaining) return null;
                snapshot.Write(buffer, 0, read); remaining -= read;
            }
            using var document = JsonDocument.Parse(snapshot.GetBuffer().AsMemory(0, (int)snapshot.Length), new JsonDocumentOptions { MaxDepth = 8 });
            var node = document.RootElement;
            if (!StartupFields(node, "version", "sourceIdentity", "generation", "lightPng", "darkPng", "motion", "colors")
                || !node.GetProperty("version").TryGetInt32(out int version) || version != 1
                || node.GetProperty("sourceIdentity").ValueKind != JsonValueKind.String
                || node.GetProperty("sourceIdentity").GetString() != sourceIdentity
                || !node.GetProperty("generation").TryGetInt64(out long generation) || generation < 0
                || node.GetProperty("motion").ValueKind != JsonValueKind.String
                || !Enum.TryParse<AvaloniaUiThemeSplashMotion>(node.GetProperty("motion").GetString(), out var motion)
                || !Enum.IsDefined(motion) || node.GetProperty("motion").GetString() != motion.ToString()) return null;
            byte[]? light = StartupPng(node.GetProperty("lightPng")), dark = StartupPng(node.GetProperty("darkPng"));
            if (light is null || dark is null || (light.Length == 0) != (dark.Length == 0)) return null;
            AvaloniaUiThemeStartupColors? colors = null;
            var colorNode = node.GetProperty("colors");
            if (colorNode.ValueKind != JsonValueKind.Null)
            {
                if (!StartupFields(colorNode, "background", "foreground", "accent")) return null;
                var background = StartupColor(colorNode.GetProperty("background")); var foreground = StartupColor(colorNode.GetProperty("foreground"));
                var accent = StartupColor(colorNode.GetProperty("accent"));
                if (background is null || foreground is null || accent is null) return null;
                colors = new(background.Value, foreground.Value, accent.Value);
            }
            return new(generation, light, dark, colors, motion);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or OverflowException)
        { return null; }
    }

    private static bool StartupFields(JsonElement node, params string[] expected)
    {
        if (node.ValueKind != JsonValueKind.Object) return false;
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
            if (!expected.Contains(property.Name, StringComparer.Ordinal) || !names.Add(property.Name)) return false;
        return names.Count == expected.Length;
    }

    private static XsrUiColor? StartupColor(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() != 4) return null;
        byte[] bytes = new byte[4]; int index = 0;
        foreach (var value in node.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetByte(out byte part)) return null;
            bytes[index++] = part;
        }
        return new(bytes[0], bytes[1], bytes[2], bytes[3]);
    }

    private static byte[]? StartupPng(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.String || node.GetString()!.Length > 4 * ((ThemeStartupPngMaximumBytes + 2) / 3)
            || !node.TryGetBytesFromBase64(out byte[]? bytes) || bytes is null || !ValidStartupPng(bytes)) return null;
        return bytes;
    }

    private static bool ValidStartupSourceIdentity(string? value) => value is { Length: > 0 and <= 128 }
        && char.IsAsciiLetterOrDigit(value[0]) && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool ValidStartupPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return true;
        if (bytes.Length is < 57 or > ThemeStartupPngMaximumBytes || !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        int width = 0, height = 0, channels = 0; bool dataSeen = false, dataClosed = false, ended = false, palette = false;
        using MemoryStream compressed = new();
        for (int offset = 8; offset < bytes.Length;)
        {
            if (bytes.Length - offset < 12) return false;
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (length > bytes.Length - offset - 12) return false;
            int count = (int)length; var kind = bytes.Slice(offset + 4, 4); var payload = bytes.Slice(offset + 8, count);
            if (StartupPngCrc(bytes.Slice(offset + 4, count + 4)) != BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 8 + count, 4))) return false;
            if (offset == 8)
            {
                if (!kind.SequenceEqual("IHDR"u8) || count != 13) return false;
                width = BinaryPrimitives.ReadInt32BigEndian(payload[..4]); height = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(4, 4));
                if (width is < 1 or > 512 || height is < 1 or > 512 || payload[8] != 8 || payload[9] is not (2 or 6)
                    || payload[10] != 0 || payload[11] != 0 || payload[12] != 0) return false;
                channels = payload[9] == 6 ? 4 : 3;
            }
            else if (kind.SequenceEqual("IDAT"u8))
            { if (dataClosed) return false; dataSeen = true; compressed.Write(payload); }
            else if (kind.SequenceEqual("IEND"u8))
            { if (count != 0 || !dataSeen || offset + 12 != bytes.Length) return false; ended = true; }
            else
            {
                if (dataSeen) dataClosed = true;
                if (kind.SequenceEqual("PLTE"u8))
                { if (palette || dataSeen || count is < 3 or > 768 || count % 3 != 0) return false; palette = true; }
                else if ((kind[0] & 32) == 0) return false;
                for (int index = 0; index < kind.Length; index++) if (!char.IsAsciiLetter((char)kind[index])) return false;
                if (kind.SequenceEqual("acTL"u8) || kind.SequenceEqual("fcTL"u8) || kind.SequenceEqual("fdAT"u8)) return false;
            }
            offset += count + 12;
        }
        if (!ended || width == 0 || compressed.Length == 0) return false;
        try
        {
            compressed.Position = 0; using ZLibStream decoder = new(compressed, CompressionMode.Decompress);
            byte[] buffer = new byte[8192]; int rowBytes = width * channels + 1, expected = rowBytes * height, decoded = 0;
            while (true)
            {
                int read = decoder.Read(buffer);
                if (read == 0) break;
                if (read > expected - decoded) return false;
                for (int index = 0; index < read; index++) if ((decoded + index) % rowBytes == 0 && buffer[index] > 4) return false;
                decoded += read;
            }
            return decoded == expected;
        }
        catch (InvalidDataException) { return false; }
    }

    private static uint StartupPngCrc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        return ~crc;
    }
}
