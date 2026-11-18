using Avalonia;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Wire;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void SceneCommitReadsMotionPolicyOnce()
    {
        var shell = XsrUiShellComposer.Compose(new XsrStateStoreBuilder().Build());
        for (int index = 0; index < 30; index++)
        {
            var leaf = shell.Tree.Create();
            shell.Tree.SetComponent(leaf, new XsrUiText("static"));
            shell.Tree.Attach(leaf, shell.Content);
        }
        using var endpoint = new XsrUiShellEndpoint(shell);
        var input = new MotionPolicyCounter(new XsrUiRendererEndpoint(shell.Renderer));
        var renderer = new XsrUiRemoteRenderer(input, _ => { });
        _ = renderer.EffectiveReducedMotion; // Capture the generated request for this property.
        using var surface = new AvaloniaUiSceneSurface(new XsrUiRemoteShell(endpoint, renderer));
        surface.Measure(new(800, 600));
        surface.Arrange(new(0, 0, 800, 600));
        surface.CommitScene();
        var control = surface.Children.OfType<AvaloniaUiSceneNodeControl>().First(n => n.Node.Text == "static");
        int previous = control.AppliedChanges;
        foreach (bool reduced in new[] { true, false })
        {
            shell.Renderer.ReducedMotion = reduced;
            input.Reads = 0;
            surface.CommitScene();
            AssertEqual(1, input.Reads);
            AssertEqual(++previous, control.AppliedChanges);
        }
    }

    private sealed class MotionPolicyCounter(IXsrUiWireTransport endpoint) : IXsrUiWireTransport
    {
        private byte[]? _policyRequest;
        public int Reads { get; set; }
        public byte[] Exchange(ReadOnlyMemory<byte> request)
        {
            _policyRequest ??= request.ToArray();
            if (request.Span.SequenceEqual(_policyRequest)) Reads++;
            return endpoint.Exchange(request);
        }
    }

    private static void UnchangedNodesSkipApplyAndRetainClipGeometry()
    {
        var tree = new XsrUiTree();
        var entity = tree.Create("stable");
        var node = new XsrUiSceneNode(entity, new(10, 20, 100, 40), 0,
            XsrUiSemanticRole.Text, "Stable", "content", null, false, null, null,
            ClipRect: new(10, 20, 100, 40));
        var control = new AvaloniaUiSceneNodeControl(_ => { }, _ => { }, () => true);
        control.Apply(node);
        var clip = control.Clip;
        for (int index = 0; index < 1000; index++) control.Apply(node);
        AssertEqual(1, control.AppliedChanges);
        AssertTrue(ReferenceEquals(clip, control.Clip));
        control.Apply(node with { Text = "changed" });
        AssertEqual(2, control.AppliedChanges);
        AssertTrue(ReferenceEquals(clip, control.Clip));
        control.Apply(node with { Rect = new(11, 20, 100, 40), Text = "changed" });
        AssertTrue(!ReferenceEquals(clip, control.Clip));
        control.ReleasePresentation();
    }
}
