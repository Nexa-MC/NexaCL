using System.Globalization;
using Avalonia.Threading;
using Nexa.Core.Media;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>Controller entry only; an opaque empty canvas deliberately owns no product navigation.</summary>
internal sealed class GamepadEntrySession : IDisposable
{
    private readonly XsrUiShell _shell;
    private readonly XsrStateStore _store;
    private readonly DesktopUiIntentSink _intents;
    private readonly NxmlDynamicContent _dynamic;
    private readonly Action<bool> _immersive;
    private readonly Action<double> _scrim;
    private readonly Action _observe;
    private readonly Func<bool> _foreground;
    private readonly DispatcherTimer? _timer;
    private IGamepadInput? _input;
    private readonly Dictionary<string, XsrUiEntityId> _nodes = [];
    private readonly HashSet<string> _suppressed = [];
    private readonly Dictionary<(GamepadLayout Layout, int Group), PngImage> _sheets = [];
    private XsrUiEntityId _root, _layer, _previousFocus;
    private string? _projection;
    private string? _device;
    private GamepadLayout _layout;
    private long _shownAt, _enteredAt, _heldAt = -1;
    private bool _armed, _dismissRequested, _disposed;
    private int _lastFrame = -1;
    private PngImage? _logo;
    internal bool IsPromptOpen => _root.IsAssigned && !IsEntered;
    internal bool IsEntered { get; private set; }
    internal bool IsIntroComplete { get; private set; }
    internal XsrUiEntityId Root => _root;

