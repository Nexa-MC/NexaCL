using System.Text;
using Nexa.Core.Media;

#if NEXA_WINDOW_WIRE
using Nexa.Xsr;
namespace Nexa.Native.Window.Wire;
#else
namespace Nexa.Xsr.Renderer.Wire;
#endif

internal static class UiWireLimits
{
#if NEXA_WINDOW_WIRE
    internal const int Frame = 72 * 1024 * 1024;
#else
    internal const int Frame = 64 * 1024 * 1024;
#endif
    internal const int String = 1024 * 1024;
    internal const int Collection = 65536;
    internal const int Values = 1_000_000;
    internal const int Images = 32 * 1024 * 1024;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
}

internal sealed partial class UiWireWriter : IDisposable
{
    private readonly MemoryStream _stream = new();
    private readonly BinaryWriter _writer;
    private readonly IReadOnlyDictionary<string, PngImage> _previous;
    private int _bytes, _values, _imageBytes;
    internal Dictionary<string, PngImage> Images { get; } = new(StringComparer.Ordinal);
    internal UiWireWriter(IReadOnlyDictionary<string, PngImage> previous)
    { _previous = previous; _writer = new(_stream, UiWireLimits.Utf8, leaveOpen: true); }
    private void Reserve(int count)
    {
        if (count < 0 || count > UiWireLimits.Frame - _bytes) throw new InvalidDataException("UI frame byte budget exceeded.");
        _bytes += count;
#if NEXA_WINDOW_WIRE
        // The 72 MiB control limit must not round MemoryStream capacity up to 128 MiB.
        if (_bytes > _stream.Capacity)
            _stream.Capacity = Math.Min(UiWireLimits.Frame, Math.Max(_bytes, Math.Max(256, _stream.Capacity * 2)));
#endif
    }
    internal void Boolean(bool value) { Reserve(1); _writer.Write(value); }
    internal void Byte(byte value) { Reserve(1); _writer.Write(value); }
    internal void Int32(int value) { Reserve(4); _writer.Write(value); }
    internal void UInt32(uint value) { Reserve(4); _writer.Write(value); }
    internal void Int64(long value) { Reserve(8); _writer.Write(value); }
    internal void Double(double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("Non-finite UI geometry.");
        Reserve(8); _writer.Write(value);
    }
    internal void LayoutWidth(double value)
    {
        if (double.IsNaN(value) || value < 0) throw new InvalidDataException("Invalid UI text width.");
        Reserve(8); _writer.Write(value);
    }
    internal void Count(int count)
    {
        if (count < 0 || count > UiWireLimits.Collection || count > UiWireLimits.Values - _values)
            throw new InvalidDataException("UI collection budget exceeded.");
        _values += count; Int32(count);
    }
    internal void String(string? value)
    {
        if (value is null) { Int32(-1); return; }
        int length = UiWireLimits.Utf8.GetByteCount(value);
        if (length > UiWireLimits.String) throw new InvalidDataException("UI string byte budget exceeded.");
        Int32(length); Reserve(length); _writer.Write(UiWireLimits.Utf8.GetBytes(value));
    }
    internal void XsrSemanticId(XsrSemanticId value) => String(value.IsAssigned ? value.Value : null);
    internal void PngImage(PngImage? value)
    {
        Boolean(value is not null); if (value is null) return;
        String(value.Key);
        bool fresh = !Images.ContainsKey(value.Key) && !_previous.ContainsKey(value.Key);
        Boolean(fresh);
        if (fresh)
        {
            if (value.Bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("UI image byte budget exceeded.");
            Int32(value.Bytes.Length); Reserve(value.Bytes.Length); _writer.Write(value.Bytes.Span);
        }
        if (Images.TryAdd(value.Key, value))
        {
            if (value.Bytes.Length > UiWireLimits.Images - _imageBytes) throw new InvalidDataException("UI image set budget exceeded.");
            _imageBytes += value.Bytes.Length;
        }
    }
    internal void Buffer(ReadOnlyMemory<byte> bytes)
    {
        Int32(bytes.Length); Reserve(bytes.Length); _writer.Write(bytes.Span);
    }
    internal void ReadOnlyMemory_Byte(ReadOnlyMemory<byte> bytes) => Buffer(bytes);
    internal byte[] Finish() { _writer.Flush(); return _stream.ToArray(); }
    public void Dispose() { _writer.Dispose(); _stream.Dispose(); }
}

internal sealed partial class UiWireReader : IDisposable
{
    private readonly MemoryStream _stream;
    private readonly BinaryReader _reader;
    private readonly IReadOnlyDictionary<string, PngImage> _previous;
    private int _values, _imageBytes;
    internal Dictionary<string, PngImage> Images { get; } = new(StringComparer.Ordinal);
    internal UiWireReader(byte[] bytes, IReadOnlyDictionary<string, PngImage> previous)
    {
        if (bytes.Length > UiWireLimits.Frame) throw new InvalidDataException("UI frame byte budget exceeded.");
        _previous = previous; _stream = new(bytes, writable: false); _reader = new(_stream, UiWireLimits.Utf8);
    }
    internal bool Boolean() => Byte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Noncanonical UI boolean.") };
    internal byte Byte() => _reader.ReadByte();
    internal int Int32() => _reader.ReadInt32();
    internal uint UInt32() => _reader.ReadUInt32();
    internal long Int64() => _reader.ReadInt64();
    internal double Double()
    {
        double value = _reader.ReadDouble();
        return double.IsFinite(value) ? value : throw new InvalidDataException("Non-finite UI geometry.");
    }
    internal double LayoutWidth()
    {
        double value = _reader.ReadDouble();
        return !double.IsNaN(value) && value >= 0 ? value : throw new InvalidDataException("Invalid UI text width.");
    }
    internal int Count()
    {
        int count = Int32();
        if (count < -1 || count > UiWireLimits.Collection || count > UiWireLimits.Values - _values || count > _stream.Length - _stream.Position)
            throw new InvalidDataException("UI collection budget exceeded.");
        if (count >= 0) _values += count;
        return count;
    }
    internal string? String()
    {
        int length = Int32();
        if (length == -1) return null;
        return UiWireLimits.Utf8.GetString(Bytes(length, UiWireLimits.String));
    }
    private byte[] Bytes(int length, int limit)
    {
        if (length < 0 || length > limit || length > _stream.Length - _stream.Position)
            throw new InvalidDataException("Invalid UI buffer length.");
        byte[] bytes = _reader.ReadBytes(length);
        if (bytes.Length != length) throw new InvalidDataException("Truncated UI buffer.");
        return bytes;
    }
    internal byte[] Buffer() => Bytes(Int32(), UiWireLimits.Frame);
    internal ReadOnlyMemory<byte> ReadOnlyMemory_Byte() => Buffer();
    internal XsrSemanticId XsrSemanticId()
    {
        string? text = String();
        if (text is null) return default;
        return global::Nexa.Xsr.XsrSemanticId.TryParse(text, out var value) ? value : throw new InvalidDataException("Invalid UI semantic ID.");
    }
    internal PngImage? PngImage()
    {
        if (!Boolean()) return null;
        string key = String() ?? throw new InvalidDataException("Missing UI image key.");
        if (key.Length != 64 || !key.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid UI image key.");
        PngImage image;
        if (Boolean())
        {
            if (Images.ContainsKey(key) || _previous.ContainsKey(key))
                throw new InvalidDataException("Duplicate UI image definition.");
            byte[] bytes = Bytes(Int32(), 16 * 1024 * 1024);
            image = global::Nexa.Core.Media.PngImage.TryCreatePreview(bytes) ?? global::Nexa.Core.Media.PngImage.TryCreateResourceIcon(bytes)
                ?? throw new InvalidDataException("Invalid UI encoded image.");
            if (image.Key != key) throw new InvalidDataException("UI image content hash mismatch.");
        }
        else if (!Images.TryGetValue(key, out image!) && !_previous.TryGetValue(key, out image!))
            throw new InvalidDataException("Unknown UI image reference.");
        if (Images.TryAdd(key, image))
        {
            if (image.Bytes.Length > UiWireLimits.Images - _imageBytes) throw new InvalidDataException("UI image set budget exceeded.");
            _imageBytes += image.Bytes.Length;
        }
        return image;
    }
    internal void End()
    {
        if (_stream.Position != _stream.Length) throw new InvalidDataException("Trailing UI frame bytes.");
    }
    public void Dispose() => _reader.Dispose();
}
