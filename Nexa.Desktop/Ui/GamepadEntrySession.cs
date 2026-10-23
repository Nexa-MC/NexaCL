using System.Globalization;
using Avalonia.Threading;
using Nexa.Core.Media;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>Controller entry presentation; the final brand lockup owns no product navigation.</summary>
internal sealed class GamepadEntrySession : IDisposable
{
    private readonly XsrUiShell _shell;
    private readonly XsrStateStore _store;
    private readonly XsrStateId _animationFrameRate;
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
    private readonly HashSet<string> _neutralDevices = [];
    private readonly Dictionary<(GamepadLayout Layout, int Group), PngImage> _sheets = [];
    private XsrUiEntityId _root, _layer, _previousFocus;
    private string? _projection;
    private string? _device;
    private string? _feedbackDevice;
    private GamepadLayout _layout;
    private long _shownAt, _enteredAt, _heldAt = -1, _gestureStartedAt = -1;
    private long _lastInputAt = -1;
    private double _holdProgress;
    private bool _armed, _dismissRequested, _disposed;
    private int _lastFrame = -1;
    private PngImage? _logo, _flash, _horizon;
    private XsrUiSize? _presentationViewport;
    internal const int IntroDuration = 5800;
    internal const int HoldMilliseconds = 1000, VibrationRampMilliseconds = 800;
    private const double HorizonFocus = 118d / 1024; // Diagonal edge of the supplied plate outline.
    internal bool IsPromptOpen => _root.IsAssigned && !IsEntered;
    internal bool IsEntered { get; private set; }
    internal bool IsIntroComplete { get; private set; }
    internal XsrUiEntityId Root => _root;
    internal TimeSpan PresentationInterval => TimeSpan.FromSeconds(1d / (_animationFrameRate.IsAssigned
        ? Math.Clamp((long)_store.Read<int>(_animationFrameRate).Value + 1, 1, 240) : 60));