    internal GamepadEntrySession(XsrUiShell shell, DesktopUiIntentSink intents, XsrStateStore store,
        AvaloniaUiPlatformActions platform, IGamepadInput? input = null, bool runTimer = true,
        Func<bool>? foreground = null, Action<bool>? immersive = null)
    {
        _shell = shell; _store = store; _intents = intents; _input = input;
        _dynamic = new(shell.Tree, store);
        _immersive = immersive ?? platform.SetImmersivePresentation;
        _scrim = platform.SetClientScrimOpacity; _observe = platform.ReportControllerInput;
        _foreground = foreground ?? (() => platform.IsWindowActive);
        intents.IntentEmitted += OnIntent;
        if (runTimer)
        {
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Input, (_, _) =>
            {
                if (_disposed) return;
                _input ??= new NativeGamepadInput(); // Native message windows belong to the GUI thread.
                Step(_input.Read(), Environment.TickCount64, _foreground());
                _timer!.Interval = TimeSpan.FromMilliseconds(_root.IsAssigned ? 20 : 1000);
            });
            Dispatcher.UIThread.Post(() => { if (!_disposed) _timer.Start(); });
        }
    }

    private void OnIntent(object? sender, DesktopUiIntentEventArgs args)
    {
        if (args.Intent.Command.Value == "ui.gamepad.dismiss" && _root.IsAssigned
            && _nodes.ContainsValue(args.Intent.Source)) _dismissRequested = true;
    }

    internal void Step(IReadOnlyList<GamepadSample> samples, long now, bool foreground = true)
    {
        if (_disposed) return;
        _suppressed.RemoveWhere(id => !samples.Any(sample => sample.Id == id));
        if (_device is not null && !samples.Any(sample => sample.Id == _device)) Close();
        if (_dismissRequested) { foreach (var sample in samples) _suppressed.Add(sample.Id); Close(); _dismissRequested = false; return; }
        if (!foreground) { _armed = false; _heldAt = -1; return; }
        if (_device is null)
        {
            if (_shell.Stage.Overlays.Any(overlay => _shell.Tree.GetComponent<XsrUiOverlayLayer>(overlay)?.IsModal == true)) return;
            var candidate = samples.FirstOrDefault(sample => !_suppressed.Contains(sample.Id));
            if (candidate is null) return;
            Show(candidate, now);
        }
        GamepadSample current = samples.First(sample => sample.Id == _device);
        if (current.Cancel) { foreach (var sample in samples) _suppressed.Add(sample.Id); Close(); return; }
        bool neutral = current.Ready && Math.Abs(current.LeftX) < .25 && Math.Abs(current.RightX) < .25
            && Math.Abs(current.LeftY) < .25 && Math.Abs(current.RightY) < .25;
        if (!IsEntered)
        {
            if (neutral) _armed = true;
            bool inward = _armed && current.Ready && current.LeftX >= .65 && current.RightX <= -.65
                && Math.Abs(current.LeftY) <= .45 && Math.Abs(current.RightY) <= .45;
            if (!inward) _heldAt = -1;
            else if (_heldAt < 0) _heldAt = now;
            else if (now - _heldAt >= 200)
            {
                IsEntered = true; _enteredAt = now; _observe(); _immersive(true);
                _shell.Renderer.Focus(default);
                _shell.Tree.GetComponent<XsrUiImage>(_nodes["GamepadLogo"])!.Raster = new(Logo(), []) { FitToBounds = true };
            }
        }
        UpdatePresentation(now);
    }

    private void Show(GamepadSample sample, long now)
    {
        _device = sample.Id; _layout = sample.Layout; _shownAt = now;
        _heldAt = -1; _armed = false; _lastFrame = -1;
        _previousFocus = _shell.Renderer.Focused;
        _layer = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("GamepadEntry.nxml"), _shell.Tree, _store);
        _root = _dynamic.Append(_layer, Workspace("#00000000", true, 0, false, 0, 160));
        _shell.Tree.Walk(_root, entity => { _nodes[_shell.Tree.Name(entity)] = entity; return true; });
        _shell.Stage.Show(_layer, modal: true);
        _shell.Renderer.Focus(_nodes["GamepadDismiss"], showIndicator: false);
    }

    internal void UpdatePresentation(long now)
    {
        if (!_root.IsAssigned || IsIntroComplete) return;
        bool reduced = _shell.Renderer.EffectiveReducedMotion;
        double age = IsEntered ? now - _enteredAt : now - _shownAt;
        if (!IsEntered)
        {
            double fade = reduced ? 1 : Smooth(age / 220);
            Apply("#000000" + ((byte)(184 * fade)).ToString("X2", CultureInfo.InvariantCulture), true, fade, false, 0, 160);
            _scrim(.72 * fade);
            int frame = reduced ? 24 : (int)(Math.Max(0, age) % 2400 / 50);
            if (_lastFrame != frame)
            {
                _lastFrame = frame;
                _shell.Tree.GetComponent<XsrUiImage>(_nodes["GamepadDrawing"])!.Raster = Artwork(_layout, frame);
                _shell.Tree.MarkDirty(_root, XsrUiDirtyKinds.Paint);
            }
        }
        else
        {
            double progress = reduced ? 1 : Smooth(age / 600);
            _scrim(.72 + .28 * progress);
            double reveal = reduced ? 0 : Smooth((age - 180) / 420) * (1 - Smooth((age - 1300) / 500));
            double size = 132 + 36 * Smooth((age - 180) / 600);
            string background = string.Create(CultureInfo.InvariantCulture, $"#{(byte)(11 * progress):X2}{(byte)(14 * progress):X2}{(byte)(22 * progress):X2}{(byte)(184 + 71 * progress):X2}");
            Apply(background, !reduced && age < 200, reduced ? 0 : 1 - Smooth(age / 180), !reduced && age < 1800, reveal, size);
            if (reduced || age >= 1800)
            {
                IsIntroComplete = true;
                // Actual large-screen content intentionally remains empty after the entry animation.
            }
        }
    }

    private static NxmlHostIr Workspace(string background, bool promptVisible, double promptOpacity, bool introVisible, double introOpacity, double logoSize)
        => DesktopMarkup.Dynamic("GamepadEntry.d.nxml", new Dictionary<string, string>
        {
            ["background"] = background,
            ["prompt_visible"] = promptVisible ? "true" : "false",
            ["prompt_opacity"] = promptOpacity.ToString("F3", CultureInfo.InvariantCulture),
            ["intro_visible"] = introVisible ? "true" : "false",
            ["intro_opacity"] = introOpacity.ToString("F3", CultureInfo.InvariantCulture),
            ["logo_size"] = logoSize.ToString("F3", CultureInfo.InvariantCulture),
        });

    private void Apply(string background, bool promptVisible, double promptOpacity, bool introVisible, double introOpacity, double logoSize)
    {
        string signature = string.Create(CultureInfo.InvariantCulture, $"{background}|{promptVisible}|{promptOpacity:F3}|{introVisible}|{introOpacity:F3}|{logoSize:F3}");
        if (signature == _projection) return;
        _dynamic.Reload(_root, Workspace(background, promptVisible, promptOpacity, introVisible, introOpacity, logoSize));
        _projection = signature;
    }

    internal XsrUiRasterImage Artwork(GamepadLayout layout, int frame)
    {
        frame = Math.Clamp(frame, 0, 47);
        var key = (layout, frame / 6);
        if (!_sheets.TryGetValue(key, out var sheet))
        {
            string file = layout switch { GamepadLayout.SymmetricLow => "01-symmetric-low", GamepadLayout.SymmetricHigh => "03-symmetric-high", _ => "02-offset-left-high" };
            using Stream stream = typeof(GamepadEntrySession).Assembly.GetManifestResourceStream("Nexa.Desktop.Assets.Gamepads." + file + "." + key.Item2.ToString("D2", CultureInfo.InvariantCulture) + ".png")!;
            using var buffer = new MemoryStream(); stream.CopyTo(buffer);
            sheet = PngImage.TryCreatePreview(buffer.ToArray()) ?? throw new InvalidDataException("Invalid controller artwork.");
            _sheets.Add(key, sheet);
        }
        frame %= 6;
        return new(sheet, [new(new(frame % 3 * 500, frame / 3 * 360, 500, 360), new(0, 0, 1, 1))]) { AspectRatio = 500d / 360 };
    }

    private PngImage Logo()
    {
        if (_logo is not null) return _logo;
        using Stream stream = typeof(GamepadEntrySession).Assembly.GetManifestResourceStream("Nexa.Desktop.Assets.Brand.neon-n-dark.png")!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        return _logo = PngImage.TryCreatePreview(buffer.ToArray()) ?? throw new InvalidDataException("Invalid entry icon.");
    }
    private static double Smooth(double progress) { progress = Math.Clamp(progress, 0, 1); return progress * progress * (3 - 2 * progress); }

    internal void Close()
    {
        if (!_root.IsAssigned) return;
        if (IsEntered) _immersive(false);
        _scrim(0);
        _shell.Stage.Dismiss(_layer); _dynamic.Clear(_layer); _shell.Tree.Destroy(_layer);
        _shell.Renderer.Focus(_shell.Tree.IsAlive(_previousFocus) ? _previousFocus : default);
        _root = _layer = _previousFocus = default; _device = _projection = null; _nodes.Clear();
        IsEntered = IsIntroComplete = false; _armed = false; _heldAt = -1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer?.Stop(); _intents.IntentEmitted -= OnIntent;
        Close(); _input?.Dispose(); _sheets.Clear();
    }
}
