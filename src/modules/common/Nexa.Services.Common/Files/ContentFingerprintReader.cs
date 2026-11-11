using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;

namespace Nexa.Services.Files;

/// <summary>Streaming provider identities with one consistent, bounded source snapshot.</summary>
public static class ContentFingerprintReader
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "SHA1 and Murmur2 are published CurseForge file identities, alongside SHA256 and SHA512.")]
    public static async Task<ContentFingerprint> ReadAsync(Stream input, long maximumBytes, CancellationToken token = default)
    {
        if (!input.CanSeek || input.Length < 0 || input.Length > maximumBytes) throw new InvalidDataException("资源文件超过识别大小限制。");
        long length = input.Length, actual = 0;
        input.Position = 0;
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        byte[] buffer = new byte[65536]; uint normalized = 0; int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            actual += read; if (actual > length) throw new IOException("资源文件在识别期间发生变化。");
            sha256.AppendData(buffer, 0, read); sha512.AppendData(buffer, 0, read); sha1.AppendData(buffer, 0, read);
            normalized += (uint)Normalize(buffer.AsSpan(0, read), countOnly: true);
        }
        if (actual != length) throw new IOException("资源文件在识别期间发生变化。");
        string digest = Convert.ToHexString(sha256.GetHashAndReset());
        input.Position = 0; actual = 0;
        uint hash = 1 ^ normalized, word = 0; int count = 0;
        unchecked
        {
            while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                actual += read; if (actual > length) throw new IOException("资源文件在识别期间发生变化。");
                sha256.AppendData(buffer, 0, read);
                int retained = Normalize(buffer.AsSpan(0, read), countOnly: false), i = 0;
                // A stream read may split a normalized Murmur word at any byte.
                while (count != 0 && i < retained)
                {
                    word |= (uint)buffer[i++] << (count * 8);
                    if (++count == 4)
                    {
                        word *= 0x5bd1e995; word ^= word >> 24; word *= 0x5bd1e995;
                        hash = hash * 0x5bd1e995 ^ word; word = 0; count = 0;
                    }
                }
                for (; i <= retained - 4; i += 4)
                {
                    uint next = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i, 4));
                    next *= 0x5bd1e995; next ^= next >> 24; next *= 0x5bd1e995;
                    hash = hash * 0x5bd1e995 ^ next;
                }
                for (; i < retained; i++) word |= (uint)buffer[i] << (count++ * 8);
            }
            if (count > 0) { hash ^= word; hash *= 0x5bd1e995; }
            hash ^= hash >> 13; hash *= 0x5bd1e995; hash ^= hash >> 15;
        }
        if (actual != length || input.Length != length || Convert.ToHexString(sha256.GetHashAndReset()) != digest)
            throw new IOException("资源文件在识别期间发生变化。");
        return new(length, digest, Convert.ToHexString(sha512.GetHashAndReset()).ToLowerInvariant(), Convert.ToHexString(sha1.GetHashAndReset()), hash);
    }

    // Hash the original bytes before compacting. Full vector blocks are bounds checked,
    // and stores always trail reads, so in-place compaction preserves unread bytes.
    private static int Normalize(Span<byte> bytes, bool countOnly)
    {
        int read = 0, written = 0;
        ref byte start = ref MemoryMarshal.GetReference(bytes);
        if (Vector256.IsHardwareAccelerated)
        {
            for (; read <= bytes.Length - Vector256<byte>.Count; read += Vector256<byte>.Count)
            {
                var value = Vector256.LoadUnsafe(ref start, (nuint)read);
                var whitespace = Vector256.Equals(value, Vector256.Create((byte)9))
                    | Vector256.Equals(value, Vector256.Create((byte)10))
                    | Vector256.Equals(value, Vector256.Create((byte)13))
                    | Vector256.Equals(value, Vector256.Create((byte)32));
                if (countOnly)
                    written += Vector256<byte>.Count - BitOperations.PopCount(whitespace.ExtractMostSignificantBits());
                else if (whitespace == Vector256<byte>.Zero)
                {
                    value.StoreUnsafe(ref start, (nuint)written); written += Vector256<byte>.Count;
                }
                else
                    for (int i = read; i < read + Vector256<byte>.Count; i++)
                        if (!Whitespace(bytes[i])) bytes[written++] = bytes[i];
            }
        }
        if (Vector128.IsHardwareAccelerated)
        {
            for (; read <= bytes.Length - Vector128<byte>.Count; read += Vector128<byte>.Count)
            {
                var value = Vector128.LoadUnsafe(ref start, (nuint)read);
                var whitespace = Vector128.Equals(value, Vector128.Create((byte)9))
                    | Vector128.Equals(value, Vector128.Create((byte)10))
                    | Vector128.Equals(value, Vector128.Create((byte)13))
                    | Vector128.Equals(value, Vector128.Create((byte)32));
                if (countOnly)
                    written += Vector128<byte>.Count - BitOperations.PopCount(whitespace.ExtractMostSignificantBits());
                else if (whitespace == Vector128<byte>.Zero)
                {
                    value.StoreUnsafe(ref start, (nuint)written); written += Vector128<byte>.Count;
                }
                else
                    for (int i = read; i < read + Vector128<byte>.Count; i++)
                        if (!Whitespace(bytes[i])) bytes[written++] = bytes[i];
            }
        }
        for (; read < bytes.Length; read++)
        {
            if (Whitespace(bytes[read])) continue;
            if (!countOnly) bytes[written] = bytes[read];
            written++;
        }
        return written;
    }
    private static bool Whitespace(byte b) => b is 9 or 10 or 13 or 32;
}