    internal GamepadEntrySession(XsrUiShell shell, DesktopUiIntentSink intents, XsrStateStore store,
        AvaloniaUiPlatformActions platform, IGamepadInput? input = null, bool runTimer = true,
        Func<bool>? foreground = null, Action<bool>? immersive = null)
    {
        _shell = shell; _store = store; _intents = intents; _input = input;
        store.TryResolve(XsrSemanticId.Parse("UiAniFPS"), out _animationFrameRate);
        _dynamic = new(shell.Tree, store);
        _immersive = immersive ?? platform.SetImmersivePresentation;
        _scrim = platform.SetClientScrimOpacity; _observe = platform.ReportControllerInput;
        _foreground = foreground ?? (() => platform.IsWindowActive);
        intents.IntentEmitted += OnIntent;
        if (runTimer)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Input, (_, _) =>
            {
                if (_disposed) return;
                _input ??= new NativeGamepadInput(); // Native message windows belong to the GUI thread.
                Step(_input.Read(), Environment.TickCount64, _foreground());
                _timer!.Interval = IsEntered && !IsIntroComplete ? PresentationInterval
                    : TimeSpan.FromMilliseconds(20);
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
        if (_gestureStartedAt >= 0 && _lastInputAt >= 0 && now - _lastInputAt > NativeGamepadFeedback.LeaseMilliseconds)
        {
            ResetGesture(); _armed = false; _neutralDevices.Clear();
        }
        _lastInputAt = now;
        _suppressed.RemoveWhere(id => !samples.Any(sample => sample.Id == id));
        _neutralDevices.RemoveWhere(id => !samples.Any(sample => sample.Id == id));
        if (_device is not null && !samples.Any(sample => sample.Id == _device)) Close();
        if (_dismissRequested) { foreach (var sample in samples) _suppressed.Add(sample.Id); Close(); _dismissRequested = false; return; }
        if (!foreground)
        {
            ResetGesture(); _armed = false; _neutralDevices.Clear(); UpdatePresentation(now); return;
        }
        foreach (var sample in samples) if (sample.Neutral) _neutralDevices.Add(sample.Id);
        if (_device is null)
        {
            if (_shell.Stage.Overlays.Any(overlay => _shell.Tree.GetComponent<XsrUiOverlayLayer>(overlay)?.IsModal == true)) return;
            var candidate = samples.FirstOrDefault(sample => !_suppressed.Contains(sample.Id) && sample.HasActivity && !sample.Cancel);
            if (candidate is null) return;
            Show(candidate, now);
        }
        GamepadSample current = samples.First(sample => sample.Id == _device);
        if (current.Cancel) { foreach (var sample in samples) _suppressed.Add(sample.Id); Close(); return; }
        if (!IsEntered)
        {
            if (!current.Valid) { _armed = false; _neutralDevices.Remove(current.Id); }
            if (current.Neutral) _armed = true;
            bool attempting = _armed && current.Valid && current.LeftX >= -.25 && current.RightX <= .25
                && (current.LeftX > .25 || current.RightX < -.25) && Math.Abs(current.LeftY) <= .45 && Math.Abs(current.RightY) <= .45;
            if (!attempting) ResetGesture();
            else
            {
                if (_gestureStartedAt < 0) _gestureStartedAt = now;
                bool inward = current.LeftX >= .65 && current.RightX <= -.65;
                if (!inward) { _heldAt = -1; _holdProgress = 0; }
                else
                {
                    if (_heldAt < 0) _heldAt = now;
                    _holdProgress = Math.Clamp((now - _heldAt) / (double)HoldMilliseconds, 0, 1);
                }
                double completion = Math.Clamp(Math.Min(current.LeftX, -current.RightX) / .65, 0, 1);
                float vibration = (float)(.12 + .88 * Math.Clamp((now - _gestureStartedAt) / (double)VibrationRampMilliseconds, 0, 1));
                if (_input is IGamepadFeedback feedback)
                {
                    feedback.ApplyFeedback(current.Id, new(vibration, (float)(completion * completion)));
                    _feedbackDevice = current.Id;
                }
                if (_holdProgress >= 1)
                {
                    StopFeedback(); // Stop the physical actuators before any page/window motion.
                    _gestureStartedAt = -1;
                    IsEntered = true; _enteredAt = now; _observe(); _immersive(true);
                    _shell.Renderer.Focus(default);
                }
            }
        }
        UpdatePresentation(now);
    }

    private void Show(GamepadSample sample, long now)
    {
        _device = sample.Id; _layout = sample.Layout; _shownAt = now;
        _heldAt = _gestureStartedAt = -1; _holdProgress = 0; _armed = _neutralDevices.Contains(sample.Id); _lastFrame = -1;
        _previousFocus = _shell.Renderer.Focused;
        _layer = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("GamepadEntry.nxml"), _shell.Tree, _store);
        _root = _dynamic.Append(_layer, Workspace(new("#00000000")));
        _shell.Tree.Walk(_root, entity => { _nodes[_shell.Tree.Name(entity)] = entity; return true; });
        _shell.Stage.Show(_layer, modal: true);
        _shell.Renderer.Focus(_nodes["GamepadDismiss"], showIndicator: false);
    }

