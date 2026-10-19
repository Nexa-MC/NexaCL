using Avalonia;
using Avalonia.Media.Imaging;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void ProgressRingSharesClockStopsAndDrawsHollowArc()
    {
        bool enabled = true;
        int invalidations = 0;
        int baseline = AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks;
        var motion = new AvaloniaUiProgressRingAnimation(() => enabled, () => invalidations++);
        motion.Update(); motion.Update();
        AssertEqual(baseline + 1, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
        enabled = false; motion.Update();
        AssertEqual(baseline, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
        AssertEqual(.25, motion.Phase);
        AssertEqual(0, invalidations); // A stop may be called from the startup render pass.
        enabled = true; motion.Update(); motion.Stop();
        AssertEqual(baseline, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
        var start = AvaloniaUiProgressRingDrawing.Sample(0, true, 0);
        var expanded = AvaloniaUiProgressRingDrawing.Sample(.5, true, 0);
        AssertTrue(expanded.Sweep > start.Sweep);
        AssertEqual(start, AvaloniaUiProgressRingDrawing.Sample(1, true, 0));
        AssertEqual(90d, AvaloniaUiProgressRingDrawing.Sample(0, false, .25).Sweep);
        using var card = new AvaloniaUiStartupCard(text => text, cancel: () => { });
        card.Measure(new Size(AvaloniaUiStartupCard.WindowWidth, double.PositiveInfinity));
        AssertTrue(card.DesiredSize.Height <= AvaloniaUiStartupCard.LoadingHeight);
        card.SetStage("failure", failed: true, canRetry: true);
        card.Measure(new Size(AvaloniaUiStartupCard.WindowWidth, double.PositiveInfinity));
        AssertTrue(card.DesiredSize.Height <= AvaloniaUiStartupCard.FailureHeight);
        using var ring = new AvaloniaUiProgressRing { ReducedMotion = true };
        ring.Measure(new Size(40, 40)); ring.Arrange(new Rect(0, 0, 40, 40));
        using var frame = new RenderTargetBitmap(new PixelSize(40, 40), new Vector(96, 96));
        frame.Render(ring);
        using var pixels = ReadBrandPixels(frame);
        AssertEqual((byte)0, pixels.GetPixel(20, 20).Alpha);
        int painted = 0;
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 40; x++) if (pixels.GetPixel(x, y).Alpha > 0) painted++;
        AssertTrue(painted > 20 && painted < 500);
    }
}
