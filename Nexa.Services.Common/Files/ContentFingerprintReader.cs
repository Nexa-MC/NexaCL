using System.Security.Cryptography;

namespace Nexa.Services.Files;

public sealed record ContentFingerprint(long Length, string Sha256, string Sha512, string Sha1, uint CurseForge);

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
            for (int i = 0; i < read; i++) if (!Whitespace(buffer[i])) normalized++;
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
                for (int i = 0; i < read; i++)
                {
                    byte b = buffer[i]; if (Whitespace(b)) continue;
                    word |= (uint)b << (count * 8);
                    if (++count == 4) { word *= 0x5bd1e995; word ^= word >> 24; word *= 0x5bd1e995; hash = hash * 0x5bd1e995 ^ word; word = 0; count = 0; }
                }
            }
            if (count > 0) { hash ^= word; hash *= 0x5bd1e995; }
            hash ^= hash >> 13; hash *= 0x5bd1e995; hash ^= hash >> 15;
        }
        if (actual != length || input.Length != length || Convert.ToHexString(sha256.GetHashAndReset()) != digest)
            throw new IOException("资源文件在识别期间发生变化。");
        return new(length, digest, Convert.ToHexString(sha512.GetHashAndReset()).ToLowerInvariant(), Convert.ToHexString(sha1.GetHashAndReset()), hash);
    }
    private static bool Whitespace(byte b) => b is 9 or 10 or 13 or 32;
}
