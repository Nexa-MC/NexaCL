using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Nexa.UI.Next;
using Nexa.Xsr;

namespace Nexa.UI.Next.Backend.Avalonia;

internal sealed partial class AvaloniaUiSceneNodeControl
{

    /// <summary>
    /// Fades one member of a transition group without translating/clipping its text.
    /// Reduced motion applies the final state immediately.
    /// </summary>
    internal void RunEnterAnimation()
    {
        AvaloniaUiMotion.Cancel(this, "enter");
        if (_reducedMotion())
        {
            SetValue(EnterProgressProperty, 1);
            return;
        }

        // Keep rapid retargets continuous instead of flashing back to transparent.
        if (EnterProgress >= 1) SetValue(EnterProgressProperty, AvaloniaMotionTokens.PageEnterInitialOpacity);
        AvaloniaUiMotion.Animate(
            this,
            "enter",
            () => (double)GetValue(EnterProgressProperty)!,
            value => SetValue(EnterProgressProperty, value),
            1,
            AvaloniaMotionTokens.PageEnterMilliseconds,
            AvaloniaUiMotion.EaseOut,
            reducedMotion: _reducedMotion);
    }

    /// <summary>
    /// Materializes one in-window overlay from the currently presented value. Every member of
    /// the overlay subtree carries the same scene anchor, so the native flat control list still
    /// moves and scales as one physical group. A retarget preserves the current value/velocity.
    /// </summary>
    private void ApplyOverlayPresentation(XsrUiSceneNode previous, XsrUiSceneNode node)
    {
        if (node.OverlayMotion == XsrUiOverlayMotionKind.None)
        {
            AvaloniaUiMotion.Cancel(this, OverlayProgressProperty);
            AvaloniaUiMotion.Cancel(this, "notification-reflow");
            OverlayProgress = 1;
            _overlayScale.ScaleX = 1;
            _overlayScale.ScaleY = 1;
            _overlayTranslate.X = 0;
            _overlayTranslate.Y = 0;
            _reflowTranslate.X = 0;
            _reflowTranslate.Y = 0;
            return;
        }

        bool newTrack = !_applied || previous.OverlayMotion != node.OverlayMotion;
        if (newTrack)
        {
            OverlayProgress = node.IsOverlayClosing ? 1 : 0;
            AnimateOverlayProgress(node.IsOverlayClosing ? 0 : 1, node.OverlayMotion);
        }
        else if (previous.IsOverlayClosing != node.IsOverlayClosing)
        {
            AnimateOverlayProgress(node.IsOverlayClosing ? 0 : 1, node.OverlayMotion);
        }

        if (!newTrack
            && !node.IsOverlayClosing
            && node.OverlayMotion == XsrUiOverlayMotionKind.Notification
            && previous.OverlayAnchor is { } previousAnchor
            && node.OverlayAnchor is { } currentAnchor
            && Math.Abs(previousAnchor.Y - currentAnchor.Y) > .01)
        {
            _reflowTranslate.Y += previousAnchor.Y - currentAnchor.Y;
            AvaloniaUiMotion.AnimateSpring(
                this,
                "notification-reflow",
                () => _reflowTranslate.Y,
                value => _reflowTranslate.Y = value,
                0,
                AvaloniaMotionTokens.NotificationReflowSpringResponseSeconds,
                () => _reducedMotion(),
                readCurrentPosition: true);
        }

        ApplyOverlayTransform();
    }

    private void AnimateOverlayProgress(double target, XsrUiOverlayMotionKind kind)
    {
        if (kind == XsrUiOverlayMotionKind.DialogScrim)
        {
            AnimateFact(
                OverlayProgressProperty,
                target,
                target > OverlayProgress ? 180 : 150,
                target > OverlayProgress ? AvaloniaUiMotion.EaseOut : AvaloniaUiMotion.EaseIn);
            return;
        }

        double response = kind == XsrUiOverlayMotionKind.Dialog
            ? AvaloniaMotionTokens.DialogSpringResponseSeconds
            : AvaloniaMotionTokens.NotificationSpringResponseSeconds;
        AvaloniaUiMotion.AnimateSpring(
            this,
            OverlayProgressProperty,
            () => OverlayProgress,
            value => OverlayProgress = value,
            target,
            response,
            () => _reducedMotion(),
            readCurrentPosition: true);
    }

