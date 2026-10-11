using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.State;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

/// <summary>Finite host-owned navigation intents projected into a modal renderer overlay.</summary>
internal sealed class DesktopCommandPalette : IDisposable
{
    private static readonly XsrSemanticId Execute = XsrSemanticId.Parse("ui.command-palette.execute");
    private static readonly XsrSemanticId Dismiss = XsrSemanticId.Parse("ui.command-palette.dismiss");
    private readonly XsrUiShell _shell;
    private readonly XsrStateStore _store;
    private readonly NxmlDynamicContent _dynamic;
    private XsrUiEntityId _layer, _rowSlot;
    private readonly DesktopUiIntentSink _intents;
    private readonly AvaloniaUiPlatformActions _platform;
    private readonly Action<DesktopCommandRoute> _navigate;
    private readonly Dictionary<XsrUiEntityId, DesktopCommandRoute> _rows = [];
    private readonly Func<bool>? _previousOpen, _previousClose;
    private readonly Func<bool> _openCallback, _closeCallback;
    private XsrUiEntityId _root, _search, _close, _empty, _previousFocus, _page;
    private string? _filterSignature;
    private bool _previousFocusVisible, _disposed;

    internal DesktopCommandPalette(XsrUiShell shell, DesktopUiIntentSink intents, XsrStateStore store,
        AvaloniaUiPlatformActions platform, Action<DesktopCommandRoute> navigate)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dynamic = new(shell.Tree, store);
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        _previousOpen = platform.CommandPaletteOpenRequested; _previousClose = platform.CommandPaletteCloseRequested;
        _openCallback = Open; _closeCallback = Close;
        platform.CommandPaletteOpenRequested = _openCallback; platform.CommandPaletteCloseRequested = _closeCallback;
        shell.Renderer.FramePreparing += OnFrame;
        intents.IntentEmitted += OnIntent;
    }

    internal bool IsOpen => _root.IsAssigned;

    internal XsrUiEntityId Find(string name)
    {
        XsrUiEntityId result = default;
        if (IsOpen) _shell.Tree.Walk(_root, entity => { if (_shell.Tree.Name(entity) == name) result = entity; return true; });
        return result;
    }

    internal bool Open()
    {
        if (_disposed) return false;
        if (IsOpen) return true;
        if (_shell.Stage.Overlays.Any(entity => _shell.Tree.GetComponent<XsrUiOverlayLayer>(entity)?.IsModal == true)) return false;
        _page = _shell.Stage.Navigation.Current;
        _previousFocus = _shell.Renderer.Focused;
        _previousFocusVisible = _shell.Tree.IsAlive(_previousFocus)
            && _shell.Tree.GetComponent<XsrUiInput>(_previousFocus)?.IsFocusVisible == true;
        _layer = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("CommandPalette.nxml"), _shell.Tree, _store);
        _root = _dynamic.Append(_layer, Workspace(empty:false));
        _search = Find("CommandPaletteSearch"); _close = Find("CommandPaletteClose");
        _empty = Find("CommandPaletteEmpty"); _rowSlot = Find("CommandPaletteRows");
        foreach (var route in DesktopCommandLine.Commands)
            _rows[_dynamic.Append(_rowSlot, Row(route, visible:true))] = route;
        _filterSignature = null;
        _shell.Stage.Show(_layer, modal: true);
        Filter(); _shell.Renderer.Focus(_search, showIndicator: false);
        return true;
    }

    internal bool Close()
    {
        if (!IsOpen) return false;
        _shell.Stage.Dismiss(_layer); _dynamic.Clear(_layer); _shell.Tree.Destroy(_layer);
        _layer = _rowSlot = default;
        _root = _search = _close = _empty = default; _rows.Clear(); _filterSignature = null;
        if (_shell.Tree.IsAlive(_previousFocus)) _shell.Renderer.Focus(_previousFocus, _previousFocusVisible);
        else _shell.Renderer.Focus(default);
        _previousFocus = default;
        return true;
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        if (_disposed || !IsOpen) return;
        if (_page != _shell.Stage.Navigation.Current) { Close(); return; }
        Filter();
    }

    private void Filter()
    {
        string query = _shell.Tree.GetComponent<XsrUiTextInput>(_search)!.ReadDraft().Trim();
        string signature = query + "\n" + string.Join('\n', DesktopCommandLine.Commands.Select(route => _shell.Renderer.LocalizeText(route.Label)));
        if (signature == _filterSignature) return;
        _filterSignature = signature;
        int count = 0;
        foreach (var (entity, route) in _rows)
        {
            bool visible = Matches(route, query);
            _dynamic.Reload(entity, Row(route, visible));
            if (visible) count++;
        }
        _dynamic.Reload(_root, Workspace(empty:count == 0));
    }

    private bool Matches(DesktopCommandRoute route, string query) => route.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
        || _shell.Renderer.LocalizeText(route.Label).Contains(query, StringComparison.OrdinalIgnoreCase);

    private void OnIntent(object? sender, DesktopUiIntentEventArgs args)
    {
        if (_disposed || !IsOpen || _page != _shell.Stage.Navigation.Current || !_shell.Tree.IsAlive(args.Intent.Source)) return;
        if (args.Intent.Command == Dismiss && (args.Intent.Source == _close || args.Intent.Source == _root)) { Close(); return; }
        if (args.Intent.Command != Execute || !_rows.TryGetValue(args.Intent.Source, out var route)
            || !_shell.Tree.GetComponent<XsrUiElement>(args.Intent.Source)!.IsVisible
            || _shell.Tree.GetComponent<XsrUiInput>(args.Intent.Source)?.Enabled != true
            || !Matches(route, _shell.Tree.GetComponent<XsrUiTextInput>(_search)!.ReadDraft().Trim())) return;
        Close(); _navigate(route);
    }

    private static NxmlHostIr Workspace(bool empty) => DesktopMarkup.Dynamic("CommandPaletteWorkspace.d.nxml",
        new Dictionary<string,string> { ["empty"] = empty ? "true" : "false" });
    private static NxmlHostIr Row(DesktopCommandRoute route, bool visible) => DesktopMarkup.Dynamic("CommandPaletteRow.d.nxml",
        new Dictionary<string,string>
        { ["key"] = "CommandPaletteCommand." + route.Id, ["label"] = route.Label, ["visible"] = visible ? "true" : "false" });

    public void Dispose()
    {
        if (_disposed) return;
        Close(); _disposed = true;
        _shell.Renderer.FramePreparing -= OnFrame; _intents.IntentEmitted -= OnIntent;
        if (_platform.CommandPaletteOpenRequested == _openCallback) _platform.CommandPaletteOpenRequested = _previousOpen;
        if (_platform.CommandPaletteCloseRequested == _closeCallback) _platform.CommandPaletteCloseRequested = _previousClose;
    }
}