    internal void UpdatePresentation(long now)
    {
        if (!_root.IsAssigned) return;
        var viewport = _shell.Renderer.Viewport;
        if (IsIntroComplete && _presentationViewport == viewport) return;
        bool reduced = _shell.Renderer.EffectiveReducedMotion;
        double age = IsIntroComplete ? IntroDuration : IsEntered ? now - _enteredAt : now - _shownAt;
        if (!IsEntered)
        {
            double fade = reduced ? 1 : .15 + .85 * Smooth(age / 140);
            Apply(new("#000000" + ((byte)(184 * fade)).ToString("X2", CultureInfo.InvariantCulture), Prompt: fade));
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
            double blackout = reduced ? 1 : Smooth(age / 500);
            _scrim(.72 + .28 * blackout);
            if (reduced) age = IntroDuration;
            double zoom = Smoother((age - 2100) / 1800);
            double reveal = Smoother((zoom - .25) / .5);
            double flash = Smooth((age - 1080) / 520) * (1 - Smoother((zoom - .12) / .32));
            double name = Smoother((age - 4330) / 600);
            double caption = Smoother((age - 5150) / 600);
            Apply(new("#000000" + ((byte)(184 + 71 * blackout)).ToString("X2", CultureInfo.InvariantCulture),
                Prompt: reduced ? 0 : 1 - Smooth(age / 180), Intro: true,
                Logo: reveal, Zoom: zoom,
                Slide: Smoother((age - 4250) / 650), Lift: caption,
                Name: name, Caption: caption, Flash: reduced ? 0 : flash,
                FlashSize: 1 + .6 * Smooth((age - 1100) / 900),
                Horizon: age >= 1000 ? 1 - reveal : 0,
                Veil: age >= 1000 ? 1 - Smooth((age - 1000) / 850) : 0,
                SunRise: Smooth((age - 1080) / 850)));
            IsIntroComplete = age >= IntroDuration;
        }
    }

    private readonly record struct Presentation(string Background, double Prompt = 0, bool Intro = false,
        double Logo = 0, double Zoom = 0, double Slide = 0, double Lift = 0,
        double Name = 0, double Caption = 0, double Flash = 0, double FlashSize = 1,
        double Horizon = 0, double Veil = 0, double SunRise = 0);

