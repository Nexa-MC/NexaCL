using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace Nexa.Services.Minecraft.Management;

public static partial class ContentInspectionService
{
    private static string SchematicSummary(ServerNbt root)
    {
        var metadata = root.Children.FirstOrDefault(t => t.Name == "Metadata") ?? root;
        var schematic = root.Children.FirstOrDefault(t => t.Name == "Schematic") ?? root;
        var lines = metadata.Children.Where(t => t.Type <= 8 && t.Name is "Name" or "Author" or "Description" or "TotalBlocks" or "TotalVolume" or "TimeCreated" or "TimeModified")
            .Concat(schematic.Children.Where(t => t.Type <= 8 && t.Name is "Width" or "Height" or "Length" or "Version" or "DataVersion"))
            .Select(t => t.Name + ": " + t.ScalarText()).ToList();
        var palette = schematic.Children.FirstOrDefault(t => t.Name == "Palette") ?? schematic.Children.FirstOrDefault(t => t.Name == "Blocks")?.Children.FirstOrDefault(t => t.Name == "Palette");
        if (palette is not null) lines.Add("Palette: " + palette.Children.Count);
        var regions = root.Children.FirstOrDefault(t => t.Name == "Regions");
        if (regions is not null) foreach (var region in regions.Children.Take(128))
        {
            var blocks = region.Children.FirstOrDefault(t => t.Name == "BlockStatePalette");
            lines.Add(region.Name + ": " + (blocks?.Children.Count ?? 0) + " palette entries");
        }
        string result = string.Join('\n', lines);
        return result[..Math.Min(result.Length, 8192)];
    }
    private static byte[] SchematicThumbnail(ServerNbt root)
    {
        var data = root.Children.FirstOrDefault(t => t.Name == "Metadata")?.Children.FirstOrDefault(t => t.Name == "PreviewImageData" && t.Type == 11);
        if (data is null || data.Payload.Length < 4) return [];
        int count = BinaryPrimitives.ReadInt32BigEndian(data.Payload), size = (int)Math.Sqrt(count);
        if (count <= 0 || count > 1024 * 1024 || size * size != count) return [];
        using var png = new MemoryStream(); png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, size); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), size); header[8] = 8; header[9] = 6;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            byte[] row = new byte[1 + size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int offset = 4 + (y * size + x) * 4, target = 1 + x * 4;
                    row[target] = data.Payload[offset + 1]; row[target + 1] = data.Payload[offset + 2]; row[target + 2] = data.Payload[offset + 3]; row[target + 3] = data.Payload[offset];
                }
                deflate.Write(row);
            }
        }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []); return png.ToArray();
        void Chunk(string type, byte[] content)
        {
            Span<byte> integer = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(integer, content.Length); png.Write(integer);
            byte[] tag = Encoding.ASCII.GetBytes(type); png.Write(tag); png.Write(content);
            uint crc = uint.MaxValue;
            foreach (byte value in tag.Concat(content)) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u); }
            BinaryPrimitives.WriteUInt32BigEndian(integer, ~crc); png.Write(integer);
        }
    }
}
