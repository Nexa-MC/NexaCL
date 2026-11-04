using System.Buffers.Binary;
using System.Globalization;

namespace Nexa.Services.Minecraft.Management;

public static partial class ContentInspectionService
{
    private static async Task<ContentInspectionSnapshot> InspectRegionAsync(Stream input, string path, ContentInspectionQuery query, CancellationToken token)
    {
        if (input.Length < 8192 || input.Length % 4096 != 0) throw new InvalidDataException("Invalid region file length.");
        byte[] header = new byte[8192]; await input.ReadExactlyAsync(header, token).ConfigureAwait(false);
        List<(int Index, long Start, long End)> chunks = [];
        for (int i = 0; i < 1024; i++)
        {
            uint location = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(i * 4));
            if (location == 0) continue;
            long start = (location >> 8) * 4096L, end = start + (location & 255) * 4096L;
            if (start < 8192 || start == end || end > input.Length) throw new InvalidDataException("Region chunk points outside its file.");
            chunks.Add((i, start, end));
        }
        long previous = 8192;
        foreach (var chunk in chunks.OrderBy(c => c.Start))
        { if (chunk.Start < previous) throw new InvalidDataException("Region chunks overlap."); previous = chunk.End; }
        string revision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(header));
        if (query.ArchiveEntry.Length == 0)
            return new("Region", revision, Path.GetFileName(path), "", [], chunks.OrderBy(c => c.Index).Skip(query.Offset).Take(PageSize)
                .Select(c => new ContentInspectionItem("chunk:" + c.Index, "[" + c.Index % 32 + ", " + c.Index / 32 + "]", "Chunk", "sector " + c.Start / 4096, true)).ToArray(), chunks.Count);
        if (!query.ArchiveEntry.StartsWith("chunk:", StringComparison.Ordinal) || !int.TryParse(query.ArchiveEntry.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            throw new InvalidDataException("Invalid region chunk selection.");
        var selected = chunks.SingleOrDefault(c => c.Index == index);
        if (selected.End == 0) throw new IOException("Region chunk no longer exists.");
        input.Position = selected.Start;
        byte[] prefix = new byte[5]; await input.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        int compression = prefix[4] & 127;
        if (length < 1 || length > Limit || length + 4L > selected.End - selected.Start) throw new InvalidDataException("Invalid region chunk length.");
        byte[] bytes;
        if ((prefix[4] & 128) != 0)
        {
            if (length != 1) throw new InvalidDataException("Invalid external chunk marker.");
            string[] parts = Path.GetFileName(path).Split('.');
            if (parts.Length != 4 || parts[0] != "r" || !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int rx)
                || !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int rz)) throw new InvalidDataException("Cannot resolve external region coordinates.");
            long x = rx * 32L + index % 32, z = rz * 32L + index / 32;
            string external = Path.Combine(Path.GetDirectoryName(path)!, $"c.{x.ToString(CultureInfo.InvariantCulture)}.{z.ToString(CultureInfo.InvariantCulture)}.mcc");
            RecoveryBlobStore.CheckLinks(external);
            await using var source = File.OpenRead(external); bytes = await ReadBoundedAsync(source, source.Length, token).ConfigureAwait(false);
        }
        else { bytes = new byte[length - 1]; await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false); }
        if (compression == 4) bytes = DecodeLz4Blocks(bytes, token);
        else if (compression is < 1 or > 3) throw new InvalidDataException("Unsupported region compression.");
        if (compression == 1 && !bytes.AsSpan().StartsWith(new byte[] { 31, 139 }) || compression == 2 && (bytes.Length < 2 || (bytes[0] & 15) != 8))
            throw new InvalidDataException("Region compression does not match its payload.");
        return InspectBytes(bytes, "chunk.nbt", query, token);
    }

    private static byte[] DecodeLz4Blocks(byte[] bytes, CancellationToken token)
    {
        using var output = new MemoryStream(); int offset = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            if (bytes.Length - offset < 21 || !bytes.AsSpan(offset, 8).SequenceEqual("LZ4Block"u8)) throw new InvalidDataException("Invalid LZ4 block header.");
            int method = bytes[offset + 8] & 240, maximum = 1 << (10 + (bytes[offset + 8] & 15));
            int encoded = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 9)), decoded = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 13));
            uint checksum = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 17)); offset += 21;
            if (encoded == 0 && decoded == 0) { if (checksum != 0 || offset != bytes.Length) throw new InvalidDataException("Invalid LZ4 terminator."); return output.ToArray(); }
            if (encoded <= 0 || decoded <= 0 || decoded > maximum || encoded > bytes.Length - offset || output.Length + decoded > Limit) throw new InvalidDataException("LZ4 block exceeds its budget.");
            byte[] block = new byte[decoded]; int read = offset, end = offset + encoded, written = 0;
            if (method == 16) { if (encoded != decoded) throw new InvalidDataException("Invalid raw LZ4 block."); bytes.AsSpan(offset, encoded).CopyTo(block); written = decoded; }
            else if (method == 32)
            {
                while (read < end)
                {
                    int marker = bytes[read++], literal = Extended(marker >> 4);
                    if (literal > end - read || literal > decoded - written) throw new InvalidDataException("Invalid LZ4 literals.");
                    bytes.AsSpan(read, literal).CopyTo(block.AsSpan(written)); read += literal; written += literal;
                    if (read == end) break;
                    if (end - read < 2) throw new InvalidDataException("Truncated LZ4 match.");
                    int distance = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(read)); read += 2;
                    int match = checked(Extended(marker & 15) + 4);
                    if (distance == 0 || distance > written || match > decoded - written) throw new InvalidDataException("Invalid LZ4 match.");
                    for (int i = 0; i < match; i++) { block[written] = block[written - distance]; written++; }
                }
            }
            else throw new InvalidDataException("Unknown LZ4 method.");
            if (written != decoded || (XxHash(block) & 0x0FFFFFFF) != checksum) throw new InvalidDataException("LZ4 block checksum or length mismatch.");
            output.Write(block); offset = end;
            int Extended(int count)
            {
                if (count != 15) return count;
                int next;
                do { if (read >= end) throw new InvalidDataException("Truncated LZ4 length."); next = bytes[read++]; count = checked(count + next); } while (next == 255);
                return count;
            }
        }
        throw new InvalidDataException("Missing LZ4 terminator.");
    }

    private static uint XxHash(ReadOnlySpan<byte> bytes)
    {
        unchecked
        {
            const uint p1 = 2654435761, p2 = 2246822519, p3 = 3266489917, p4 = 668265263, p5 = 374761393;
            static uint Rotate(uint value, int shift) => (value << shift) | (value >> (32 - shift));
            static uint Round(uint acc, uint value) => Rotate(unchecked(acc + value * p2), 13) * p1;
            int i = 0; uint hash;
            if (bytes.Length >= 16)
            {
                uint a = 0x9747b28c + p1 + p2, b = 0x9747b28c + p2, c = 0x9747b28c, d = 0x9747b28c - p1;
                do { a = Round(a, BinaryPrimitives.ReadUInt32LittleEndian(bytes[i..])); b = Round(b, BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i + 4)..])); c = Round(c, BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i + 8)..])); d = Round(d, BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i + 12)..])); i += 16; } while (i <= bytes.Length - 16);
                hash = Rotate(a, 1) + Rotate(b, 7) + Rotate(c, 12) + Rotate(d, 18);
            }
            else hash = 0x9747b28c + p5;
            hash += (uint)bytes.Length;
            while (i <= bytes.Length - 4) { hash = Rotate(hash + BinaryPrimitives.ReadUInt32LittleEndian(bytes[i..]) * p3, 17) * p4; i += 4; }
            while (i < bytes.Length) hash = Rotate(hash + bytes[i++] * p5, 11) * p1;
            hash ^= hash >> 15; hash *= p2; hash ^= hash >> 13; hash *= p3; return hash ^ (hash >> 16);
        }
    }
}
