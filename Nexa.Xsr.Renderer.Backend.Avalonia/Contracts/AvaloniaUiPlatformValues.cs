using Nexa.Core.Media;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public enum AvaloniaUiInputKind { Keyboard, Mouse, Touch, Controller }

public enum AvaloniaUiThemePickerKind { Package, Image, Icon, Font, Audio, Video }

public sealed record AvaloniaUiDesktopPolicy(bool TrayEnabled = true, bool CloseToTray = false, bool MinimizeToTray = false);

public enum AvaloniaUiMemoryPressure { Unknown, Normal, Elevated, Critical }
public readonly record struct AvaloniaUiMemoryObservation(AvaloniaUiMemoryPressure Pressure,
    long? AvailableBytes, long? LimitBytes, long? GpuResidentBytes, string? Source);

/// <summary>Native process-wide submission and admission facts; no physical display FPS claim.</summary>
public readonly record struct AvaloniaUiRuntimeCapture(long CommittedScenes, long NodePaintCallbacks,
    int ActiveMotionTracks, bool MotionTimerRunning, int MotionTargetFramesPerSecond, double? MotionTickRate,
    long RasterPixelChargeBytes, long RasterAdmissionBudgetBytes, int RasterEntries, int RasterLeases,
    long RasterDecodeAttempts, long RasterDisposedBitmaps, AvaloniaUiMemoryObservation MemoryPressure);

public enum AvaloniaUiMediaStatus { Stopped, Playing, Paused, Completed, DependencyMissing, Failed }
public sealed record AvaloniaUiMediaState(AvaloniaUiMediaStatus Status, string? Detail = null, long Generation = 0)
{
    public AvaloniaUiMediaState(AvaloniaUiMediaStatus status, string? detail) : this(status, detail, 0) { }
    public void Deconstruct(out AvaloniaUiMediaStatus status, out string? detail) { status = Status; detail = Detail; }
}
public sealed record AvaloniaUiMediaFrame(long Generation, string Path, PngImage Image);
