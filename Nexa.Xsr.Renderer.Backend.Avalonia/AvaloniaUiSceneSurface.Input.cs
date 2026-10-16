using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiSceneSurface
{
    private IPointer? _pointerOwner;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_disposed || e.Handled) return;
        if (_fileDragActive || _pointerOwner is not null && !ReferenceEquals(_pointerOwner, e.Pointer))
        {
            e.Handled = true;
            return;
        }
        Point position = e.GetPosition(this);
        XsrUiPoint point = new(position.X, position.Y);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (ReleasePointerOwnership()) CommitScene();
            e.Handled = ShowContextMenu(point);
            return;
        }
        if (!AvaloniaUiPointerPolicy.IsPrimaryPress(e, this)) return;
        _contextMenu?.Close();
        bool handled = _shell.Renderer.PointerPressed(point);
        bool scrollbarGesture = _shell.Renderer.IsScrollbarGestureActive;
        if (scrollbarGesture) { _fileDragPress = null; _textSelecting = default; }
        else TrackFileDrag(e, position);
        CommitScene();
        if (!scrollbarGesture) BeginTextSelection(position, e.ClickCount);
        if (handled || _textSelecting.IsAssigned || _fileDragPress is not null)
        {
            _pointerOwner = e.Pointer;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (IsTitleBarPoint(point))
        {
            TitleBarDragRequested?.Invoke(this, e);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_disposed || e.Handled || !ReferenceEquals(_pointerOwner, e.Pointer)) return;
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        _fileDragPress = null;
        if (_fileDragActive)
        {
            _fileDragReleased = true;
            if (ReleasePointerOwnership()) CommitScene();
            e.Handled = true;
            return;
        }
        // Native capture loss is synchronous. Retire ownership before releasing capture so
        // the completed release cannot cancel a fresh gesture started by an emitted intent.
        _pointerOwner = null;
        e.Pointer.Capture(null);
        Point position = e.GetPosition(this);
        XsrUiClickModifiers modifiers = XsrUiClickModifiers.None;
        if (e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) modifiers |= XsrUiClickModifiers.Toggle;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= XsrUiClickModifiers.Extend;
        bool handled = _shell.Renderer.PointerReleased(new XsrUiPoint(position.X, position.Y), modifiers);
        handled |= _textSelecting.IsAssigned;
        _textSelecting = default;
        RetireTouchHover(e.Pointer);
        CommitScene();
        e.Handled = handled;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_disposed || e.Handled || !CanRoutePointerMove(e.Pointer)) return;
        Point position = e.GetPosition(this);
        _pointerInside = true;
        if (MoveFileDrag(e, position)) { e.Handled = true; return; }
        _lastPointerPoint = new XsrUiPoint(position.X, position.Y);
        UpdatePointerCursor(_lastPointerPoint);
        if (ExtendTextSelection(position)) return;
        if (_shell.Renderer.PointerMoved(_lastPointerPoint))
        {
            CommitScene();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_disposed || !CanRoutePointerMove(e.Pointer)) return;
        if (e.Pointer.Captured == this) return;
        _pointerInside = false;
        SetPointerCursor(XsrUiPointerCursor.Default);
        if (_shell.Renderer.PointerMoved(new XsrUiPoint(-1, -1)))
        {
            CommitScene();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_disposed || !ReferenceEquals(_pointerOwner, e.Pointer)) return;
        // Starting the OS file drag releases capture itself. Retain its owner until mouse-up
        // or drag completion, so storage preparation cannot start a drag after release.
        if (!_fileDragActive) _pointerOwner = null;
        _fileDragPress = null;
        _textSelecting = default;
        bool changed = _shell.Renderer.CancelPointerGesture();
        changed |= RetireTouchHover(e.Pointer);
        if (changed) CommitScene();
    }

    private bool CanRoutePointerMove(IPointer pointer) => _pointerOwner is not null
        ? ReferenceEquals(_pointerOwner, pointer)
        : pointer.Type != PointerType.Touch;

    private bool ReleasePointerOwnership()
    {
        IPointer? owner = _pointerOwner;
        _pointerOwner = null;
        _fileDragPress = null;
        _textSelecting = default;
        bool changed = _shell.Renderer.CancelPointerGesture();
        if (owner?.Captured == this) owner.Capture(null);
        changed |= RetireTouchHover(owner);
        return changed;
    }

    private bool RetireTouchHover(IPointer? pointer)
    {
        if (pointer?.Type != PointerType.Touch) return false;
        _pointerInside = false;
        SetPointerCursor(XsrUiPointerCursor.Default);
        return _shell.Renderer.PointerMoved(new(-1, -1));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_disposed || _pointerOwner is not null && !ReferenceEquals(_pointerOwner, e.Pointer)) return;
        Point position = e.GetPosition(this);
        if (_shell.Renderer.PointerScroll(
                new XsrUiPoint(position.X, position.Y),
                -e.Delta.Y * AvaloniaMotionTokens.ContinuousScrollPixelsPerWheelUnit,
                -e.Delta.X * AvaloniaMotionTokens.ContinuousScrollPixelsPerWheelUnit))
        {
            CommitScene();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        { e.Handled = ShowContextMenu(default, keyboard: true); return; }
        if (e.Key == Key.Escape && (_fileDragActive || _fileDragPress is not null))
        {
            _fileDragReleased = true;
            ReleasePointerOwnership();
            CommitScene();
            e.Handled = true;
            return;
        }
        if (HandleTextEditingKey(e)) { e.Handled = true; return; }
        XsrUiKey? key = e.Key switch
        {
            Key.Tab => XsrUiKey.Tab,
            Key.Enter => XsrUiKey.Enter,
            Key.Space => XsrUiKey.Space,
            Key.Up => XsrUiKey.Up,
            Key.Down => XsrUiKey.Down,
            Key.Left => XsrUiKey.Left,
            Key.Right => XsrUiKey.Right,
            Key.Home => XsrUiKey.Home,
            Key.End => XsrUiKey.End,
            Key.Escape => XsrUiKey.Escape,
            _ => null,
        };
        if (key is { } routed && _shell.Renderer.HandleKey(routed))
        {
            CommitScene();
            e.Handled = true;
        }
    }
}
