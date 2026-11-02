using Nexa.Core.Media;

namespace Nexa.Xsr.Renderer.Wire;

/// <summary>Sender-owned scene stream. Accept a packet only after the receiver acknowledges it.</summary>
public sealed class XsrUiSceneSender : IDisposable
{
    private readonly object _gate = new();
    private Dictionary<string, PngImage> _images = new(StringComparer.Ordinal);
    private XsrUiScenePacket? _pending;
    private long _next, _acknowledged;
    private bool _disposed;

    public XsrUiScenePacket Prepare(XsrUiScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.Outgoing.Count > 256) throw new InvalidDataException("Too many outgoing UI layers.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending is not null) throw new InvalidOperationException("A UI scene packet is already pending.");
            long sequence = checked(++_next);
            using var writer = new UiWireWriter(_images);
            writer.Int32(0x3155534e); writer.String(UiWireSchema.Fingerprint);
            writer.Int64(sequence); writer.Int64(_acknowledged); writer.Int64(scene.Version);
            writer.IReadOnlyList_XsrUiSceneNode(scene.Nodes);
            writer.Count(scene.Outgoing.Count);
            foreach (var layer in scene.Outgoing) writer.XsrUiOutgoingLayer(layer);
            var images = writer.Images;
            _pending = new(writer.Finish(), sequence, accepted =>
            {
                lock (_gate)
                {
                    if (accepted && !_disposed) { _images = images; _acknowledged = sequence; }
                    else { _images = new(StringComparer.Ordinal); _acknowledged = 0; }
                    _pending = null;
                }
            });
            return _pending;
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending?.Dispose();
            _pending = null;
            _images.Clear();
        }
    }

}

/// <summary>One pending binary frame. Rejection or abandonment forces a full image resynchronization.</summary>
public sealed class XsrUiScenePacket : IDisposable
{
    private Action<bool>? _finish;
    internal XsrUiScenePacket(byte[] bytes, long sequence, Action<bool> finish)
    { Bytes = bytes; Sequence = sequence; _finish = finish; }
    public ReadOnlyMemory<byte> Bytes { get; }
    public long Sequence { get; }
    public void Acknowledge(long sequence)
    {
        if (sequence != Sequence) throw new InvalidDataException("UI scene acknowledgement does not match its packet.");
        Interlocked.Exchange(ref _finish, null)?.Invoke(true);
    }
    public void Dispose() => Interlocked.Exchange(ref _finish, null)?.Invoke(false);
}

/// <summary>Receiver-owned image table and monotonic scene admission; failed packets leave both unchanged.</summary>
public sealed class XsrUiSceneReceiver
{
    private readonly object _gate = new();
    private Dictionary<string, PngImage> _images = new(StringComparer.Ordinal);
    private long _sequence;
    private XsrUiScene? _scene;
    public long Sequence { get { lock (_gate) return _sequence; } }
    public XsrUiScene? Scene { get { lock (_gate) return _scene; } }
    public int RetainedImageCount { get { lock (_gate) return _images.Count; } }

    public XsrUiScene Accept(ReadOnlyMemory<byte> packet)
    {
        if (packet.Length > UiWireLimits.Frame) throw new InvalidDataException("UI frame byte budget exceeded.");
        byte[] bytes = packet.ToArray();
        lock (_gate)
        {
            try
            {
                // Inspect the fixed envelope before deciding whether previous image references are valid.
                using var header = new UiWireReader(bytes, _images);
                if (header.Int32() != 0x3155534e || header.String() != UiWireSchema.Fingerprint)
                    throw new InvalidDataException("UI scene schema mismatch.");
                long sequence = header.Int64(), baseline = header.Int64();
                if (sequence <= Sequence || baseline != 0 && baseline != Sequence)
                    throw new InvalidDataException("Stale UI scene or missing baseline.");
                using var reader = new UiWireReader(bytes, baseline == 0 ? new Dictionary<string, PngImage>(StringComparer.Ordinal) : _images);
                _ = reader.Int32(); _ = reader.String(); _ = reader.Int64(); _ = reader.Int64();
                long version = reader.Int64();
                var nodes = reader.IReadOnlyList_XsrUiSceneNode() ?? throw new InvalidDataException("Missing UI nodes.");
                int count = reader.Count();
                if (count is < 0 or > 256) throw new InvalidDataException("Invalid outgoing UI layer count.");
                var outgoing = new XsrUiOutgoingLayer[count];
                for (int i = 0; i < count; i++) outgoing[i] = reader.XsrUiOutgoingLayer() ?? throw new InvalidDataException("Missing outgoing UI layer.");
                reader.End();
                var scene = new XsrUiScene(version, nodes.ToArray(), outgoing);
                _images = reader.Images; _sequence = sequence; _scene = scene;
                return scene;
            }
            catch (EndOfStreamException error) { throw new InvalidDataException("Truncated UI scene.", error); }
            catch (System.Text.DecoderFallbackException error) { throw new InvalidDataException("Invalid UI UTF-8.", error); }
        }
    }
}
