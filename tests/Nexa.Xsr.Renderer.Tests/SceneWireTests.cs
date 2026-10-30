using Nexa.Core.Media;
using Nexa.Xsr.Renderer.Wire;

namespace Nexa.Xsr.Renderer.Tests;

internal static partial class Program
{
    private static XsrUiScene WireScene(PngImage? image = null)
    {
        var node = new XsrUiSceneNode(new(12, 3), new(1, 2, 300, 400), 2,
            XsrUiSemanticRole.None, "中文 🌌", "content", null, true, .25, .75,
            RasterImage: image is null ? null : new(image, []), PresentationOpacity: .6)
        {
            ThemeActivationGeneration = 47,
            IsChecked = true,
            ClipGeometry = [new(new(0, 0, 100, 200))],
        };
        return new(23, [node], [new(new(15, 2), [node], true)]);
    }

    private static void SceneWireRoundTripAndAdmissionAreAtomic()
    {
        using var sender = new XsrUiSceneSender();
        var receiver = new XsrUiSceneReceiver();
        using var packet = sender.Prepare(WireScene());
        AssertThrows<InvalidOperationException>(() => sender.Prepare(WireScene()));
        var decoded = receiver.Accept(packet.Bytes);
        AssertEqual(23L, decoded.Version);
        AssertEqual("中文 🌌", decoded[0].Label);
        AssertEqual(new XsrUiEntityId(12, 3), decoded[0].Entity);
        AssertEqual(47L, decoded[0].ThemeActivationGeneration);
        AssertEqual(new XsrUiRect(0, 0, 100, 200), decoded[0].ClipGeometry![0].Rect);
        AssertTrue(decoded.Outgoing[0].BehindSelf);
        packet.Acknowledge(receiver.Sequence);
        AssertThrows<InvalidDataException>(() => receiver.Accept(packet.Bytes));
        using var next = sender.Prepare(WireScene());
        byte[] bytes = next.Bytes.ToArray();
        foreach (int length in new[] { 0, 3, 17, bytes.Length - 1 })
            AssertThrows<InvalidDataException>(() => receiver.Accept(bytes.AsMemory(0, length)));
        AssertThrows<InvalidDataException>(() => receiver.Accept(bytes.Concat(new byte[] { 0 }).ToArray()));
        bytes[0] ^= 1;
        AssertThrows<InvalidDataException>(() => receiver.Accept(bytes));
        AssertTrue(ReferenceEquals(decoded, receiver.Scene));
        AssertEqual(packet.Sequence, receiver.Sequence);
        receiver.Accept(next.Bytes);
        next.Acknowledge(receiver.Sequence);
    }

    private static void SceneWireImagesReuseRetireAndResynchronize()
    {
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        var image = PngImage.TryCreatePreview(png) ?? throw new InvalidOperationException("Invalid test image.");
        using var sender = new XsrUiSceneSender();
        var receiver = new XsrUiSceneReceiver();
        using var first = sender.Prepare(WireScene(image));
        var decoded = receiver.Accept(first.Bytes);
        AssertEqual(image.Key, decoded[0].RasterImage!.Image.Key);
        AssertEqual(1, receiver.RetainedImageCount);
        first.Acknowledge(receiver.Sequence);
        using var second = sender.Prepare(WireScene(image));
        AssertTrue(second.Bytes.Length < first.Bytes.Length);
        var repeated = receiver.Accept(second.Bytes);
        AssertTrue(ReferenceEquals(decoded[0].RasterImage!.Image, repeated[0].RasterImage!.Image));
        // Accepted remotely, but the acknowledgement was lost: next frame must define its images again.
        second.Dispose();
        using var resync = sender.Prepare(WireScene(image));
        AssertEqual(first.Bytes.Length, resync.Bytes.Length);
        receiver.Accept(resync.Bytes);
        resync.Acknowledge(receiver.Sequence);
        using var empty = sender.Prepare(new(24, []));
        receiver.Accept(empty.Bytes);
        empty.Acknowledge(receiver.Sequence);
        AssertEqual(0, receiver.RetainedImageCount);
        sender.Dispose();
        AssertThrows<ObjectDisposedException>(() => sender.Prepare(WireScene()));
    }
    private sealed class WireTextProvider : IXsrUiTextLayoutProvider
    {
        public long Revision => 19;
        public int Calls { get; private set; }
        public XsrUiTextLayoutRequest? Last { get; private set; }
        public XsrUiTextLayoutMetrics Measure(XsrUiTextLayoutRequest request)
        {
            Calls++; Last = request;
            return new(new(128, 24), [0, 12, 24]);
        }
    }

    private static void TextWirePreservesUnboundedLayoutAndRejectsTrailingRequests()
    {
        var local = new WireTextProvider();
        var endpoint = new XsrUiTextLayoutEndpoint(local);
        var transport = new RecordingUiTransport(endpoint);
        var remote = new XsrUiRemoteTextLayoutProvider(transport);
        AssertEqual(19L, remote.Revision);
        var metrics = remote.Measure(new("中文", "font", 16, 400, IncludeCaretPositions: true));
        AssertEqual(new XsrUiSize(128, 24), metrics.Size);
        AssertSequence(new double[] { 0, 12, 24 }, metrics.CaretPositions!.ToArray());
        AssertTrue(double.IsPositiveInfinity(local.Last!.MaximumWidth));
        AssertTrue(local.Last.IncludeCaretPositions);
        AssertThrows<InvalidDataException>(() => endpoint.Exchange(transport.Last!.Concat(new byte[] { 0 }).ToArray()));
        AssertEqual(1, local.Calls);
    }

