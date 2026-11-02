using Nexa.Core.Media;

namespace Nexa.Xsr.Renderer.Wire;

[Flags]
public enum XsrUiShellInvalidation { None = 0, Render = 1, Navigation = 2 }

/// <summary>Window-owned shell view. Its transport targets the Host-owned tree on the same UI thread.</summary>
public sealed class XsrUiRemoteShell : IXsrUiBackendShell
{
    private readonly IXsrUiWireTransport _transport;
    private readonly XsrUiSceneReceiver _scenes = new();
    private XsrUiWindowFacts _facts;
    public XsrUiRemoteShell(IXsrUiWireTransport transport, IXsrUiBackendRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(transport); ArgumentNullException.ThrowIfNull(renderer);
        _transport = transport; Renderer = renderer; _facts = Refresh();
    }
    public string Title => _facts.Title;
    public string Version => _facts.Version;
    public XsrUiEntityId Root => _facts.Root;
    public XsrUiEntityId CurrentPage => _facts.CurrentPage;
    public bool HasContentTransition => _facts.HasContentTransition;
    public XsrSemanticId SelectedNavigationId => _facts.SelectedNavigationId;
    public XsrUiShellPalette Palette => _facts.Palette;
    public bool IsNavigationExpanded => _facts.IsNavigationExpanded;
    public double RailPresentationProgress => _facts.RailPresentationProgress;
    public IXsrUiBackendRenderer Renderer { get; }
    public event EventHandler? RenderRequested;
    public event EventHandler? NavigationExpandedChanged;

    // The native dispatcher delivers/coalesces these notifications. Do not call this on a worker thread.
    public void NotifyInvalidated(XsrUiShellInvalidation invalidation)
    {
        if ((invalidation & ~(XsrUiShellInvalidation.Render | XsrUiShellInvalidation.Navigation)) != 0)
            throw new InvalidDataException("Invalid UI shell notification.");
        if (invalidation == XsrUiShellInvalidation.None) return;
        _facts = Refresh();
        if ((invalidation & XsrUiShellInvalidation.Navigation) != 0) NavigationExpandedChanged?.Invoke(this, EventArgs.Empty);
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    public XsrUiScene Render(XsrUiSize viewport)
    {
        using var request = XsrUiShellChannel.Request(1);
        request.XsrUiSize(viewport);
        using var response = XsrUiShellChannel.Response(_transport.Exchange(request.Finish()));
        var facts = XsrUiShellChannel.ReadFacts(response);
        byte[] frame = response.Buffer(); response.End();
        var scene = _scenes.Accept(frame);
        using var acknowledgement = XsrUiShellChannel.Request(2);
        acknowledgement.Int64(_scenes.Sequence);
        using var accepted = XsrUiShellChannel.Response(_transport.Exchange(acknowledgement.Finish()));
        accepted.End();
        _facts = facts;
        return scene;
    }
    public void SetRailPresentationProgress(double value)
    {
        using var request = XsrUiShellChannel.Request(3); request.Double(value);
        _facts = ExchangeFacts(request);
    }
    public void PublishWindowActivity(bool active, bool minimized)
    {
        using var request = XsrUiShellChannel.Request(4); request.Boolean(active); request.Boolean(minimized);
        _facts = ExchangeFacts(request);
    }
    public void PublishWindowMetrics(double trafficLightPadding, bool fullscreen, double contentInset = 0)
    {
        using var request = XsrUiShellChannel.Request(5);
        request.Double(trafficLightPadding); request.Boolean(fullscreen); request.Double(contentInset);
        _facts = ExchangeFacts(request);
    }
    private XsrUiWindowFacts Refresh()
    {
        using var request = XsrUiShellChannel.Request(0); return ExchangeFacts(request);
    }
    private XsrUiWindowFacts ExchangeFacts(UiWireWriter request)
    {
        using var response = XsrUiShellChannel.Response(_transport.Exchange(request.Finish()));
        var facts = XsrUiShellChannel.ReadFacts(response); response.End(); return facts;
    }
}

/// <summary>Host-owned shell admission. Owner-thread calls only; retire the native call gate before disposal.</summary>
public sealed class XsrUiShellEndpoint : IXsrUiWireTransport, IDisposable
{
    private readonly IXsrUiBackendShell _shell;
    private readonly XsrUiSceneSender _scenes = new();
    private XsrUiScenePacket? _pending;
    private bool _disposed, _rendering;
    // Subscribers enqueue a native notification; they must not render or synchronously wait here.
    public event Action<XsrUiShellInvalidation>? Invalidated;
    public XsrUiShellEndpoint(IXsrUiBackendShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell); _shell = shell;
        shell.RenderRequested += OnRender; shell.NavigationExpandedChanged += OnNavigation;
    }
    private void OnRender(object? sender, EventArgs args) => Invalidated?.Invoke(XsrUiShellInvalidation.Render);
    private void OnNavigation(object? sender, EventArgs args) => Invalidated?.Invoke(XsrUiShellInvalidation.Navigation);