    private static string Number(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
    private static string Visible(bool value) => value ? "true" : "false";
    private static string Shift(double x, double y) => $"{Number(x)},{Number(y)},{Number(-x)},{Number(-y)}";

    private NxmlHostIr Workspace(Presentation frame, double logoSize = 168, double logoX = 0, double logoY = 0, double flashSize = 512)
        => DesktopMarkup.Dynamic("GamepadEntry.d.nxml", new Dictionary<string, string>
        {
            ["background"] = frame.Background,
            ["prompt_visible"] = Visible(!frame.Intro || frame.Prompt > 0),
            ["prompt_opacity"] = Number(frame.Prompt),
            ["holding"] = Visible(_heldAt >= 0),
            ["hold_progress"] = Number(_holdProgress),
            ["intro_visible"] = Visible(frame.Intro),
            ["logo_visible"] = Visible(frame.Logo >= .001),
            ["logo_opacity"] = Number(frame.Logo),
            ["logo_size"] = Number(logoSize),
            ["logo_margin"] = ImagePlacement(logoSize, logoX, logoY),
            ["flash_visible"] = Visible(frame.Flash >= .001),
            ["flash_opacity"] = Number(frame.Flash),
            ["flash_size"] = Number(flashSize),
            ["flash_margin"] = ImagePlacement(flashSize, 14 - 26 * frame.SunRise, 14 - 26 * frame.SunRise),
            ["horizon_visible"] = Visible(frame.Horizon >= .001),
            ["horizon_opacity"] = Number(frame.Horizon),
            ["veil_visible"] = Visible(frame.Veil >= .001),
            ["veil_opacity"] = Number(frame.Veil),
            ["name_visible"] = Visible(frame.Name >= .001),
            ["name_opacity"] = Number(frame.Name),
            ["name_margin"] = Shift(96 + 32 * (1 - frame.Name), -18 * frame.Lift),
            ["caption_visible"] = Visible(frame.Caption >= .001),
            ["caption_opacity"] = Number(frame.Caption),
            ["caption_margin"] = Shift(96, 70 - 14 * frame.Caption),
        });

    private static string ImagePlacement(double size, double x, double y)
        => $"{Number(-size + x)},{Number(-size + y)},{Number(-size - x)},{Number(-size - y)}";

    private void Apply(Presentation frame)
    {
        var viewport = _shell.Renderer.Viewport;
        double canvas = Math.Max(1, Math.Min(viewport.Width, viewport.Height));
        // A large carrier uses the same bounded source bitmap and the existing smooth
        // complete-image sampler. Negative margins let NXML place it beyond the viewport;
        // native clipping reveals the outer plate arc, like a planet seen from space.
        double size = canvas * 10 * Math.Pow(168 / canvas / 10, frame.Zoom);
        // Keep the bright arc stable while the camera pulls back. Recenter in screen
        // space late in the shot, without coupling a large pan to the shrinking size.
        double recenter = Smoother((frame.Zoom - .35) / .65);
        double cameraOffset = (.5 - HorizonFocus) * (size - 168 * recenter);
        double x = cameraOffset - 162 * frame.Slide;
        double y = cameraOffset - 18 * frame.Lift;
        double flashSize = frame.FlashSize * canvas;
        string signature = string.Create(CultureInfo.InvariantCulture,
            $"{frame.Background}|{frame.Prompt:F3}|{frame.Intro}|{frame.Logo:F3}|{frame.Flash:F3}|{frame.Horizon:F3}|{frame.Veil:F3}|{frame.SunRise:F3}|{frame.Name:F3}|{frame.Caption:F3}|{frame.Lift:F3}|{size:F3}|{x:F3}|{y:F3}|{flashSize:F3}|{_heldAt >= 0}|{_holdProgress:F3}");
        if (signature != _projection)
        {
            _dynamic.Reload(_root, Workspace(frame, size, x, y, flashSize));
            _projection = signature;
        }
        if (!frame.Intro) return;
        _presentationViewport = viewport;
        if (_shell.Tree.GetComponent<XsrUiImage>(_nodes["GamepadLogo"])!.Raster is null)
        {
            _shell.Tree.GetComponent<XsrUiImage>(_nodes["GamepadLogo"])!.Raster = new(Logo(), []) { FitToBounds = true };
            _shell.Tree.GetComponent<XsrUiImage>(_nodes["GamepadFlash"])!.Raster = new(_flash ??= Texture("gamepad-entry-flash.png"), []) { FitToBounds = true };
            _shell.Tree.GetComponent<XsrUiImage>(_nodes["GamepadHorizon"])!.Raster = new(_horizon ??= Texture("gamepad-entry-horizon.png"), []) { FitToBounds = true };
            _shell.Tree.MarkDirty(_root, XsrUiDirtyKinds.Paint);
        }
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

    private PngImage Logo() => _logo ??= Texture("neon-n-entry.png");

    private static PngImage Texture(string name)
    {
        using Stream stream = typeof(GamepadEntrySession).Assembly.GetManifestResourceStream("Nexa.Desktop.Assets.Brand." + name)!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        return PngImage.TryCreatePreview(buffer.ToArray()) ?? throw new InvalidDataException("Invalid entry texture.");
    }
    private static double Smooth(double progress) { progress = Math.Clamp(progress, 0, 1); return progress * progress * (3 - 2 * progress); }
    private static double Smoother(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return progress * progress * progress * (progress * (progress * 6 - 15) + 10);
    }

    internal void Close()
    {
        ResetGesture();
        if (!_root.IsAssigned) return;
        if (IsEntered) _immersive(false);
        _scrim(0);
        _shell.Stage.Dismiss(_layer); _dynamic.Clear(_layer); _shell.Tree.Destroy(_layer);
        _shell.Renderer.Focus(_shell.Tree.IsAlive(_previousFocus) ? _previousFocus : default);
        _root = _layer = _previousFocus = default; _device = _projection = null; _nodes.Clear();
        _presentationViewport = null;
        IsEntered = IsIntroComplete = false; _armed = false; _heldAt = -1;
    }

    private void StopFeedback()
    {
        string? device = _feedbackDevice; _feedbackDevice = null;
        if (device is not null && _input is IGamepadFeedback feedback) feedback.ApplyFeedback(device, default);
    }
    private void ResetGesture()
    {
        StopFeedback(); _gestureStartedAt = _heldAt = -1; _holdProgress = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer?.Stop(); _intents.IntentEmitted -= OnIntent;
        Close(); _input?.Dispose(); _sheets.Clear();
    }
}
