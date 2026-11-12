namespace Nexa.Sidecar.Protocol;

public static partial class SidecarDataMessages
{
    private static void WriteValue(SidecarPayloadWriter writer, ushort firstField, SidecarBinaryValue value)
    {
        int count = Math.Min(value.Length, SidecarWireCodecs.MaximumValueLength);
        writer.WriteBytes(firstField, value.Span[..count]);
        if (count == value.Length) return;
        writer.WriteUInt32(6, checked((uint)value.Length));
        ushort field = 7;
        while (count < value.Length)
        {
            int length = Math.Min(value.Length - count, SidecarWireCodecs.MaximumValueLength);
            writer.WriteBytes(field++, value.Span.Slice(count, length)); count += length;
        }
    }

    private sealed class ValueChunks(int payloadLength)
    {
        private byte[]? _value;
        private int _offset;
        private ushort _next = 7;
        private const int LastChunkField = 6 + (SidecarWireCodecs.MaximumGeneratedDtoLength - 1) / SidecarWireCodecs.MaximumValueLength;
        internal void Begin(uint total, uint? codec, byte[]? first)
        {
            if (_value is not null || codec != SidecarWireCodecs.GeneratedDto
                || total <= SidecarWireCodecs.MaximumValueLength || total > SidecarWireCodecs.MaximumGeneratedDtoLength
                || total > payloadLength || first?.Length != SidecarWireCodecs.MaximumValueLength)
                throw new SidecarProtocolException("Invalid large DTO declaration.");
            _value = new byte[checked((int)total)];
            first.CopyTo(_value, 0); _offset = first.Length;
        }
        internal void Add(SidecarPayloadField field)
        {
            // Preserve unknown-field behavior when no large-value extension is declared.
            if (_value is null || field.Id < 7 || field.Id > LastChunkField) return;
            if (field.Id != _next++) throw new SidecarProtocolException("Missing large DTO continuation.");
            var bytes = field.ReadBytes();
            int required = Math.Min(_value.Length - _offset, SidecarWireCodecs.MaximumValueLength);
            if (required == 0 || bytes.Length != required) throw new SidecarProtocolException("Invalid large DTO continuation length.");
            bytes.CopyTo(_value, _offset); _offset += bytes.Length;
        }
        internal byte[]? Finish(byte[]? first)
        {
            if (_value is null) return first;
            if (_offset != _value.Length) throw new SidecarProtocolException("Truncated large DTO.");
            return _value;
        }
    }
}
