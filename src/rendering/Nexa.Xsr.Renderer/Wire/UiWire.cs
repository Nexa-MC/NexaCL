using System.Buffers.Binary;
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
    private byte[] _buffer;
    private readonly IReadOnlyDictionary<string, PngImage> _previous;
    private int _bytes, _values, _imageBytes;
    internal Dictionary<string, PngImage> Images { get; } = new(StringComparer.Ordinal);
    internal UiWireWriter(IReadOnlyDictionary<string, PngImage> previous, int initialCapacity = 0)
    {
        _previous = previous;
        if ((uint)initialCapacity > UiWireLimits.Frame) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _buffer = initialCapacity == 0 ? [] : new byte[initialCapacity];
    }

    private Span<byte> Reserve(int count)
    {
        if ((uint)count > (uint)(_buffer.Length - _bytes))
        {
            if (count < 0 || count > UiWireLimits.Frame - _bytes) throw new InvalidDataException("UI frame byte budget exceeded.");
            int capacity = Math.Min(UiWireLimits.Frame, Math.Max(_bytes + count, Math.Max(256, _buffer.Length * 2)));
            Array.Resize(ref _buffer, capacity);
        }
        var slice = _buffer.AsSpan(_bytes, count);
        _bytes += count;
        return slice;
    }
    internal void Boolean(bool value) => Byte(value ? (byte)1 : (byte)0);
    internal void Byte(byte value) => Reserve(1)[0] = value;
    internal void Int32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(sizeof(int)), value);
    internal void UInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(sizeof(uint)), value);
    internal void Int64(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(sizeof(long)), value);
    internal void Double(double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("Non-finite UI geometry.");
        BinaryPrimitives.WriteDoubleLittleEndian(Reserve(sizeof(double)), value);
    }
    internal void LayoutWidth(double value)
    {
        if (double.IsNaN(value) || value < 0) throw new InvalidDataException("Invalid UI text width.");
        BinaryPrimitives.WriteDoubleLittleEndian(Reserve(sizeof(double)), value);
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
        Int32(length);
        if (length != 0) UiWireLimits.Utf8.GetBytes(value, Reserve(length));
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
            Int32(value.Bytes.Length); value.Bytes.Span.CopyTo(Reserve(value.Bytes.Length));
        }
        if (Images.TryAdd(value.Key, value))
        {
            if (value.Bytes.Length > UiWireLimits.Images - _imageBytes) throw new InvalidDataException("UI image set budget exceeded.");
            _imageBytes += value.Bytes.Length;
        }
    }
    internal void Buffer(ReadOnlyMemory<byte> bytes)
    {
        Int32(bytes.Length); bytes.Span.CopyTo(Reserve(bytes.Length));
    }
    internal void ReadOnlyMemory_Byte(ReadOnlyMemory<byte> bytes) => Buffer(bytes);
    internal byte[] Finish() => _buffer.AsSpan(0, _bytes).ToArray();
    public void Dispose() { _buffer = []; _bytes = 0; }
}

internal sealed partial class UiWireReader : IDisposable
{
    private readonly byte[] _packet;
    private int _position;
    private readonly IReadOnlyDictionary<string, PngImage> _previous;
    private int _values, _imageBytes;
    internal Dictionary<string, PngImage> Images { get; } = new(StringComparer.Ordinal);
    internal UiWireReader(byte[] bytes, IReadOnlyDictionary<string, PngImage> previous)
    {
        if (bytes.Length > UiWireLimits.Frame) throw new InvalidDataException("UI frame byte budget exceeded.");
        _previous = previous; _packet = bytes;
    }
    internal bool Boolean() => Byte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Noncanonical UI boolean.") };
    private ReadOnlySpan<byte> Scalar(int length)
    {
        if (length > _packet.Length - _position) throw new EndOfStreamException();
        var value = _packet.AsSpan(_position, length);
        _position += length;
        return value;
    }
    internal byte Byte() => Scalar(1)[0];
    internal int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Scalar(sizeof(int)));
    internal uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Scalar(sizeof(uint)));
    internal long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Scalar(sizeof(long)));
    internal double Double()
    {
        double value = BinaryPrimitives.ReadDoubleLittleEndian(Scalar(sizeof(double)));
        return double.IsFinite(value) ? value : throw new InvalidDataException("Non-finite UI geometry.");
    }
    internal double LayoutWidth()
    {
        double value = BinaryPrimitives.ReadDoubleLittleEndian(Scalar(sizeof(double)));
        return !double.IsNaN(value) && value >= 0 ? value : throw new InvalidDataException("Invalid UI text width.");
    }
    internal int Count()
    {
        int count = Int32();
        if (count < -1 || count > UiWireLimits.Collection || count > UiWireLimits.Values - _values || count > _packet.Length - _position)
            throw new InvalidDataException("UI collection budget exceeded.");
        if (count >= 0) _values += count;
        return count;
    }
    internal string? String()
    {
        int length = Int32();
        if (length == -1) return null;
        return UiWireLimits.Utf8.GetString(Take(length, UiWireLimits.String).Span);
    }
    private ReadOnlyMemory<byte> Take(int length, int limit)
    {
        if (length < 0 || length > limit || length > _packet.Length - _position)
            throw new InvalidDataException("Invalid UI buffer length.");
        var slice = _packet.AsMemory(_position, length);
        _position += length;
        return slice;
    }
    internal ReadOnlyMemory<byte> BufferView() => Take(Int32(), UiWireLimits.Frame);
    internal byte[] Buffer() => BufferView().ToArray();
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
            byte[] bytes = Take(Int32(), 16 * 1024 * 1024).ToArray();
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
        if (_position != _packet.Length) throw new InvalidDataException("Trailing UI frame bytes.");
    }
    public void Dispose() { }
}
