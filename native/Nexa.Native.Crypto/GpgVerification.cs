using System.Text;

using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;

namespace Nexa.Native.Crypto;

/// <summary>
/// Detached ASCII-armored GPG verification over the release public key. The armored public
/// key and expected fingerprint are supplied by the composition root. The engine never
/// selects a trust root on behalf of its caller.
/// </summary>
public sealed class GpgVerification
{
    private const int MaximumSignatureEnvelopeBytes = 1024 * 1024;

    private readonly string _armoredPublicKey;
    private readonly string _expectedFingerprint;

    public GpgVerification(string armoredPublicKey, string expectedFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(armoredPublicKey);
        _armoredPublicKey = armoredPublicKey;
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        _expectedFingerprint = expectedFingerprint;
    }

    public async Task VerifyAsync(
        Stream content,
        Stream detachedSignature,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(detachedSignature);

        try
        {
            // Armored decoding probes/rewinds. Bound actual input and decompressed bytes.
            using MemoryStream signatureBuffer = await ReadEnvelopeAsync(detachedSignature, cancellationToken).ConfigureAwait(false);
            Session session = Begin(signatureBuffer.ToArray());
            byte[] buffer = new byte[128 * 1024];
            while (true)
            {
                int read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                session.Update(buffer.AsSpan(0, read));
            }

            session.Finish();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception failure) when (failure is PgpException or CryptoException or FormatException
            or ArgumentException or InvalidOperationException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidDataException("GPG 签名或公钥数据无效。", failure);
        }
    }

    public Session Begin(byte[] envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Length > MaximumSignatureEnvelopeBytes) throw new InvalidDataException("GPG signature envelope exceeds its limit.");
        using var signatureBuffer = new MemoryStream(envelope, writable: false);
        PgpSignature signature = ReadSignatureAsync(signatureBuffer, CancellationToken.None).GetAwaiter().GetResult();
        if (signature.SignatureType != PgpSignature.BinaryDocument
            || signature.HashAlgorithm is not (HashAlgorithmTag.Sha256 or HashAlgorithmTag.Sha384 or HashAlgorithmTag.Sha512))
            throw new InvalidDataException("GPG 签名必须使用二进制文档类型和 SHA-256/384/512 摘要。");
        PgpPublicKey publicKey = LoadPublicKey(signature.KeyId);
        string fingerprint = Convert.ToHexString(publicKey.GetFingerprint());
        if (!string.Equals(fingerprint, _expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"GPG 公钥指纹不匹配：{fingerprint}。");

        // Policy packets come from the composition root's trusted keyring, not a
        // caller-supplied keyring authorized merely by a matching fingerprint.
        if (publicKey.IsRevoked()) throw new InvalidDataException("GPG 签名密钥已被吊销。");
        long validSeconds = publicKey.GetValidSeconds();
        if (validSeconds > 0 && DateTime.UtcNow - publicKey.CreationTime.ToUniversalTime() >= TimeSpan.FromSeconds(validSeconds))
            throw new InvalidDataException("GPG 签名密钥已过期。");

        signature.InitVerify(publicKey);
        return new Session(signature);
    }

    public sealed class Session
    {
        private readonly PgpSignature _signature;
        internal Session(PgpSignature signature) => _signature = signature;
        public void Update(ReadOnlySpan<byte> content) => _signature.Update(content);
        public void Finish()
        {
            if (!_signature.Verify())
                throw new InvalidDataException("GPG 签名校验失败，更新文件可能已被修改。");

            PgpSignatureSubpacketVector? hashed = _signature.GetHashedSubPackets();
            long expires = hashed?.GetSignatureExpirationTime() ?? 0;
            if (expires > 0)
            {
                if (!hashed!.HasSignatureCreationTime())
                    throw new InvalidDataException("GPG 签名过期策略缺少已认证的创建时间。");
                if (DateTime.UtcNow - hashed.GetSignatureCreationTime().ToUniversalTime() >= TimeSpan.FromSeconds(expires))
                    throw new InvalidDataException("GPG 签名已过期。");
            }
        }
    }

    private static async Task<MemoryStream> ReadEnvelopeAsync(Stream source, CancellationToken token)
    {
        MemoryStream buffer = new();
        try
        {
            byte[] chunk = new byte[8192];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int read = await source.ReadAsync(chunk, token).ConfigureAwait(false);
                if (read == 0) break;
                if (read > MaximumSignatureEnvelopeBytes - buffer.Length)
                    throw new InvalidDataException("GPG 签名封套超过大小限制。");
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            return buffer;
        }
        catch { buffer.Dispose(); throw; }
    }

    private static async Task<PgpSignature> ReadSignatureAsync(Stream detachedSignature, CancellationToken token)
    {
        using Stream decoded = PgpUtilities.GetDecoderStream(detachedSignature);
        PgpObjectFactory factory = new(decoded);
        PgpObject? value = factory.NextPgpObject();
        if (value is PgpCompressedData compressed)
        {
            using Stream compressedData = compressed.GetDataStream();
            using MemoryStream plain = await ReadEnvelopeAsync(compressedData, token).ConfigureAwait(false);
            value = new PgpObjectFactory(plain).NextPgpObject();
        }

        if (value is not PgpSignatureList signatures || signatures.Count == 0)
        {
            throw new InvalidDataException("更新签名不是有效的 detached GPG 签名。");
        }

        return signatures[0];
    }

    private PgpPublicKey LoadPublicKey(long keyId)
    {
        byte[] armored = Encoding.ASCII.GetBytes(_armoredPublicKey);
        using MemoryStream armoredStream = new(armored);
        using Stream decoded = PgpUtilities.GetDecoderStream(armoredStream);
        PgpPublicKeyRingBundle bundle = new(decoded);
        PgpPublicKey? key = bundle.GetPublicKey(keyId);
        return key ?? throw new InvalidDataException($"更新签名使用了未授权的 GPG 密钥：{keyId:X16}。");
    }
}
