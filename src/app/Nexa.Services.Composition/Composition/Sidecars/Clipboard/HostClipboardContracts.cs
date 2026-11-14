using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostClipboardTransfer(long TransferId);
public sealed record HostClipboardSnapshot(byte[]? TextUtf8, int ImageLength, byte[]? ImageSha256);
public sealed record HostClipboardReadChunk(long TransferId, int Offset, int Length);
public sealed record HostClipboardChunk(int Offset, byte[] Bytes);
public sealed record HostClipboardWrite(long TransferId, byte[]? TextUtf8, int ImageLength, byte[]? ImageSha256);
public sealed record HostClipboardAppend(long TransferId, int Offset, byte[] Bytes);
public sealed record HostClipboardActions(
    Func<CancellationToken, Task<(string? Text, byte[]? Image)>> Read,
    Func<string?, byte[]?, CancellationToken, Task> Write);
public static class SidecarClipboardRoutes
{
    public static readonly XsrSemanticId Read = XsrSemanticId.Parse("interaction.clipboard.read");
    public static readonly XsrSemanticId ReadChunk = XsrSemanticId.Parse("interaction.clipboard.read_chunk");
    public static readonly XsrSemanticId BeginWrite = XsrSemanticId.Parse("interaction.clipboard.begin_write");
    public static readonly XsrSemanticId Append = XsrSemanticId.Parse("interaction.clipboard.append");
    public static readonly XsrSemanticId Commit = XsrSemanticId.Parse("interaction.clipboard.commit");
    public static readonly XsrSemanticId Release = XsrSemanticId.Parse("interaction.clipboard.release");
}