    private sealed class RecordingUiTransport(IXsrUiWireTransport endpoint) : IXsrUiWireTransport
    {
        public byte[]? Last { get; private set; }
        public byte[] Exchange(ReadOnlyMemory<byte> request)
        { Last = request.ToArray(); return endpoint.Exchange(request); }
    }

    private static void InputWirePreservesHitsAndRejectsBeforeMutation()
    {
        var tree = new XsrUiTree();
        var root = tree.Create("wire-input");
        tree.SetComponent(root, new XsrUiElement { Width = 200, Height = 100 });
        tree.SetComponent(root, new XsrUiTransition());
        var renderer = new XsrUiRenderer(tree, new Nexa.Xsr.State.XsrStateStoreBuilder().Build());
        renderer.SetRoot(root);
        var scene = renderer.Render();
        var endpoint = new XsrUiRendererEndpoint(renderer);
        var recording = new RecordingUiTransport(endpoint);
        var remote = new XsrUiRemoteRenderer(recording,
            provider => renderer.TextLayoutProvider = provider is null ? null :
                new XsrUiRemoteTextLayoutProvider(new XsrUiTextLayoutEndpoint(provider)));
        AssertEqual(renderer.HitTest(new(10, 10)), remote.HitTest(new(10, 10)));
        AssertEqual(renderer.PointerCursorAt(new(10, 10)), remote.PointerCursorAt(new(10, 10)));
        AssertEqual(renderer.ColorScheme, remote.ColorScheme);
        remote.SetTransitionOffset(scene[0].Entity, 14);
        AssertClose(14, renderer.GetTransitionOffset(scene[0].Entity));
        byte[] request = recording.Last!;
        renderer.SetTransitionOffset(scene[0].Entity, 28);
        AssertThrows<InvalidDataException>(() => endpoint.Exchange(request.Concat(new byte[] { 0 }).ToArray()));
        AssertClose(28, renderer.GetTransitionOffset(scene[0].Entity));
        var text = new WireTextProvider();
        remote.TextLayoutProvider = text;
        AssertEqual(19L, renderer.TextLayoutProvider!.Revision);
        remote.TextLayoutProvider = null;
        AssertTrue(renderer.TextLayoutProvider is null);
    }

    private static void ShellWirePreservesWindowMetricsAndRecoversAfterLostAcknowledgement()
    {
        var builder = new Nexa.Xsr.State.XsrStateStoreBuilder();
        XsrUiShellWindowState.Declare(builder);
        var shell = new XsrUiShell(builder.Build());
        using var endpoint = new XsrUiShellEndpoint(shell);
        var transport = new InterruptedShellTransport(endpoint);
        var remote = new XsrUiRemoteShell(transport, shell.Renderer);
        AssertEqual(shell.Title, remote.Title);
        AssertEqual(shell.SelectedNavigationId, remote.SelectedNavigationId);
        remote.PublishWindowMetrics(86, false, 8);
        var scene = remote.Render(new(850, 500));
        AssertEqual(new XsrUiRect(8, 8, 834, XsrUiShell.TitleBarHeight), Node(scene, shell.TitleBar).Rect);
        remote.PublishWindowMetrics(86, true, 8);
        scene = remote.Render(new(1040, 680));
        AssertEqual(new XsrUiRect(0, 0, 1040, XsrUiShell.TitleBarHeight), Node(scene, shell.TitleBar).Rect);
        transport.DropAcknowledgement = true;
        AssertThrows<IOException>(() => remote.Render(new(1040, 680)));
        transport.DropAcknowledgement = false;
        scene = remote.Render(new(900, 600));
        AssertEqual(900d, Node(scene, shell.TitleBar).Rect.Width);
        int refreshed = 0;
        remote.RenderRequested += (_, _) => refreshed++;
        remote.NotifyInvalidated(XsrUiShellInvalidation.Render);
        AssertEqual(1, refreshed);
        remote.SetRailPresentationProgress(.3);
        var request = transport.Last!;
        shell.SetRailPresentationProgress(.8);
        AssertThrows<InvalidDataException>(() => endpoint.Exchange(request.Concat(new byte[] { 0 }).ToArray()));
        AssertClose(.8, shell.RailPresentationProgress);
        int notices = 0;
        endpoint.Invalidated += _ => notices++;
        shell.ToggleNavigationExpanded();
        AssertTrue(notices > 0);
        endpoint.Dispose();
        notices = 0;
        shell.ToggleNavigationExpanded();
        AssertEqual(0, notices);
        AssertThrows<ObjectDisposedException>(() => remote.Render(new(900, 600)));
    }

    private sealed class InterruptedShellTransport(IXsrUiWireTransport endpoint) : IXsrUiWireTransport
    {
        public bool DropAcknowledgement { get; set; }
        public byte[]? Last { get; private set; }
        public byte[] Exchange(ReadOnlyMemory<byte> request)
        {
            Last = request.ToArray();
            // Header is magic, length-prefixed schema fingerprint, then the fixed operation number.
            var bytes = request.Span;
            int operationOffset = 8 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
            if (DropAcknowledgement && System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes[operationOffset..]) == 2)
                throw new IOException("Simulated lost acknowledgement.");
            return endpoint.Exchange(request);
        }
    }

}
