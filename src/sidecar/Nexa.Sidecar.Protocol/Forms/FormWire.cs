using System.Text;

namespace Nexa.Sidecar.Protocol.Forms.Wire;

// Primitive operations used by the generated form codec. No Host or renderer dependency.
internal abstract class ServiceWireBudget
{
    private int _depth;
    internal DepthScope EnterValue()
    {
        if (_depth >= 8) throw new InvalidDataException("Form nesting budget exceeded.");
        _depth++; return new(this);
    }
    internal readonly struct DepthScope(ServiceWireBudget owner) : IDisposable
    { public void Dispose() => owner._depth--; }
}

internal sealed partial class ServiceWireWriter : ServiceWireBudget, IDisposable
{
    private readonly MemoryStream _stream = new();
    private readonly BinaryWriter _writer;
    internal ServiceWireWriter() => _writer = new(_stream, SidecarUiForms.Utf8, leaveOpen: true);
    private void Reserve(int bytes)
    { if (bytes < 0 || bytes > SidecarUiForms.MaximumEncodedLength - _stream.Length) throw new InvalidDataException("Form byte budget exceeded."); }
    internal void Boolean(bool value) { Reserve(1); _writer.Write(value); }
    internal void Int32(int value) { Reserve(4); _writer.Write(value); }
    internal void Count(int value)
    { if (value is < 0 or > SidecarUiForms.MaximumFields) throw new InvalidDataException("Form field budget exceeded."); Int32(value); }
    internal void String(string? value)
    {
        if (value is null) { Int32(-1); return; }
        int bytes = SidecarUiForms.Utf8.GetByteCount(value);
        if (bytes > 32768) throw new InvalidDataException("Form string budget exceeded.");
        Reserve(checked(4 + bytes)); Int32(bytes); _writer.Write(SidecarUiForms.Utf8.GetBytes(value));
    }
    internal void Array_Byte(byte[]? value)
    {
        if (value is null) { Int32(-1); return; }
        if (value.Length > SidecarUiForms.MaximumValueLength) throw new InvalidDataException("Form value budget exceeded.");
        Reserve(checked(4 + value.Length)); Int32(value.Length); _writer.Write(value);
    }
    internal byte[] Finish() => _stream.ToArray();
    public void Dispose() { _writer.Dispose(); _stream.Dispose(); }
}

internal sealed partial class ServiceWireReader : ServiceWireBudget, IDisposable
{
    private readonly MemoryStream _stream;
    private readonly BinaryReader _reader;
    internal ServiceWireReader(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > SidecarUiForms.MaximumEncodedLength) throw new InvalidDataException("Form byte budget exceeded.");
        _stream = new(bytes.ToArray(), writable: false);
        _reader = new(_stream, SidecarUiForms.Utf8, leaveOpen: true);
    }
    internal bool Boolean() => _reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Invalid form boolean.") };
    internal int Int32() => _reader.ReadInt32();
    internal int Count()
    { int value = Int32(); return value is >= -1 and <= SidecarUiForms.MaximumFields ? value : throw new InvalidDataException("Form field budget exceeded."); }
    private byte[] Bytes(int count, int maximum)
    {
        if (count < 0 || count > maximum || count > _stream.Length - _stream.Position) throw new InvalidDataException("Invalid form byte length.");
        return _reader.ReadBytes(count);
    }
    internal string? String() { int count = Int32(); return count == -1 ? null : SidecarUiForms.Utf8.GetString(Bytes(count, 32768)); }
    internal byte[]? Array_Byte() { int count = Int32(); return count == -1 ? null : Bytes(count, SidecarUiForms.MaximumValueLength); }
    internal void End() { if (_stream.Position != _stream.Length) throw new InvalidDataException("Trailing form bytes."); }
    public void Dispose() { _reader.Dispose(); _stream.Dispose(); }
}
