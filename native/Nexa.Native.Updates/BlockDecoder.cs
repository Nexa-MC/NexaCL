using System.IO.Compression;
using System.Threading.Channels;

namespace Nexa.Native.Updates;

public static class BlockDecoder
{
    public static Stream Open(Stream input, int codec, bool leaveOpen) => codec switch
    {
        1 => new GZipStream(input, CompressionMode.Decompress, leaveOpen),
        2 => new ZstdSharp.DecompressionStream(input, leaveOpen: leaveOpen),
        _ => throw new InvalidDataException("Unsupported block codec.")
    };
}

// Only owned packets enter these channels. Capacity one bounds memory even if
// the caller stops consuming. The wake callback never reads the caller's stream.
internal sealed class DecoderSession : Stream
{
    internal const int PacketSize = 65536;
    private readonly Channel<byte[]> _input = Channel.CreateBounded<byte[]>(1);
    private readonly Channel<byte[]> _output = Channel.CreateBounded<byte[]>(1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Action _wake;
    private byte[]? _inputPacket;
    private int _inputOffset;
    private byte[]? _outputPacket;
    private int _outputOffset;
    private bool _needsInput;
    private bool _ended;
    private bool _eof;
    private Exception? _failure;
    private readonly object _gate = new();

    internal DecoderSession(int codec, Action wake)
    {
        _wake = wake;
        _ = RunAsync(codec);
    }

    private async Task RunAsync(int codec)
    {
        try
        {
            using Stream decoder = BlockDecoder.Open(this, codec, true);
            while (true)
            {
                byte[] packet = new byte[PacketSize];
                int count = await decoder.ReadAsync(packet, _stop.Token).ConfigureAwait(false);
                if (count == 0) break;
                if (count != packet.Length) Array.Resize(ref packet, count);
                await _output.Writer.WriteAsync(packet, _stop.Token).ConfigureAwait(false);
                _wake();
            }
        }
        catch (Exception e) { lock (_gate) _failure = e; }
        finally
        {
            lock (_gate) _ended = true;
            _output.Writer.TryComplete();
            _wake();
        }
    }

    internal void Stop() => _stop.Cancel();

    internal void Feed(ReadOnlySpan<byte> input)
    {
        lock (_gate)
        {
            if (!_needsInput) throw new InvalidOperationException("Input was not requested.");
            _needsInput = false;
            if (input.IsEmpty) { _eof = true; _input.Writer.TryComplete(); }
            else if (!_input.Writer.TryWrite(input.ToArray())) throw new InvalidOperationException("Input queue full.");
        }
    }

    // >=0: bytes/EOF; -2: input needed; -3: await wake; -4: codec error.
    internal int Poll(Span<byte> output, out string? error)
    {
        lock (_gate)
        {
            error = null;
            if (_outputPacket is null && _output.Reader.TryRead(out byte[]? next))
            { _outputPacket = next; _outputOffset = 0; }
            if (_outputPacket is not null)
            {
                int count = Math.Min(output.Length, _outputPacket.Length - _outputOffset);
                _outputPacket.AsSpan(_outputOffset, count).CopyTo(output);
                _outputOffset += count;
                if (_outputOffset == _outputPacket.Length) _outputPacket = null;
                return count;
            }
            if (_ended)
            {
                error = _failure?.Message;
                return _failure is null ? 0 : -4;
            }
            return _needsInput ? -2 : -3;
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty) return 0;
        if (_inputPacket is null)
        {
            lock (_gate) { if (_eof) return 0; _needsInput = true; }
            _wake();
            if (!await _input.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) return 0;
            if (!_input.Reader.TryRead(out _inputPacket)) throw new IOException("Input packet unavailable.");
            _inputOffset = 0;
        }
        int count = Math.Min(buffer.Length, _inputPacket.Length - _inputOffset);
        _inputPacket.AsMemory(_inputOffset, count).CopyTo(buffer);
        _inputOffset += count;
        if (_inputOffset == _inputPacket.Length) _inputPacket = null;
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count), _stop.Token).AsTask().GetAwaiter().GetResult();
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
