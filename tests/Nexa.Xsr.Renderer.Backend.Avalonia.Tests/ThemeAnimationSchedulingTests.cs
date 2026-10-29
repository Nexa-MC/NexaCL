using Avalonia;
using Avalonia.Threading;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.State;
namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;
internal static partial class Program
{
    private sealed class BreathingTheme : IXsrUiThemeStyleProgram
    {
        public long Revision => 1;
        public XsrUiComputedStyle Compute(XsrUiThemeStyleContext context) => new()
        {
            Animations = [new(XsrUiStyleAnimationProperty.Opacity, TimeSpan.FromSeconds(3),
                [new(0, .86), new(1, 1)], Repeat: true, Alternate: true)]
        };
    }
    private static void ThemeAnimationFramesDoNotScheduleTheirOwnSuccessors()
    {
        var shell = new XsrUiShell(new XsrStateStoreBuilder().Build());
        using var surface = new AvaloniaUiSceneSurface(shell);
        surface.Measure(new Size(850, 500)); surface.Arrange(new Rect(0, 0, 850, 500));
        shell.Renderer.ApplyTheme(new BreathingTheme(), 1);
        surface.CommitScene();
        int invalidations = 0;
        shell.Tree.RenderInvalidated += (_, _) => invalidations++;
        for (int frame = 0; frame < 5; frame++)
        {
            int before = invalidations;
            surface.CommitScene();
            AssertEqual(0, invalidations - before);
        }
        AssertTrue(surface.ThemeAnimationClockActive);
        int beforeTick = invalidations;
        AssertTrue(shell.Renderer.AdvanceThemeAnimations());
        AssertEqual(beforeTick + 1, invalidations);
        surface.CommitScene();
        AssertEqual(beforeTick + 1, invalidations);
        shell.Renderer.ReducedMotion = true;
        surface.CommitScene();
        int baseline = invalidations; surface.CommitScene();
        AssertEqual(0, invalidations - baseline);
        int committed = 0, inputAt = -1;
        shell.Renderer.ReducedMotion = false;
        surface.SceneCommitted += (_, _) => { if (++committed == 40) shell.Renderer.ReducedMotion = true; };
        Dispatcher.UIThread.Post(() => inputAt = committed, DispatcherPriority.Input);
        surface.RequestCommit();
        Dispatcher.UIThread.RunJobs();
        AssertTrue(inputAt >= 0 && inputAt < 40);
        AssertTrue(committed < 40);
    }
}
