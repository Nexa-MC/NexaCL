using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Threading;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void VerifyRawTouchInputAndPointerOwnership()
    {
        XsrUiIntentBuffer intents = new();
        XsrUiShell shell = XsrUiShellComposer.Compose(new XsrStateStoreBuilder().Build(), intentSink: intents);
        shell.Renderer.ReducedMotion = true;
        XsrUiEntityId page = shell.Tree.Create("touch-input-page");
        shell.Tree.SetComponent(page, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        XsrUiEntityId button = Leaf("touch-input-button", 300, 48);
        shell.Tree.SetComponent(button, new XsrUiInput { Clickable = true, Focusable = true });
        shell.Tree.SetComponent(button, new XsrUiSemantic(XsrUiSemanticRole.Button, "Touch action"));
        shell.Tree.SetComponent(button, new XsrUiCommandBinding(XsrSemanticId.Parse("test.touch.click")));
        XsrUiEntityId slider = Leaf("touch-input-range", 300, 40);
        XsrUiRangeInput range = new(0, 100, 1, 0);
        shell.Tree.SetComponent(slider, range);
        shell.Tree.SetComponent(slider, new XsrUiInput { Clickable = true, Focusable = true });
        shell.Tree.SetComponent(slider, new XsrUiSemantic(XsrUiSemanticRole.Slider, "Touch range"));
        XsrUiEntityId viewport = Leaf("touch-input-viewport", 300, 160);
        shell.Tree.SetComponent(viewport, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        XsrUiScroll scroll = new() { ShowsVerticalIndicator = true };
        shell.Tree.SetComponent(viewport, scroll);
        shell.Tree.SetComponent(viewport, new XsrUiScrollGesture());
        XsrUiEntityId content = shell.Tree.Create("touch-input-content");
        shell.Tree.SetComponent(content, new XsrUiElement { Width = 270, Height = 1400 });
        shell.Tree.SetComponent(content, new XsrUiSemantic(XsrUiSemanticRole.Text, "Scrollable touch content"));
        shell.Tree.Attach(content, viewport);
        XsrUiEntityId horizontalViewport = Leaf("touch-input-horizontal-viewport", 300, 70);
        shell.Tree.SetComponent(horizontalViewport, new XsrUiStackPanel(XsrUiOrientation.Horizontal));
        XsrUiScroll horizontalScroll = new() { ShowsHorizontalIndicator = true };
        shell.Tree.SetComponent(horizontalViewport, horizontalScroll);
        shell.Tree.SetComponent(horizontalViewport, new XsrUiScrollGesture());
        XsrUiEntityId horizontalContent = shell.Tree.Create("touch-input-horizontal-content");
        shell.Tree.SetComponent(horizontalContent, new XsrUiElement { Width = 1400, Height = 50 });
        shell.Tree.SetComponent(horizontalContent, new XsrUiSemantic(XsrUiSemanticRole.Text, "Horizontal touch cards"));
        shell.Tree.Attach(horizontalContent, horizontalViewport);
        shell.Stage.Navigation.Replace(page);
        AvaloniaUiShellWindow window = new(shell);
        try
        {
            window.Show();
            window.UpdateLayout();
            AvaloniaUiSceneSurface surface = window.Surface;
            surface.CommitScene();
            window.UpdateLayout();
            using RawTouchDriver touch = new(window, surface);
            XsrUiPoint buttonPoint = PointIn(button, .5, .5);
            IPointer first = touch.Press(1, buttonPoint);
            AssertEqual(PointerType.Touch, first.Type);
            AssertTrue(first.Captured == surface);
            AssertTrue(shell.Tree.GetComponent<XsrUiInput>(button)!.IsPressed);
            touch.Release(1, buttonPoint);
            AssertEqual(1, intents.Count);
            AssertEqual(button, intents.Drain()[0].Source);
            AssertTrue(first.Captured is null);
            AssertFalse(shell.Tree.GetComponent<XsrUiInput>(button)!.IsPressed);

            XsrUiPoint scrollPoint = PointIn(viewport, .5, .75);
            first = touch.Press(2, scrollPoint);
            touch.Move(2, scrollPoint with { Y = scrollPoint.Y - 55 });
            AssertTrue(scroll.OffsetY >= 50);
            AssertTrue(first.Captured == surface);
            touch.Release(2, scrollPoint with { Y = scrollPoint.Y - 55 });
            AssertFalse(Node(surface.Scene!, viewport).ScrollMotion!.Value.Dragging);
            AssertTrue(first.Captured is null);
            AssertEqual(0, intents.Count);

            XsrUiPoint rangeStart = PointIn(slider, .25, .5), rangeEnd = PointIn(slider, .75, .5);
            first = touch.Press(3, rangeStart);
            AssertTrue(range.Value is >= 20 and <= 30);
            touch.Move(3, rangeEnd);
            AssertTrue(range.Value is >= 70 and <= 80);
            touch.Release(3, rangeEnd);
            AssertTrue(first.Captured is null);
            AssertFalse(shell.Tree.GetComponent<XsrUiInput>(slider)!.IsPressed);

            XsrUiPoint horizontalPoint = PointIn(horizontalViewport, .75, .4);
            first = touch.Press(12, horizontalPoint);
            touch.Move(12, horizontalPoint with { X = horizontalPoint.X - 55 });
            AssertTrue(horizontalScroll.OffsetX >= 50);
            AssertEqual(0d, horizontalScroll.OffsetY);
            AssertTrue(first.Captured == surface);
            touch.Release(12, horizontalPoint with { X = horizontalPoint.X - 55 });
            AssertFalse(Node(surface.Scene!, horizontalViewport).ScrollMotion!.Value.Dragging);
            AssertTrue(first.Captured is null);
            AssertEqual(0, intents.Count);
            Console.WriteLine("PASS: real raw touch normalizes clicks, vertical/horizontal list drags and range drags through TouchDevice");

            // A second contact may neither replace the admitted contact nor release its gesture.
            first = touch.Press(4, rangeStart);
            double admittedValue = range.Value;
            Point mousePoint = surface.TranslatePoint(new Point(rangeEnd.X, rangeEnd.Y), window)!.Value;
            window.MouseMove(mousePoint);
            AssertEqual(admittedValue, range.Value);
            IPointer second = touch.Press(5, buttonPoint);
            AssertEqual(admittedValue, range.Value);
            AssertTrue(first.Captured == surface);
            touch.Move(5, rangeEnd);
            AssertEqual(admittedValue, range.Value);
            touch.Release(5, buttonPoint);
            AssertEqual(admittedValue, range.Value);
            AssertTrue(first.Captured == surface);
            AssertTrue(shell.Tree.GetComponent<XsrUiInput>(slider)!.IsPressed);
            AssertEqual(0, intents.Count);
            touch.Move(4, rangeEnd);
            AssertTrue(range.Value is >= 70 and <= 80);
            touch.Release(4, rangeEnd);
            AssertFalse(shell.Tree.GetComponent<XsrUiInput>(slider)!.IsPressed);
            AssertTrue(first.Captured is null);
            AssertTrue(second.Captured is null);

            // Capture loss from an ignored contact must not cancel the admitted slider drag.
            first = touch.Press(6, rangeStart);
            second = touch.Press(7, buttonPoint);
            touch.Cancel(7, buttonPoint);
            AssertTrue(first.Captured == surface);
            AssertTrue(shell.Tree.GetComponent<XsrUiInput>(slider)!.IsPressed);
            touch.Move(6, rangeEnd);
            AssertTrue(range.Value is >= 70 and <= 80);
            touch.Cancel(6, rangeEnd);
            AssertFalse(shell.Tree.GetComponent<XsrUiInput>(slider)!.IsPressed);
            AssertTrue(first.Captured is null);

            // After its owner is cancelled, a fresh primary contact remains usable.
            first = touch.Press(8, buttonPoint);
            touch.Release(8, buttonPoint);
            AssertEqual(1, intents.Count);
            AssertEqual(button, intents.Drain()[0].Source);
            AssertTrue(first.Captured is null);

            // Releasing the admitted pointer does not turn an already held second contact into a click.
            first = touch.Press(9, buttonPoint);
            second = touch.Press(10, buttonPoint);
            touch.Release(9, buttonPoint);
            AssertEqual(1, intents.Count);
            intents.Drain();
            touch.Move(10, rangeEnd);
            touch.Release(10, buttonPoint);
            AssertEqual(0, intents.Count);
            AssertTrue(first.Captured is null);
            AssertTrue(second.Captured is null);

            first = touch.Press(11, rangeStart);
            window.Close();
            AssertTrue(first.Captured is null);
            AssertFalse(shell.Tree.GetComponent<XsrUiInput>(slider)!.IsPressed);
            Console.WriteLine("PASS: secondary touch move, release and capture loss preserve the admitted pointer gesture");

            XsrUiPoint PointIn(XsrUiEntityId entity, double x, double y)
            {
                XsrUiRect rect = Node(surface.Scene!, entity).Rect;
                return new(rect.X + rect.Width * x, rect.Y + rect.Height * y);
            }
        }
        finally { window.Close(); }

        XsrUiEntityId Leaf(string key, double width, double height)
        {
            XsrUiEntityId entity = shell.Tree.Create(key);
            shell.Tree.SetComponent(entity, new XsrUiElement
            {
                Width = width,
                Height = height,
                HorizontalAlignment = XsrUiAlignment.Start,
            });
            shell.Tree.Attach(entity, page);
            return entity;
        }
    }

    /// <summary>Injects platform events through Avalonia's real hit test and TouchDevice, without mouse emulation.</summary>
    private sealed class RawTouchDriver : IDisposable
    {
        private readonly AvaloniaUiShellWindow _window;
        private readonly AvaloniaUiSceneSurface _surface;
        private readonly TouchDevice _device = CreateTouchDevice();
        private readonly Dictionary<long, IPointer> _pointers = [];
        private readonly IInputRoot _root;
        private long _pressId;
        private ulong _timestamp = 1000;

        internal RawTouchDriver(AvaloniaUiShellWindow window, AvaloniaUiSceneSurface surface)
        {
            _window = window;
            _surface = surface;
            DrainInputAndRender();
            IInputRoot? root = null;
            Control probe = new() { IsHitTestVisible = false };
            probe.AttachedToVisualTree += (_, args) => root = args.PresentationSource as IInputRoot;
            surface.Children.Add(probe);
            surface.Children.Remove(probe);
            _root = root ?? throw new InvalidOperationException("Touch fixture has no attached native input root.");
            window.AddHandler(InputElement.PointerPressedEvent, ObservePress, handledEventsToo: true);
        }

        internal IPointer Press(long id, XsrUiPoint point)
        {
            _pressId = id;
            Send(id, RawPointerEventType.TouchBegin, point);
            return _pointers.TryGetValue(id, out IPointer? pointer) ? pointer
                : throw new InvalidOperationException($"Raw touch {id} did not reach the native window at {point}.");
        }

        internal void Move(long id, XsrUiPoint point) => Send(id, RawPointerEventType.TouchUpdate, point);
        internal void Release(long id, XsrUiPoint point) => Send(id, RawPointerEventType.TouchEnd, point);
        internal void Cancel(long id, XsrUiPoint point) => Send(id, RawPointerEventType.TouchCancel, point);

        private void Send(long id, RawPointerEventType kind, XsrUiPoint point)
        {
            DrainInputAndRender();
            Point native = _surface.TranslatePoint(new Point(point.X, point.Y), _window)
                ?? throw new InvalidOperationException("Touch fixture is detached from its native window.");
            _timestamp += 20;
            GetPlatformInput(_window.PlatformImpl!)!(CreateTouchEvent(_device, _timestamp, _root, kind,
                native, RawInputModifiers.None, id));
            DrainInputAndRender();
        }

        private static void DrainInputAndRender()
        {
            for (int index = 0; index < 10; index++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                if (!Dispatcher.UIThread.HasJobsWithPriority(DispatcherPriority.SystemIdle)) return;
            }
            Dispatcher.UIThread.RunJobs();
        }

        private void ObservePress(object? sender, PointerPressedEventArgs args)
        {
            if (args.Pointer.Type == PointerType.Touch) _pointers[_pressId] = args.Pointer;
        }

        public void Dispose()
        {
            DisposeTouchDevice(_device);
            _window.RemoveHandler(InputElement.PointerPressedEvent, ObservePress);
        }

        // Avalonia intentionally omits platform-only raw input constructors from its public
        // reference assembly. Static accessors keep these real-input tests NativeAOT-safe.
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        private static extern TouchDevice CreateTouchDevice();

        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        private static extern RawTouchEventArgs CreateTouchEvent(IInputDevice device, ulong timestamp,
            IInputRoot root, RawPointerEventType type, Point position, RawInputModifiers modifiers, long id);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Input")]
        private static extern Action<RawInputEventArgs>? GetPlatformInput(ITopLevelImpl impl);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Dispose")]
        private static extern void DisposeTouchDevice(TouchDevice device);
    }
}
