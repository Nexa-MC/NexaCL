namespace Nexa.Xsr.Renderer;

/// <summary>Authored identity retained independently of flattened host presentation.</summary>
public sealed record XsrUiStyleIdentity(string Kind, string? Key, IReadOnlyList<string> Classes, string? Role = null);

[Flags]
public enum XsrUiThemeInteractionState
{
    None = 0, Hover = 1, Pressed = 2, Focus = 4, FocusVisible = 8, Selected = 16, Disabled = 32, Checked = 64,
}

public sealed record XsrUiThemeEnvironment(string Platform = "unknown", bool IsDark = false,
    long EnvironmentRevision = 0, long OptionRevision = 0, long ResourceRevision = 0, long FontRevision = 0,
    IReadOnlySet<string>? Capabilities = null);

public sealed record XsrUiThemeStyleContext(XsrUiEntityId Entity, XsrUiStyleIdentity Identity,
    IReadOnlyList<XsrUiStyleIdentity> Parents, XsrUiThemeInteractionState InteractionState,
    XsrUiSize Viewport, XsrUiThemeEnvironment Environment, XsrUiVisualStyleSnapshot SourceVisual);

/// <summary>A prepared bounded declaration program. Compute performs no IO or application callbacks.</summary>
public interface IXsrUiThemeStyleProgram
{
    long Revision { get; }
    XsrUiComputedStyle Compute(XsrUiThemeStyleContext context);
}

public enum XsrUiThemePosition { Flow = 0, Absolute = 1, Overlay = 2 }
public enum XsrUiGridUnit { Auto = 0, Pixels = 1, Fraction = 2 }
public readonly record struct XsrUiGridTrack(XsrUiGridUnit Unit, double Value = 1);
public sealed record XsrUiGridLayout(IReadOnlyList<XsrUiGridTrack> Columns,
    IReadOnlyList<XsrUiGridTrack>? Rows = null, double ColumnGap = 0, double RowGap = 0);
public readonly record struct XsrUiStyleTransform(double TranslateX = 0, double TranslateY = 0,
    double ScaleX = 1, double ScaleY = 1, double Rotation = 0);
public readonly record struct XsrUiShadow(XsrUiColor Color, double OffsetX, double OffsetY, double BlurRadius);
public readonly record struct XsrUiGradientStop(double Position, XsrUiColor Color);
public sealed record XsrUiLinearGradient(double Angle, IReadOnlyList<XsrUiGradientStop> Stops);
public sealed record XsrUiBackgroundImage(string ResourceId, string Fit = "cover", double Opacity = 1);
public enum XsrUiStyleAnimationProperty { Opacity, TranslateX, TranslateY, ScaleX, ScaleY, Rotation, CornerRadius, BlurRadius }
public readonly record struct XsrUiStyleKeyframe(double Position, double Value);
public sealed record XsrUiStyleAnimation(XsrUiStyleAnimationProperty Property, TimeSpan Duration,
    IReadOnlyList<XsrUiStyleKeyframe> Keyframes, bool Repeat = false, bool Alternate = false);

