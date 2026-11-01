namespace Nexa.Services.Files;

public sealed record ContentFingerprint(long Length, string Sha256, string Sha512, string Sha1, uint CurseForge);