    public byte[] Exchange(ReadOnlyMemory<byte> request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.Length > UiWireLimits.Frame) throw new InvalidDataException("UI shell request exceeds its budget.");
        using var reader = XsrUiShellChannel.Response(request.ToArray());
        using var writer = XsrUiShellChannel.Envelope();
        switch (reader.Int32())
        {
            case 0:
                reader.End(); XsrUiShellChannel.WriteFacts(writer, _shell); break;
            case 1:
                var viewport = reader.XsrUiSize(); reader.End();
                if (_rendering) throw new InvalidOperationException("Reentrant UI shell rendering is not supported.");
                _rendering = true;
                try
                {
                    // An abandoned acknowledgement forces a fresh image baseline on the next render.
                    _pending?.Dispose(); _pending = null;
                    var scene = _shell.Render(viewport);
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _pending = _scenes.Prepare(scene);
                    XsrUiShellChannel.WriteFacts(writer, _shell);
                    writer.Buffer(_pending.Bytes);
                }
                catch { _pending?.Dispose(); _pending = null; throw; }
                finally { _rendering = false; }
                break;
            case 2:
                long sequence = reader.Int64(); reader.End();
                (_pending ?? throw new InvalidDataException("No pending UI shell frame.")).Acknowledge(sequence);
                _pending.Dispose(); _pending = null;
                break;
            case 3:
                double progress = reader.Double(); reader.End();
                _shell.SetRailPresentationProgress(progress); XsrUiShellChannel.WriteFacts(writer, _shell); break;
            case 4:
                bool active = reader.Boolean(), minimized = reader.Boolean(); reader.End();
                _shell.PublishWindowActivity(active, minimized); XsrUiShellChannel.WriteFacts(writer, _shell); break;
            case 5:
                double padding = reader.Double(); bool fullscreen = reader.Boolean(); double inset = reader.Double(); reader.End();
                _shell.PublishWindowMetrics(padding, fullscreen, inset); XsrUiShellChannel.WriteFacts(writer, _shell); break;
            default: throw new InvalidDataException("Unknown UI shell operation.");
        }
        return writer.Finish();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shell.RenderRequested -= OnRender; _shell.NavigationExpandedChanged -= OnNavigation;
        Invalidated = null;
        _pending?.Dispose(); _pending = null; _scenes.Dispose();
    }
}

internal sealed record XsrUiWindowFacts(string Title, string Version, XsrUiEntityId Root, XsrUiEntityId CurrentPage,
    bool HasContentTransition, XsrSemanticId SelectedNavigationId, XsrUiShellPalette Palette,
    bool IsNavigationExpanded, double RailPresentationProgress);

// Fixed shell envelope v1. Scene and nested DTO changes also require the generated fingerprint.
internal static class XsrUiShellChannel
{
    private static readonly IReadOnlyDictionary<string, PngImage> EmptyImages = new Dictionary<string, PngImage>();
    internal static UiWireWriter Envelope()
    {
        var writer = new UiWireWriter(EmptyImages);
        writer.Int32(0x3157554e); writer.String(UiWireSchema.Fingerprint); return writer;
    }
    internal static UiWireWriter Request(int operation)
    { var writer = Envelope(); writer.Int32(operation); return writer; }
    internal static UiWireReader Response(byte[] bytes)
    {
        var reader = new UiWireReader(bytes, EmptyImages);
        try
        {
            if (reader.Int32() != 0x3157554e || reader.String() != UiWireSchema.Fingerprint)
                throw new InvalidDataException("UI shell schema mismatch.");
            return reader;
        }
        catch { reader.Dispose(); throw; }
    }
    internal static void WriteFacts(UiWireWriter writer, IXsrUiBackendShell shell)
    {
        writer.String(shell.Title); writer.String(shell.Version);
        writer.XsrUiEntityId(shell.Root); writer.XsrUiEntityId(shell.CurrentPage);
        writer.Boolean(shell.HasContentTransition); writer.XsrSemanticId(shell.SelectedNavigationId);
        writer.XsrUiShellPalette(shell.Palette); writer.Boolean(shell.IsNavigationExpanded); writer.Double(shell.RailPresentationProgress);
    }
    internal static XsrUiWindowFacts ReadFacts(UiWireReader reader) => new(
        reader.String() ?? throw new InvalidDataException("Missing UI title."),
        reader.String() ?? throw new InvalidDataException("Missing UI version."),
        reader.XsrUiEntityId(), reader.XsrUiEntityId(), reader.Boolean(), reader.XsrSemanticId(),
        reader.XsrUiShellPalette(), reader.Boolean(), reader.Double());
}
