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

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void VerifyNativeWindowPointerRouting(
        AvaloniaUiShellWindow window, XsrUiShell shell, AvaloniaUiSceneSurface surface)
    {
        WindowState previousState = window.WindowState;
        bool previousMotion = shell.Renderer.ReducedMotion;
        XsrUiEntityId previousFocus = shell.Renderer.Focused;
        bool previousFocusVisible = previousFocus.IsAssigned && shell.Tree.IsAlive(previousFocus)
            && shell.Tree.GetComponent<XsrUiInput>(previousFocus)?.IsFocusVisible == true;
        XsrUiEntityId back = shell.Tree.Create("native-title-back"), edit = shell.Tree.Create("native-title-edit"),
            overlay = shell.Tree.Create("native-title-cover");
        var requested = new List<PointerType>();
        var requestedClicks = new List<int>();
        var requestedStates = new List<WindowState>();
        void OnDrag(object? sender, PointerPressedEventArgs args)
        {
            requested.Add(args.Pointer.Type);
            requestedClicks.Add(args.ClickCount);
            requestedStates.Add(window.WindowState);
        }
        Point Native(XsrUiPoint point) => surface.TranslatePoint(new Point(point.X, point.Y), window)!.Value;
        XsrUiPoint Center(XsrUiEntityId entity)
        {
            XsrUiRect rect = Node(surface.Scene!, entity).Rect;
            return new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        }
        XsrUiPoint BareTitle()
        {
            XsrUiRect rect = Node(surface.Scene!, shell.TitleBar).Rect;
            // Keep clear of both the scene's title children and the native window buttons.
            return new(rect.X + rect.Width - 180, rect.Y + rect.Height / 2);
        }
        void Commit()
        {
            surface.CommitScene();
            window.UpdateLayout();
        }
        void MouseClick(XsrUiPoint point)
        {
            window.MouseDown(Native(point), MouseButton.Left);
            window.MouseUp(Native(point), MouseButton.Left);
        }
        using RawTouchDriver touch = new(window, surface);
        void TouchTap(long id, XsrUiPoint point)
        {
            touch.Press(id, point);
            touch.Release(id, point);
        }
        surface.TitleBarDragRequested += OnDrag;
        try
        {
            shell.Renderer.ReducedMotion = true;
            window.WindowState = WindowState.Normal;
            shell.Tree.SetComponent(back, new XsrUiElement { Width = 40, Height = 30, VerticalAlignment = XsrUiAlignment.Center });
            shell.Tree.SetComponent(back, new XsrUiSemantic(XsrUiSemanticRole.Button, "返回"));
            shell.Tree.SetComponent(back, new XsrUiInput { Clickable = true, Focusable = true });
            shell.Tree.SetComponent(back, new XsrUiCommandBinding(XsrSemanticId.Parse("test.title.back")));
            shell.Tree.SetComponent(back, new XsrUiText("返回"));
            shell.Tree.Attach(back, shell.TitleBar);
            shell.Tree.SetComponent(edit, new XsrUiElement { Width = 100, Height = 30, VerticalAlignment = XsrUiAlignment.Center });
            shell.Tree.SetComponent(edit, new XsrUiSemantic(XsrUiSemanticRole.TextInput, "标题栏输入"));
            shell.Tree.SetComponent(edit, new XsrUiInput { Focusable = true });
            shell.Tree.SetComponent(edit, new XsrUiTextInput());
            shell.Tree.Attach(edit, shell.TitleBar);
            Commit();

            MouseClick(BareTitle());
            AssertEqual(1, requested.Count);
            AssertEqual(PointerType.Mouse, requested[^1]);
            TouchTap(1, BareTitle());
            AssertEqual(2, requested.Count);
            AssertEqual(PointerType.Touch, requested[^1]);

            int before = requested.Count;
            MouseClick(Center(back));
            TouchTap(2, Center(back));
            MouseClick(Center(edit));
            TouchTap(3, Center(edit));
            AssertEqual(before, requested.Count);
            AssertEqual(edit, shell.Renderer.Focused);

            // A second finger must not enter Win32 BeginMoveDrag, which rejects a
            // non-primary pointer asynchronously, or replace the first contact's gesture.
            XsrUiPoint title = BareTitle();
            touch.Press(4, title);
            AssertEqual(++before, requested.Count);
            touch.Press(5, title with { X = title.X - 12 });
            AssertEqual(before, requested.Count);
            touch.Release(5, title with { X = title.X - 12 });
            touch.Release(4, title);

            shell.Tree.SetComponent(overlay, new XsrUiElement());
            shell.Tree.SetComponent(overlay, new XsrUiVisualStyle { Background = XsrUiColor.FromRgb(30, 40, 50) });
            shell.Stage.Show(overlay);
            Commit();
            MouseClick(BareTitle());
            TouchTap(6, BareTitle());
            AssertEqual(before, requested.Count);

            // A transparent non-modal composition layer passes through. Making the same
            // full-window layer modal suppresses the title and must block native dragging.
            shell.Tree.GetComponent<XsrUiVisualStyle>(overlay)!.Background = new XsrUiColor(0, 0, 0, 0);
            shell.Tree.MarkDirty(overlay, XsrUiDirtyKinds.Paint);
            Commit();
            MouseClick(BareTitle());
            AssertEqual(++before, requested.Count);
            shell.Stage.Dismiss(overlay);
            shell.Stage.Show(overlay, modal: true);
            Commit();
            MouseClick(BareTitle());
            TouchTap(7, BareTitle());
            AssertEqual(before, requested.Count);
            shell.Stage.Dismiss(overlay);
            Commit();

            // The preceding opaque/transparent overlay presses share a caption position.
            // Their real MouseDevice sequence may already have maximized the window when
            // the caption becomes exposed. Establish this case's own initial state.
            window.WindowState = WindowState.Normal;
            Commit();
            AssertEqual(WindowState.Normal, window.WindowState);
            // Headless MouseDown/Up drains and renders between calls. On slower CI machines
            // those frames can exceed the OS double-click interval. Raw timestamps keep the
            // two real MouseDevice presses deterministic without manufacturing ClickCount.
            title = BareTitle() with { X = BareTitle().X - 30 };
            AssertTrue(window.CanResize);
            AssertTrue(surface.IsNativeTitleBarPoint(title));
            int doubleClickStart = requested.Count;
            DoubleClickWithRawMouse(window, surface, title);
            if (requested.Count != doubleClickStart + 2 || requestedClicks[^2] != 1 || requestedClicks[^1] != 2)
                throw new InvalidOperationException($"Native double-click fixture received [{string.Join(',', requestedClicks.Skip(doubleClickStart))}] "
                    + $"at {title}; resize={window.CanResize}, caption={surface.IsNativeTitleBarPoint(title)}, state={window.WindowState}.");
            // macOS delegates the user's double-click preference to the actual OS. Its
            // headless platform cannot turn that native preference into a maximize action.
            WindowState expectedState = OperatingSystem.IsMacOS() ? WindowState.Normal : WindowState.Maximized;
            if (window.WindowState != expectedState)
                throw new InvalidOperationException($"Native double-click expected {expectedState}, got {window.WindowState}; "
                    + $"states after caption presses: [{string.Join(',', requestedStates.Skip(doubleClickStart))}].");
            window.WindowState = WindowState.Normal;
            Commit();
        }
        finally
        {
            surface.TitleBarDragRequested -= OnDrag;
            shell.Stage.Dismiss(overlay);
            shell.Tree.Destroy(overlay);
            shell.Tree.Destroy(back);
            shell.Tree.Destroy(edit);
            window.WindowState = previousState;
            shell.Renderer.ReducedMotion = previousMotion;
            if (previousFocus.IsAssigned && shell.Tree.IsAlive(previousFocus))
                _ = shell.Renderer.Focus(previousFocus, previousFocusVisible);
            window.Activate();
            Commit();
        }
        Console.WriteLine("PASS: native mouse/touch title drag, primary contact, interactive/overlay exclusion and double-click maximize");
    }

    private static void DoubleClickWithRawMouse(
        AvaloniaUiShellWindow window, AvaloniaUiSceneSurface surface, XsrUiPoint point)
    {
        IInputRoot? root = null;
        Control probe = new() { IsHitTestVisible = false };
        probe.AttachedToVisualTree += (_, args) => root = args.PresentationSource as IInputRoot;
        surface.Children.Add(probe);
        surface.Children.Remove(probe);
        IInputRoot inputRoot = root ?? throw new InvalidOperationException("Mouse fixture has no native input root.");
        Action<RawInputEventArgs> input = GetWindowMouseInput(window.PlatformImpl!)
            ?? throw new InvalidOperationException("Mouse fixture has no platform input entry.");
        Point native = surface.TranslatePoint(new Point(point.X, point.Y), window)
            ?? throw new InvalidOperationException("Mouse fixture is detached from its native window.");
        MouseDevice device = CreateWindowMouseDevice(null);
        try
        {
            ulong timestamp = 1_000;
            for (int click = 0; click < 2; click++)
            {
                Send(RawPointerEventType.LeftButtonDown);
                Send(RawPointerEventType.LeftButtonUp);
            }

            void Send(RawPointerEventType kind)
            {
                input(CreateWindowMouseEvent(device, timestamp, inputRoot, kind, native, RawInputModifiers.None));
                timestamp += 20;
                for (int index = 0; index < 10; index++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    if (!Dispatcher.UIThread.HasJobsWithPriority(DispatcherPriority.SystemIdle)) return;
                }
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally { DisposeWindowMouseDevice(device); }
    }

    // Platform-only raw constructors are hidden in Avalonia's reference assembly. Static
    // accessors exercise its real input pipeline while remaining compatible with NativeAOT.
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern MouseDevice CreateWindowMouseDevice(Pointer? pointer);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern RawPointerEventArgs CreateWindowMouseEvent(IInputDevice device, ulong timestamp,
        IInputRoot root, RawPointerEventType type, Point position, RawInputModifiers modifiers);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Input")]
    private static extern Action<RawInputEventArgs>? GetWindowMouseInput(ITopLevelImpl impl);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Dispose")]
    private static extern void DisposeWindowMouseDevice(MouseDevice device);
}
