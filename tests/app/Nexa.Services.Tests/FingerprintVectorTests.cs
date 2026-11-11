using System.Buffers.Binary;
using System.Security.Cryptography;
using Nexa.Services.Files;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "Checks the published provider SHA1 identity.")]
    private static async ValueTask FingerprintsMatchScalarAcrossReadBoundaries()
    {
        var random = new Random(731);
        foreach (int length in new[] { 0, 1, 3, 4, 15, 16, 17, 31, 32, 33, 63, 64, 65, 65535, 65536, 65537 })
        {
            byte[] bytes = new byte[length]; random.NextBytes(bytes);
            foreach (int pattern in new[] { 0, 1, 2, 3 })
            {
                if (pattern == 1)
                    for (int i = 0; i < bytes.Length; i += 3) bytes[i] = new byte[] { 9, 10, 13, 32 }[i % 4];
                if (pattern == 2) Array.Fill(bytes, (byte)32);
                if (pattern == 3) Array.Fill(bytes, (byte)42);
                var expected = new ContentFingerprint(length, Convert.ToHexString(SHA256.HashData(bytes)),
                    Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant(), Convert.ToHexString(SHA1.HashData(bytes)),
                    ScalarCurseForge(bytes));
                foreach (int chunk in new[] { 1, 3, 17, 31, 32, 33, 65536 })
                {
                    using var input = new FingerprintChunkStream(bytes, chunk);
                    AssertEqual(expected, await ContentFingerprintReader.ReadAsync(input, length));
                }
            }
        }
        using var known = new MemoryStream("a b\r\nc"u8.ToArray());
        AssertEqual(1621425345U, (await ContentFingerprintReader.ReadAsync(known, 64)).CurseForge);
    }

    private static uint ScalarCurseForge(byte[] bytes)
    {
        byte[] normalized = bytes.Where(value => value is not (9 or 10 or 13 or 32)).ToArray();
        unchecked
        {
            uint hash = 1 ^ (uint)normalized.Length;
            int offset = 0;
            for (; offset <= normalized.Length - 4; offset += 4)
            {
                uint word = BinaryPrimitives.ReadUInt32LittleEndian(normalized.AsSpan(offset, 4));
                word *= 0x5bd1e995; word ^= word >> 24; word *= 0x5bd1e995;
                hash = hash * 0x5bd1e995 ^ word;
            }
            if (offset < normalized.Length)
            {
                uint tail = 0;
                for (int shift = 0; offset < normalized.Length; offset++, shift += 8) tail |= (uint)normalized[offset] << shift;
                hash ^= tail; hash *= 0x5bd1e995;
            }
            hash ^= hash >> 13; hash *= 0x5bd1e995; return hash ^ (hash >> 15);
        }
    }

    private sealed class FingerprintChunkStream(byte[] bytes, int chunk) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(chunk, buffer.Length)], cancellationToken);
    }
}
