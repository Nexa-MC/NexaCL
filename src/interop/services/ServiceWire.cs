using System.Text;
using Nexa.Core.Media;

namespace Nexa.Native.Services.Wire;

internal static class ServiceWireLimits
{
    internal const int Frame = 68 * 1024 * 1024, String = 32 * 1024, Collection = 16384, Depth = 64;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
}

internal abstract class ServiceWireBudget
{
    private int _depth;
    internal DepthScope EnterValue()
    {
        if (_depth >= ServiceWireLimits.Depth) throw new InvalidDataException("Service nesting budget exceeded.");
        _depth++; return new(this);
    }
    internal readonly struct DepthScope(ServiceWireBudget owner) : IDisposable
    { public void Dispose() => owner._depth--; }
}

internal sealed partial class ServiceWireWriter : ServiceWireBudget, IDisposable
{
    private readonly MemoryStream _stream = new();
    private readonly BinaryWriter _writer;
    internal ServiceWireWriter() { _writer = new(_stream, ServiceWireLimits.Utf8, leaveOpen: true); }
    private void Reserve(int bytes)
    { if (bytes < 0 || bytes > ServiceWireLimits.Frame - _stream.Length) throw new InvalidDataException("Service frame budget exceeded."); }
    internal void Boolean(bool value) { Reserve(1); _writer.Write(value); }
    internal void Byte(byte value) { Reserve(1); _writer.Write(value); }
    internal void SByte(sbyte value) { Reserve(1); _writer.Write(value); }
    internal void Int16(short value) { Reserve(2); _writer.Write(value); }
    internal void UInt16(ushort value) { Reserve(2); _writer.Write(value); }
    internal void Int32(int value) { Reserve(4); _writer.Write(value); }
    internal void UInt32(uint value) { Reserve(4); _writer.Write(value); }
    internal void Int64(long value) { Reserve(8); _writer.Write(value); }
    internal void UInt64(ulong value) { Reserve(8); _writer.Write(value); }
    internal void Single(float value) { Reserve(4); _writer.Write(value); }
    internal void Double(double value) { Reserve(8); _writer.Write(value); }
    internal void Char(char value) => UInt16(value);
    internal void Count(int value)
    { if (value is < 0 or > ServiceWireLimits.Collection) throw new InvalidDataException("Service collection budget exceeded."); Int32(value); }
    internal void String(string? value)
    {
        if (value is null) { Int32(-1); return; }
        int count = ServiceWireLimits.Utf8.GetByteCount(value);
        if (count > ServiceWireLimits.String) throw new InvalidDataException("Service string budget exceeded.");
        Reserve(checked(4 + count)); Int32(count); _writer.Write(ServiceWireLimits.Utf8.GetBytes(value));
    }
    internal void Buffer(ReadOnlyMemory<byte> bytes)
    { Reserve(checked(4 + bytes.Length)); Int32(bytes.Length); _writer.Write(bytes.Span); }
    internal void Array_Byte(byte[]? bytes) { if (bytes is null) Int32(-1); else Buffer(bytes); }
    internal void ReadOnlyMemory_Byte(ReadOnlyMemory<byte> value) => Buffer(value);
    internal void Guid(global::System.Guid value) => Buffer(value.ToByteArray());
    internal void TimeSpan(global::System.TimeSpan value) => Int64(value.Ticks);
    internal void DateTimeOffset(global::System.DateTimeOffset value) { Int64(value.Ticks); Int32((int)value.Offset.TotalMinutes); }
    internal void Uri(global::System.Uri? value) => String(value?.AbsoluteUri);
    internal void XsrSemanticId(Nexa.Xsr.XsrSemanticId value) => String(value.Value);
    internal void PngImage(PngImage? value) { Boolean(value is not null); if (value is not null) Buffer(value.Bytes); }
    internal byte[] Finish() => _stream.ToArray();
    public void Dispose() { _writer.Dispose(); _stream.Dispose(); }
}

internal sealed partial class ServiceWireReader : ServiceWireBudget, IDisposable
{
    private readonly MemoryStream _stream;
    private readonly BinaryReader _reader;
    internal ServiceWireReader(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > ServiceWireLimits.Frame) throw new InvalidDataException("Service frame budget exceeded.");
        _stream = new(bytes.ToArray(), writable: false); _reader = new(_stream, ServiceWireLimits.Utf8, leaveOpen: true);
    }
    internal bool Boolean() => Byte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Invalid service boolean.") };
    internal byte Byte() => _reader.ReadByte();
    internal sbyte SByte() => _reader.ReadSByte();
    internal short Int16() => _reader.ReadInt16();
    internal ushort UInt16() => _reader.ReadUInt16();
    internal int Int32() => _reader.ReadInt32();
    internal uint UInt32() => _reader.ReadUInt32();
    internal long Int64() => _reader.ReadInt64();
    internal ulong UInt64() => _reader.ReadUInt64();
    internal float Single() => _reader.ReadSingle();
    internal double Double() => _reader.ReadDouble();
    internal char Char() => (char)UInt16();
    internal int Count()
    { int value = Int32(); return value is >= -1 and <= ServiceWireLimits.Collection ? value : throw new InvalidDataException("Service collection budget exceeded."); }
    private byte[] Bytes(int count, int maximum)
    {
        if (count < 0 || count > maximum || count > _stream.Length - _stream.Position) throw new InvalidDataException("Invalid service byte length.");
        return _reader.ReadBytes(count);
    }
    internal string? String() { int count = Int32(); return count == -1 ? null : ServiceWireLimits.Utf8.GetString(Bytes(count, ServiceWireLimits.String)); }
    internal byte[] Buffer() => Bytes(Int32(), ServiceWireLimits.Frame);
    internal byte[]? Array_Byte() { int count = Int32(); return count == -1 ? null : Bytes(count, ServiceWireLimits.Frame); }
    internal ReadOnlyMemory<byte> ReadOnlyMemory_Byte() => Buffer();
    internal global::System.Guid Guid()
    { byte[] bytes = Buffer(); return bytes.Length == 16 ? new(bytes) : throw new InvalidDataException("Invalid service GUID."); }
    internal global::System.TimeSpan TimeSpan() => global::System.TimeSpan.FromTicks(Int64());
    internal global::System.DateTimeOffset DateTimeOffset()
    {
        long ticks = Int64(); int minutes = Int32();
        if (minutes is < -840 or > 840) throw new InvalidDataException("Invalid service time offset.");
        try { return new(ticks, global::System.TimeSpan.FromMinutes(minutes)); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid service time.", error); }
    }
    internal global::System.Uri? Uri()
    {
        string? value = String();
        if (value is null) return null;
        return global::System.Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : throw new InvalidDataException("Invalid service URI.");
    }
    internal Nexa.Xsr.XsrSemanticId XsrSemanticId() => Nexa.Xsr.XsrSemanticId.Parse(String() ?? throw new InvalidDataException("Missing service semantic ID."));
    internal PngImage? PngImage() => !Boolean() ? null : Nexa.Core.Media.PngImage.TryCreatePreview(Buffer()) ?? throw new InvalidDataException("Invalid service PNG.");
    internal void End() { if (_stream.Position != _stream.Length) throw new InvalidDataException("Trailing service bytes."); }
    public void Dispose() { _reader.Dispose(); _stream.Dispose(); }
}
