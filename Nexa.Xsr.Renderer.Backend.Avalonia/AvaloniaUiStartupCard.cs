using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>Bounded native startup decoration using the shared presentation clock.</summary>
internal sealed class AvaloniaUiStartupCard : Border, IDisposable
{
    internal const double WindowWidth = 480;
    internal const double LoadingHeight = 272;
    internal const double FailureHeight = 384;
    internal const double PassiveHeight = 230;

    private readonly AvaloniaUiBrandImages? _brand = AvaloniaUiBrandImages.TryLoadProduct();
    private readonly Bitmap? _fallback;
    private AvaloniaUiBrandImages? _themeBrand;
    private AvaloniaUiThemeStartupSnapshot? _themeSnapshot;
    private AvaloniaUiThemeSplashMotion _splashMotion;
    private bool _splashReduced;
    private readonly Image _mark;
    private readonly TextBlock _name, _edition, _version, _stage;
    private readonly Border _rail;
    private readonly AvaloniaUiProgressRing _statusRing;
    private readonly Grid _status;
    private readonly ScrollViewer _stageViewport;
    private readonly StackPanel _buttons;
    private readonly Button _cancel, _retry, _logLink;
    private readonly TextBlock _logLabel;
    private string? _failureLog;
    private Func<string, string> _localize;
    private string _stageSource = "启动中";
    private bool _dark, _failed, _disposed;
    internal Grid Header { get; }
    internal WindowIcon? ProductIcon => (_themeBrand ?? _brand)?.ResolveIcon(_dark);

