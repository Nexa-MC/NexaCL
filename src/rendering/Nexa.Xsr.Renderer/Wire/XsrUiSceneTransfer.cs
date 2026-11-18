using System.Buffers.Binary;

namespace Nexa.Xsr.Renderer.Wire;

// Lossless byte-range transfer of a canonical scene packet. Its header (including
// sequence and version) is always literal. The normal decoder admits the result.
internal static class XsrUiSceneTransfer
{
    private const int RetentionLimit = 4 * 1024 * 1024;
    private static readonly int HeaderLength = 2 * sizeof(int) + UiWireLimits.Utf8.GetByteCount(UiWireSchema.Fingerprint) + 3 * sizeof(long);

    internal static ReadOnlyMemory<byte> Retain(ReadOnlyMemory<byte> frame) =>
        frame.Length <= RetentionLimit ? frame : default;

    internal static void Write(UiWireWriter writer, ReadOnlyMemory<byte> frame, ReadOnlyMemory<byte> previous)
    {
        if (previous.Length >= HeaderLength && frame.Length >= HeaderLength && frame.Length <= RetentionLimit)
        {
            var body = frame.Span[HeaderLength..];
            var prior = previous.Span[HeaderLength..];
            int prefix = body.CommonPrefixLength(prior);
            int suffix = CommonSuffix(body[prefix..], prior[prefix..]);
            if (prefix + suffix > 3 * sizeof(int))
            {
                writer.Boolean(true);
                writer.Int32(prefix); writer.Int32(suffix);
                writer.Buffer(frame[..HeaderLength]);
                writer.Buffer(frame.Slice(HeaderLength + prefix, body.Length - prefix - suffix));
                return;
            }
        }
        writer.Boolean(false);
        writer.Buffer(frame);
    }

    private static int CommonSuffix(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        int limit = Math.Min(left.Length, right.Length);
        int matched = 0;
        const int block = 128;
        while (limit - matched >= block && left.Slice(left.Length - matched - block, block)
            .SequenceEqual(right.Slice(right.Length - matched - block, block)))
            matched += block;
        while (matched < limit && left[^(matched + 1)] == right[^(matched + 1)]) matched++;
        return matched;
    }

    internal static byte[] Read(UiWireReader reader, ReadOnlyMemory<byte> previous, long previousSequence)
    {
        if (!reader.Boolean()) return reader.Buffer();
        int prefix = reader.Int32(), suffix = reader.Int32();
        var header = reader.BufferView();
        var middle = reader.BufferView();
        if (header.Length != HeaderLength || previous.Length < HeaderLength || prefix < 0 || suffix < 0
            || prefix > previous.Length - HeaderLength || suffix > previous.Length - HeaderLength - prefix
            || middle.Length > RetentionLimit - HeaderLength - prefix - suffix)
            throw new InvalidDataException("Invalid UI scene delta ranges.");
        if (previousSequence == 0 || BinaryPrimitives.ReadInt64LittleEndian(header.Span[^(2 * sizeof(long))..]) != previousSequence)
            throw new InvalidDataException("Missing UI scene delta baseline.");
        var frame = new byte[HeaderLength + prefix + middle.Length + suffix];
        header.Span.CopyTo(frame);
        previous.Span.Slice(HeaderLength, prefix).CopyTo(frame.AsSpan(HeaderLength));
        middle.Span.CopyTo(frame.AsSpan(HeaderLength + prefix));
        previous.Span[^suffix..].CopyTo(frame.AsSpan(frame.Length - suffix));
        return frame;
    }
}
