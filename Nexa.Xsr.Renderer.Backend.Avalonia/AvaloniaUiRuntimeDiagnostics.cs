namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public static class AvaloniaUiRuntimeDiagnostics
{
    private static long _committedScenes, _nodePaintCallbacks;
    internal static void SceneCommitted() => Interlocked.Increment(ref _committedScenes);
    internal static void NodePainted() => Interlocked.Increment(ref _nodePaintCallbacks);

    /// <summary>Copies only counters and the latest sample; it performs no native I/O or render.</summary>
    public static AvaloniaUiRuntimeCapture Capture()
    {
        var motion = AvaloniaUiMotion.CaptureDiagnostics();
        var raster = AvaloniaUiRasterPool.Shared.CaptureDiagnostics();
        return new(Interlocked.Read(ref _committedScenes), Interlocked.Read(ref _nodePaintCallbacks),
            motion.ActiveTracks, motion.TimerRunning, motion.TargetFramesPerSecond, motion.TickRate,
            raster.Bytes, raster.AdmissionBudget, raster.Entries, raster.Leases, raster.DecodeAttempts,
            raster.DisposedBitmaps, raster.Pressure);
    }
}
