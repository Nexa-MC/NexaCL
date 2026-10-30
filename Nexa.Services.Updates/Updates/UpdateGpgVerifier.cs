namespace Nexa.Services.Updates;

/// <summary>Detached GPG admission through the shared cryptographic engine.</summary>
public sealed class UpdateGpgVerifier : IUpdateSignatureVerifier
{
    public const string ReleaseKeyFingerprint = "5701218D69B531E1A7ED35BB6E31F5974A273AEE";
    private readonly string _armoredPublicKey;
    private readonly string _expectedFingerprint;

    public UpdateGpgVerifier(string armoredPublicKey, string? expectedFingerprint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(armoredPublicKey);
        _armoredPublicKey = armoredPublicKey;
        _expectedFingerprint = expectedFingerprint ?? ReleaseKeyFingerprint;
    }

    public Task VerifyAsync(Stream content, Stream detachedSignature, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(detachedSignature);
#if NEXA_EXTERNAL_MODULES
        return NativeCryptoClient.VerifyAsync(_armoredPublicKey, _expectedFingerprint, content, detachedSignature, cancellationToken);
#else
        return new Nexa.Native.Crypto.GpgVerification(_armoredPublicKey, _expectedFingerprint)
            .VerifyAsync(content, detachedSignature, cancellationToken);
#endif
    }
}
