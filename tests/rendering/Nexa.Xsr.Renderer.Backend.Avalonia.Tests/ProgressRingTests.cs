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
        var outcome = new AvaloniaUiOutcomeAnimation(() => invalidations++);
        outcome.Update("success", true, false);
        AssertEqual(baseline + 1, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
        outcome.Update("success", true, false);
        AssertEqual(baseline + 1, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
        outcome.Update("success", false, false);
        AssertEqual(1d, outcome.Progress);
        AssertEqual(baseline, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
        outcome.Update("success", true, false); // A retained result does not re-enter.
        AssertEqual(baseline, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
        outcome.Update("failure", true, true);
        AssertEqual(1d, outcome.Progress);
        AssertEqual(baseline, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
        var start = AvaloniaUiProgressRingDrawing.Sample(0, true, 0);
        var expanded = AvaloniaUiProgressRingDrawing.Sample(.5, true, 0);
        AssertTrue(expanded.Sweep > start.Sweep);
        AssertEqual(start, AvaloniaUiProgressRingDrawing.Sample(1, true, 0));
        AssertEqual(90d, AvaloniaUiProgressRingDrawing.Sample(0, false, .25).Sweep);
        foreach (bool success in new[] { false, true })
        {
            int previousPainted = -1;
            foreach (double phase in new[] { 0d, .55, 1d })
            {
                using var outcomeFrame = new RenderTargetBitmap(new PixelSize(48, 48), new Vector(96, 96));
                using (var context = outcomeFrame.CreateDrawingContext())
                    AvaloniaUiOutcomeAnimation.Draw(context, new Rect(0, 0, 48, 48), global::Avalonia.Media.Brushes.White, success, phase);
                using var outcomePixels = ReadBrandPixels(outcomeFrame);
                int count = 0;
                for (int y = 0; y < 48; y++)
                    for (int x = 0; x < 48; x++) if (outcomePixels.GetPixel(x, y).Alpha > 0) count++;
                AssertTrue(count > previousPainted);
                AssertTrue(count < 1100); // Stroke geometry, never a filled status disk.
                previousPainted = count;
            }
        }
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