    internal AvaloniaUiStartupCard(Func<string, string> localize, Action? cancel = null,
        Action? retry = null, Stream? fallback = null, Action? openLog = null)
    {
        _localize = localize;
        if (_brand is null && fallback is not null) _fallback = new Bitmap(fallback);
        Margin = new Thickness(12);
        Padding = new Thickness(22);
        CornerRadius = new CornerRadius(22);
        BorderThickness = new Thickness(1);
        BoxShadow = new BoxShadows(new BoxShadow { Blur = 20, OffsetY = 6, Color = Color.FromArgb(42, 0, 22, 32) });
        var family = AvaloniaUiTypefaceCache.GetDefault(FontWeight.Normal).FontFamily;
        TextBlock Text(string text, double size, FontWeight weight = default) => new()
        {
            Text = text,
            FontSize = size,
            FontFamily = family,
            FontWeight = weight == default ? FontWeight.Normal : weight,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _mark = new Image { Width = 76, Height = 76, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        AutomationProperties.SetName(_mark, "NexaCL");
        _name = Text("NexaCL", 28, FontWeight.SemiBold);
        _edition = Text("Firefly", 12, FontWeight.Medium);
        _version = Text("", 11);
        var identity = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _name, _edition, _version },
        };
        Grid.SetColumn(identity, 1);
        Header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("92,*"),
            Background = Brushes.Transparent,
            Children = { _mark, identity }
        };
        _rail = new Border { Height = 2, CornerRadius = new CornerRadius(1), IsHitTestVisible = false };
        _statusRing = new AvaloniaUiProgressRing
        {
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _stage = Text(localize(_stageSource), 12);
        _stage.TextWrapping = TextWrapping.Wrap;
        _stage.TextTrimming = TextTrimming.None;
        AutomationProperties.SetLiveSetting(_stage, AutomationLiveSetting.Polite);
        _stageViewport = new ScrollViewer
        {
            Content = _stage,
            Height = 42,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_stageViewport, 1);
        _status = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("40,*"),
            Children = { _statusRing, _stageViewport },
        };
        _cancel = new Button
        {
            Content = localize("取消并退出"),
            FontFamily = family,
            FontSize = 12,
            Height = 32,
            Padding = new Thickness(12, 4),
            CornerRadius = new CornerRadius(9),
            IsCancel = true
        };
        _retry = new Button
        {
            Content = localize("重试"),
            FontFamily = family,
            FontSize = 12,
            Height = 32,
            Padding = new Thickness(16, 4),
            CornerRadius = new CornerRadius(9),
            IsVisible = false
        };
        _cancel.Click += (_, _) => cancel?.Invoke();
        _retry.Click += (_, _) => { _retry.IsEnabled = false; retry?.Invoke(); };
        _logLabel = Text(localize("日志文件不可用"), 12);
        _logLabel.TextDecorations = TextDecorations.Underline;
        _logLink = new Button
        {
            Content = _logLabel,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 4),
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsVisible = false,
            IsEnabled = false
        };
        AutomationProperties.SetAutomationId(_logLink, "StartupLogLink");
        _logLink.Click += (_, _) => { if (_failed && _failureLog is not null) openLog?.Invoke(); };
        _buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            IsVisible = cancel is not null,
            Children = { _retry, _cancel }
        };
        // Keep the identity header as the native drag region. Recovery buttons and
        // the scrollable hint remain outside it, aligned with the identity column.
        var details = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(92, 0, 0, 0),
            Children = { _rail, _status, _logLink, _buttons },
        };
        Child = new StackPanel { Spacing = 14, Children = { Header, details } };
    }

    internal void ApplySplashMotion(AvaloniaUiThemeSplashMotion motion, bool reducedMotion)
    {
        if (_splashMotion == motion && _splashReduced == reducedMotion) return;
        AvaloniaUiMotion.CancelAll(this);
        _splashMotion = motion; _splashReduced = reducedMotion;
        _statusRing.ReducedMotion = reducedMotion; _statusRing.UpdateMotion();
        _mark.Opacity = 1; _mark.Clip = null;
        if (reducedMotion || motion is AvaloniaUiThemeSplashMotion.Default or AvaloniaUiThemeSplashMotion.None or AvaloniaUiThemeSplashMotion.Fade) return;
        if (motion == AvaloniaUiThemeSplashMotion.Breathe) Breathe(lower: true);
        else if (motion == AvaloniaUiThemeSplashMotion.Draw)
        {
            var clip = new RectangleGeometry(new Rect(0, 0, 0, 76)); _mark.Clip = clip;
            double progress = 0;
            AvaloniaUiMotion.Animate(this, "theme-splash-draw", () => progress,
                value => { progress = value; clip.Rect = new Rect(0, 0, 76 * value, 76); }, 1, 600,
                completed: () => { if (!_disposed && _splashMotion == motion) _mark.Clip = null; },
                reducedMotion: () => _splashReduced || _disposed);
        }
        void Breathe(bool lower)
        {
            if (_disposed || _splashReduced || _splashMotion != AvaloniaUiThemeSplashMotion.Breathe) return;
            AvaloniaUiMotion.Animate(this, "theme-splash-breathe", () => _mark.Opacity, value => _mark.Opacity = value,
                lower ? .88 : 1, 1800, completed: () => Breathe(!lower), reducedMotion: () => _splashReduced || _disposed);
        }
    }

    internal void SetThemeSnapshot(AvaloniaUiThemeStartupSnapshot? snapshot)
    {
        if (_themeSnapshot == snapshot) return;
        AvaloniaUiBrandImages? replacement = null;
        if (snapshot is { BrandLightPng.IsEmpty: false })
        {
            using var light = new MemoryStream(snapshot.BrandLightPng.ToArray(), writable: false);
            using var dark = new MemoryStream(snapshot.BrandDarkPng.ToArray(), writable: false);
            replacement = AvaloniaUiBrandImages.Load(light, dark);
        }
        AvaloniaUiBrandImages? previous = _themeBrand;
        _themeBrand = replacement; _themeSnapshot = snapshot;
        _mark.Source = (_themeBrand ?? _brand)?.Resolve(_dark) ?? _fallback;
        previous?.Dispose();
    }

    internal void ApplyAppearance(bool dark, string version)
    {
        _dark = dark;
        _mark.Source = (_themeBrand ?? _brand)?.Resolve(dark) ?? _fallback;
        Color surface = dark ? Color.FromRgb(13, 26, 36) : Color.FromRgb(248, 252, 255);
        Color lower = dark ? Color.FromRgb(9, 20, 29) : Color.FromRgb(232, 243, 250);
        Color mint = dark ? Color.FromRgb(80, 229, 195) : Color.FromRgb(14, 126, 134);
        Color cyan = dark ? Color.FromRgb(48, 187, 239) : Color.FromRgb(38, 125, 194);
        Background = Gradient(surface, lower, vertical: true);
        BorderBrush = Gradient(Color.FromArgb(dark ? (byte)90 : (byte)100, mint.R, mint.G, mint.B),
            Color.FromArgb(45, cyan.R, cyan.G, cyan.B));
        _rail.Background = Gradient(mint, cyan);
        _name.Foreground = new SolidColorBrush(dark ? Color.FromRgb(231, 244, 249) : Color.FromRgb(25, 48, 64));
        _edition.Foreground = new SolidColorBrush(mint);
        _version.Foreground = new SolidColorBrush(dark ? Color.FromRgb(137, 166, 181) : Color.FromRgb(95, 119, 137));
        _version.Text = "v" + version;
        string[] parts = version.Split('.');
        _edition.Text = parts.Length == 5 && parts[3] is "alpha" or "beta"
            ? "Firefly " + (parts[3] == "alpha" ? "Alpha " : "Beta ") + parts[4] : "Firefly";
        _cancel.Background = Brushes.Transparent;
        _cancel.BorderBrush = new SolidColorBrush(dark ? Color.FromRgb(46, 65, 78) : Color.FromRgb(196, 216, 227));
        _cancel.Foreground = _version.Foreground;
        _retry.Background = new SolidColorBrush(dark ? Color.FromRgb(20, 65, 70) : Color.FromRgb(210, 241, 244));
        _retry.Foreground = _name.Foreground;
        _logLabel.Foreground = _edition.Foreground;
        if (_themeSnapshot?.Colors is { } colors)
        {
            Background = ThemeBrush(colors.Background);
            _name.Foreground = ThemeBrush(colors.Foreground);
            _edition.Foreground = ThemeBrush(colors.Accent);
            _rail.Background = ThemeBrush(colors.Accent);
            _version.Foreground = ThemeBrush(colors.Foreground with { Alpha = 180 });
        }
        _logLabel.Foreground = _edition.Foreground;
        ApplyStatus();
    }

    private static SolidColorBrush ThemeBrush(XsrUiColor color) => new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));

    internal void SetStage(string stage, bool failed, bool canRetry)
    {
        _stageSource = stage;
        _failed = failed;
        AutomationProperties.SetLiveSetting(_stage, failed ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        string display = _localize(stage);
        _stage.Text = display.Length <= 1024 ? display : display[..1024];
        _stageViewport.Height = failed ? 108 : 42;
        _status.ColumnDefinitions[0].Width = new GridLength(40);
        _stageViewport.Offset = default;
        _retry.IsVisible = canRetry;
        _retry.IsEnabled = canRetry;
        _retry.IsDefault = canRetry;
        _logLink.IsVisible = failed;
        ApplyStatus();
    }

    internal void SetFailureLog(string? path)
    {
        _failureLog = path;
        _logLabel.Text = path is null ? _localize("日志文件不可用") : _localize("查看日志") + "：" + Path.GetFileName(path);
        _logLink.IsEnabled = path is not null;
        ToolTip.SetTip(_logLink, path);
    }

    private void ApplyStatus()
    {
        Color ink = _failed ? (_dark ? Color.FromRgb(255, 186, 130) : Color.FromRgb(153, 74, 24))
            : _dark ? Color.FromRgb(179, 206, 219) : Color.FromRgb(74, 101, 121);
        _stage.Foreground = new SolidColorBrush(ink);
        _statusRing.Ink = _edition.Foreground ?? Brushes.DodgerBlue;
        _statusRing.Failed = _failed;
        _statusRing.Ink = _failed ? new SolidColorBrush(ink) : _statusRing.Ink;
        _statusRing.IsVisible = true;
        _statusRing.UpdateMotion();
    }

    internal void Relocalize(Func<string, string> localize)
    {
        _localize = localize;
        _cancel.Content = localize("取消并退出");
        _retry.Content = localize("重试");
        SetFailureLog(_failureLog);
        SetStage(_stageSource, _failed, _retry.IsVisible);
    }

    private static LinearGradientBrush Gradient(Color from, Color to, bool vertical = false) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(vertical ? 0 : 1, vertical ? 1 : 0, RelativeUnit.Relative),
        GradientStops = [new GradientStop(from, 0), new GradientStop(to, 1)],
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _statusRing.Dispose();
        AvaloniaUiMotion.CancelAll(this);
        _mark.Source = null;
        _themeBrand?.Dispose();
        _brand?.Dispose();
        _fallback?.Dispose();
    }
}