    private void ApplyOverlayTransform()
    {
        double progress = Math.Clamp(OverlayProgress, 0, 1);
        double startScale = _node.OverlayMotion switch
        {
            XsrUiOverlayMotionKind.Notification => .985,
            XsrUiOverlayMotionKind.Dialog => .97,
            _ => 1,
        };
        double scale = startScale + ((1 - startScale) * progress);
        _overlayScale.ScaleX = scale;
        _overlayScale.ScaleY = scale;

        double translateX = 0;
        double translateY = _node.OverlayMotion switch
        {
            XsrUiOverlayMotionKind.Notification => 10 * (1 - progress),
            XsrUiOverlayMotionKind.Dialog => 8 * (1 - progress),
            _ => 0,
        };
        if (_node.OverlayAnchor is { } anchor && scale != 1)
        {
            double anchorCenterX = anchor.X + (anchor.Width / 2);
            double anchorCenterY = anchor.Y + (anchor.Height / 2);
            double nodeCenterX = _node.Rect.X + (_node.Rect.Width / 2);
            double nodeCenterY = _node.Rect.Y + (_node.Rect.Height / 2);
            translateX += (anchorCenterX - nodeCenterX) * (1 - scale);
            translateY += (anchorCenterY - nodeCenterY) * (1 - scale);
        }

        _overlayTranslate.X = translateX;
        _overlayTranslate.Y = translateY;
    }

    /// <summary>
    /// Presents one scene-fact value, animating from its currently presented value. Reduced
    /// motion applies the fact immediately.
    /// </summary>
    private void AnimateFact(AvaloniaProperty property, double target, double durationMilliseconds, Func<double, double>? easing = null)
    {
        if (_reducedMotion())
        {
            AvaloniaUiMotion.Cancel(this, property);
            SetValue(property, target);
            return;
        }

        AvaloniaUiMotion.Animate(
            this,
            property,
            () => (double)GetValue(property)!,
            value => SetValue(property, value),
            target,
            durationMilliseconds,
            easing,
            reducedMotion: () => _reducedMotion());
    }

    private void SetPressScale(double scale)
    {
        if (_reducedMotion() || !_node.IsEnabled || scale < 1)
        {
            AvaloniaUiMotion.Cancel(this, ScaleTransform.ScaleXProperty);
            AvaloniaUiMotion.Cancel(this, ScaleTransform.ScaleYProperty);
            _pressScale.ScaleX = scale;
            _pressScale.ScaleY = scale;
            return;
        }

        AvaloniaUiMotion.Animate(
            this, ScaleTransform.ScaleXProperty, () => _pressScale.ScaleX, value => _pressScale.ScaleX = value,
            scale, AvaloniaMotionTokens.PressMilliseconds,
            reducedMotion: () => _reducedMotion());
        AvaloniaUiMotion.Animate(
            this, ScaleTransform.ScaleYProperty, () => _pressScale.ScaleY, value => _pressScale.ScaleY = value,
            scale, AvaloniaMotionTokens.PressMilliseconds,
            reducedMotion: () => _reducedMotion());
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == HoverOpacityProperty
            || e.Property == PillScaleProperty
            || e.Property == ToggleProgressProperty
            || e.Property == EnterProgressProperty
            || e.Property == OverlayProgressProperty)
        {
            if (e.Property == OverlayProgressProperty)
            {
                ApplyOverlayTransform();
            }

            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopCaret();
        AvaloniaUiMotion.CancelAll(this);
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        if (_node.TextInput is not null) return new AvaloniaUiSceneTextAutomationPeer(this);
        if (_node.Role is XsrUiSemanticRole.Switch or XsrUiSemanticRole.CheckBox)
            return new AvaloniaUiSceneToggleAutomationPeer(this);
        if (_node.Role is XsrUiSemanticRole.Navigation or XsrUiSemanticRole.RadioGroup)
        {
            return new AvaloniaUiSceneNavigationAutomationPeer(this);
        }

        if (_node.Role is XsrUiSemanticRole.NavigationItem or XsrUiSemanticRole.RadioButton
            && _node.IsClickable
            && _selectionContainer is not null)
        {
            return new AvaloniaUiSceneNavigationItemAutomationPeer(this);
        }

        return _node.Role == XsrUiSemanticRole.Button || _node.IsClickable
            ? new AvaloniaUiSceneInvokeAutomationPeer(this)
            : new AvaloniaUiSceneNodeAutomationPeer(this);
    }
}
