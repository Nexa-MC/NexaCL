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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled) return;
        Point position = e.GetPosition(this);
        XsrUiPoint point = new(position.X, position.Y);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            e.Handled = ShowContextMenu(point);
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _contextMenu?.Close();
        bool handled = _shell.Renderer.PointerPressed(point);
        bool scrollbarGesture = _shell.Renderer.IsScrollbarGestureActive;
        if (scrollbarGesture) { _fileDragPress = null; _textSelecting = default; }
        else TrackFileDrag(e, position);
        CommitScene();
        if (!scrollbarGesture) BeginTextSelection(position, e.ClickCount);
        if (handled)
        {
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
        if (e.Handled) return;
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        _fileDragPress = null;
        if (_fileDragActive) { _fileDragReleased = true; e.Handled = true; return; }
        Point position = e.GetPosition(this);
        XsrUiClickModifiers modifiers = XsrUiClickModifiers.None;
        if (e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) modifiers |= XsrUiClickModifiers.Toggle;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= XsrUiClickModifiers.Extend;
        bool handled = _shell.Renderer.PointerReleased(new XsrUiPoint(position.X, position.Y), modifiers);
        handled |= _textSelecting.IsAssigned;
        _textSelecting = default;
        e.Pointer.Capture(null);
        CommitScene();
        e.Handled = handled;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
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
        _fileDragPress = null;
        _textSelecting = default;
        if (_shell.Renderer.CancelPointerGesture()) CommitScene();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
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
            _fileDragPress = null;
            _shell.Renderer.CancelPointerGesture();
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