/// <summary>Style-only overrides. This type deliberately contains no commands, tree ownership or business state.</summary>
public sealed record XsrUiComputedStyle
{
    public static XsrUiComputedStyle Empty { get; } = new();
    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? MinWidth { get; init; }
    public double? MaxWidth { get; init; }
    public double? MinHeight { get; init; }
    public double? MaxHeight { get; init; }
    public double? Weight { get; init; }
    public XsrUiThickness? Margin { get; init; }
    public XsrUiThickness? Padding { get; init; }
    public XsrUiAlignment? HorizontalAlignment { get; init; }
    public XsrUiAlignment? VerticalAlignment { get; init; }
    public XsrUiOrientation? Direction { get; init; }
    public double? Spacing { get; init; }
    public XsrUiColor? Background { get; init; }
    public XsrUiColor? Foreground { get; init; }
    public XsrUiColor? Border { get; init; }
    public XsrUiColor? Hover { get; init; }
    public XsrUiSurfaceKind? Surface { get; init; }
    public double? Opacity { get; init; }
    public double? CornerRadius { get; init; }
    public double? BorderWidth { get; init; }
    public double? BlurRadius { get; init; }
    public double? FontSize { get; init; }
    public double? FontWeight { get; init; }
    public string? FontFamily { get; init; }
    public string? ImageSource { get; init; }
    public XsrUiTextAlignment? TextAlignment { get; init; }
    public bool? WrapText { get; init; }
    public double? VirtualItemWidth { get; init; }
    public double? VirtualItemHeight { get; init; }
    public double? VirtualItemSpacing { get; init; }
    public int? VirtualItemColumns { get; init; }
    public int? Order { get; init; }
    public XsrUiThemePosition? Position { get; init; }
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double? Right { get; init; }
    public double? Bottom { get; init; }
    public XsrUiGridLayout? Grid { get; init; }
    public int? GridColumn { get; init; }
    public int? GridRow { get; init; }
    public int? GridColumnSpan { get; init; }
    public int? GridRowSpan { get; init; }
    public XsrUiStyleTransform? Transform { get; init; }
    public XsrUiShadow? Shadow { get; init; }
    public XsrUiBackgroundImage? BackgroundImage { get; init; }
    public XsrUiLinearGradient? BackgroundGradient { get; init; }
    public IReadOnlyList<XsrUiStyleAnimation>? Animations { get; init; }
}

public readonly record struct XsrUiLayoutFacts(double? Width, double? Height, double? MinWidth,
    double? MaxWidth, double? MinHeight, double? MaxHeight, XsrUiThickness Margin, XsrUiThickness Padding);

/// <summary>Same-generation virtual layout facts. Hosts retain item/scroll anchors when this value changes.</summary>
public enum XsrUiVirtualLayoutKind { List, Waterfall }
public readonly record struct XsrUiVirtualLayoutMetrics(long ActivationGeneration, long ProgramRevision,
    double ItemWidth, double ItemHeight, double Spacing, int Columns, int RealizationLimit)
{
    public XsrUiVirtualLayoutKind Kind { get; init; }
    public bool HasItemWidthOverride { get; init; }
    public bool HasItemHeightOverride { get; init; }
    public bool HasColumnsOverride { get; init; }
}

/// <summary>Marks host-owned virtual extent/realization structure that must not accept arbitrary layout overrides.</summary>
public sealed record XsrUiVirtualLayoutContract(double ItemWidth, double ItemHeight, double Spacing = 0,
    int Columns = 1, int RealizationLimit = 256, bool SupportsThemeMetrics = false, XsrUiVirtualLayoutKind Kind = XsrUiVirtualLayoutKind.List);

internal sealed record XsrUiVirtualGeometry;

/// <summary>A bounded style/placement diagnostic, distinct from application callback failures.</summary>
public sealed class XsrUiThemeStyleException : InvalidOperationException
{
    public XsrUiThemeStyleException() { }
    public XsrUiThemeStyleException(string message) : base(message) { }
    public XsrUiThemeStyleException(string message, Exception innerException) : base(message, innerException) { }
    public XsrUiThemeStyleException(XsrUiEntityId entity, string message) : base(message) => Entity = entity;
    public XsrUiEntityId? Entity { get; }
}

/// <summary>Finite frame recovery notification. A handler may clear or replace styles, never render recursively.</summary>
public sealed class XsrUiThemeStyleFailureEventArgs(XsrUiThemeStyleException exception,
    long activationGeneration, long programRevision) : EventArgs
{
    public XsrUiThemeStyleException Exception { get; } = exception;
    public long ActivationGeneration { get; } = activationGeneration;
    public long ProgramRevision { get; } = programRevision;
}
