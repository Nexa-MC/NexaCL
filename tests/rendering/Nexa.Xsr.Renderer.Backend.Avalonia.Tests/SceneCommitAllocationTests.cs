using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Threading;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void SceneCommitAllocationStaysWithinOneSceneNodePerNode()
    {
        var shell = XsrUiShellComposer.Compose(new XsrStateStoreBuilder().Build());
        shell.Renderer.ReducedMotion = true;
        XsrUiEntityId last = default;
        for (int index = 0; index < 400; index++)
        {
            last = shell.Tree.Create("row");
            shell.Tree.Attach(last, shell.Content);
            shell.Tree.SetComponent(last, new XsrUiElement { Height = 20 });
            shell.Tree.SetComponent(last, new XsrUiText("Row"));
        }

        using AvaloniaUiSceneSurface surface = new(shell);
        surface.Measure(new Size(850, 500));
        surface.Arrange(new Rect(0, 0, 850, 500));
        surface.CommitScene();
        Dispatcher.UIThread.RunJobs();
        int nodes = surface.Scene!.Count;
        AssertTrue(nodes >= 400);

        // One repainted row commits a whole new scene. The scene array is the renderer's own
        // cost; applying it must not allocate again for every node it leaves unchanged.
        void Frame() { shell.Tree.MarkDirty(last, XsrUiDirtyKinds.Paint); surface.CommitScene(); }
        for (int warm = 0; warm < 5; warm++) Frame();
        const int frames = 20;
        long version = surface.Scene!.Version;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < frames; frame++) Frame();
        long perNode = (GC.GetAllocatedBytesForCurrentThread() - before) / frames / nodes;
        AssertEqual(version + frames, surface.Scene!.Version);
        if (perNode >= 2 * Unsafe.SizeOf<XsrUiSceneNode>())
            throw new InvalidOperationException($"A scene commit allocated {perNode} bytes per node for {nodes} nodes.");
    }
}
