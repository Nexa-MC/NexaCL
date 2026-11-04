using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Files;
namespace Nexa.Services.Minecraft.Management;

public static partial class ContentInspectionService
{
    private const int Limit = 32 * 1024 * 1024, PageSize = 64;
    public static Task<ContentInspectionSnapshot> ReadAsync(ContentInspectionQuery query, CancellationToken token = default) => Task.Run(async () =>
    {
        if (!Path.IsPathFullyQualified(query.Path) || query.Path.Any(char.IsControl) || query.Offset < 0 || query.Node.Length > 2048 || query.ArchiveEntry.Length > 4096) throw new InvalidDataException("查看路径无效。");
        string path = Path.GetFullPath(query.Path); RecoveryBlobStore.CheckLinks(path);
        if (Directory.Exists(path))
        {
            var entries = Directory.EnumerateFileSystemEntries(path).Take(10001).ToArray();
            if (entries.Length > 10000) throw new InvalidDataException("目录条目过多。");
            return new ContentInspectionSnapshot("Directory", "", path, "", [], entries.Order(StringComparer.Ordinal).Skip(query.Offset).Take(PageSize)
                .Select(p => new ContentInspectionItem(p, Path.GetFileName(p), Directory.Exists(p) ? "Directory" : "File", "", true)).ToArray(), entries.Length);
        }
        await using var input = File.OpenRead(path);
        if (input.Length > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("文件超过 2 GiB。");
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".mca" or ".mcr") return await InspectRegionAsync(input, path, query, token).ConfigureAwait(false);
        if (extension is ".zip" or ".jar" or ".mrpack")
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            if (archive.Entries.Count > 50000 || archive.Entries.Any(e => e.FullName.Length > 4096)) throw new InvalidDataException("压缩包条目过多。");
            if (query.ArchiveEntry.Length == 0)
            {
                var entries = archive.Entries.Where(e => e.Name.Length > 0).OrderBy(e => e.FullName, StringComparer.Ordinal).ToArray();
                return new ContentInspectionSnapshot("ZIP", "", $"{entries.Length} files", "", [], entries.Skip(query.Offset).Take(PageSize)
                    .Select(e => new ContentInspectionItem(e.FullName, e.FullName, "File", e.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes", true)).ToArray(), entries.Length);
            }
            var entry = archive.GetEntry(query.ArchiveEntry) ?? throw new IOException("包内文件已变化。");
            await using var data = entry.Open();
            return InspectBytes(await ReadBoundedAsync(data, entry.Length, token).ConfigureAwait(false), entry.FullName, query, token);
        }
        return InspectBytes(await ReadBoundedAsync(input, input.Length, token).ConfigureAwait(false), path, query, token);
    }, token);

    private static async Task<byte[]> ReadBoundedAsync(Stream input, long length, CancellationToken token)
    {
        using var output = new MemoryStream();
        await ArchiveReadBudget.CopyAsync(input, output, length, Limit, new(Limit), token).ConfigureAwait(false);
        return output.ToArray();
    }
    private static ContentInspectionSnapshot InspectBytes(byte[] bytes, string name, ContentInspectionQuery query, CancellationToken token)
    {
        string revision = Convert.ToHexString(SHA256.HashData(bytes));
        string extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension is ".nbt" or ".dat" or ".dat_old" or ".litematic" or ".schem" or ".schematic")
        {
            using var input = new MemoryStream(bytes); Stream decoded = input;
            if (bytes.AsSpan().StartsWith(new byte[] { 0x1f, 0x8b })) decoded = new GZipStream(input, CompressionMode.Decompress);
            else if (bytes.Length > 1 && (bytes[0] & 15) == 8 && ((bytes[0] << 8) + bytes[1]) % 31 == 0) decoded = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(); byte[] buffer = new byte[81920]; int read;
            while ((read = decoded.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); if (output.Length + read > Limit) throw new InvalidDataException("NBT 解压后超过 32 MiB。"); output.Write(buffer, 0, read); }
            if (decoded != input) decoded.Dispose();
            var root = ServerNbt.ParseDocument(output.ToArray(), token);
            var node = root;
            if (query.Node.Length > 0)
                foreach (string part in query.Node.Split('/'))
                {
                    if (!int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index) || index < 0 || index >= node.Children.Count) throw new InvalidDataException("NBT 节点路径无效。");
                    node = node.Children[index];
                }
            int count = node.Type is 7 or 11 or 12 ? BinaryPrimitives.ReadInt32BigEndian(node.Payload) : node.Children.Count;
            List<ContentInspectionItem> items = [];
            for (int i = query.Offset; i < count && i < (long)query.Offset + PageSize; i++)
            {
                token.ThrowIfCancellationRequested();
                string id = query.Node.Length == 0 ? i.ToString(System.Globalization.CultureInfo.InvariantCulture) : query.Node + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (node.Type is 7 or 11 or 12)
                {
                    int width = node.Type == 7 ? 1 : node.Type == 11 ? 4 : 8;
                    var value = node.Payload.AsSpan(4 + i * width, width);
                    long number = width == 1 ? unchecked((sbyte)value[0]) : width == 4 ? BinaryPrimitives.ReadInt32BigEndian(value) : BinaryPrimitives.ReadInt64BigEndian(value);
                    items.Add(new(id, "[" + i + "]", TypeName(node.Type == 7 ? (byte)1 : node.Type == 11 ? (byte)3 : (byte)4), number.ToString(System.Globalization.CultureInfo.InvariantCulture), false));
                }
                else
                {
                    var child = node.Children[i]; string value = child.ScalarText();
                    items.Add(new(id, child.Name.Length == 0 ? "[" + i + "]" : child.Name[..Math.Min(child.Name.Length, 8192)], TypeName(child.Type), value[..Math.Min(value.Length, 4096)], child.Type is 7 or 9 or 10 or 11 or 12));
                }
            }
            string summary = SchematicSummary(root);
            return new("NBT", revision, summary.Length > 0 ? summary : root.Name, node.ScalarText()[..Math.Min(node.ScalarText().Length, 8192)], query.Node.Length == 0 ? SchematicThumbnail(root) : [], items, count);
        }
        if (extension == ".png")
        {
            if (bytes.Length < 24 || !bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || bytes.Length > 4 * 1024 * 1024
                || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16)) is 0 or > 4096 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20)) is 0 or > 4096) throw new InvalidDataException("图片超过预览预算或格式无效。");
            return new("PNG", revision, name, "", bytes, [], 0);
        }
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("文本预览超过 1 MiB。");
        string text = new UTF8Encoding(false, true).GetString(bytes);
        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) throw new InvalidDataException("此文件不能作为 UTF-8 文本预览。");
        int textOffset = Math.Min(text.Length, checked((int)Math.Min(int.MaxValue, (long)query.Offset * 128)));
        return new("Text", revision, name, text.Substring(textOffset, Math.Min(8192, text.Length - textOffset)), [], [], (text.Length + 127) / 128);
    }
    private static string TypeName(byte type) => type switch { 1 => "Byte", 2 => "Short", 3 => "Int", 4 => "Long", 5 => "Float", 6 => "Double", 7 => "ByteArray", 8 => "String", 9 => "List", 10 => "Compound", 11 => "IntArray", 12 => "LongArray", _ => "End" };
}
