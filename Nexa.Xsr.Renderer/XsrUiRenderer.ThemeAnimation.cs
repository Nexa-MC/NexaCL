namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiRenderer
{
    private readonly Dictionary<XsrUiEntityId, ThemeAnimationClock> _themeAnimationClocks = [];
    private readonly HashSet<XsrUiEntityId> _themeAnimationVisible = [];
    private sealed record ThemeAnimationClock(DateTimeOffset Started, DateTimeOffset LastRendered, long PreparedFrame,
        IReadOnlyList<XsrUiStyleAnimation> Animations);

    private DateTimeOffset PrepareThemeAnimationClock(XsrUiEntityId entity)
    {
        var animations = GetThemeStyle(entity)?.Style.Animations ?? [];
        DateTimeOffset now = _gestureTime.GetUtcNow();
        if (!_themeAnimationClocks.TryGetValue(entity, out var clock)
            || !SameThemeAnimations(clock.Animations, animations)
            || !_themeAnimationVisible.Contains(entity) && clock.PreparedFrame != _sceneVersion + 1)
        {
            clock = new(now, now, _sceneVersion + 1, animations);
            _themeAnimationClocks[entity] = clock;
        }
        return clock.Started;
    }

    private static bool StyleAnimationChangesValue(XsrUiStyleAnimation animation)
    {
        double first = animation.Keyframes[0].Value;
        for (int index = 1; index < animation.Keyframes.Count; index++)
            if (animation.Keyframes[index].Value != first) return true;
        return false;
    }

    private static bool SameThemeAnimations(IReadOnlyList<XsrUiStyleAnimation> first, IReadOnlyList<XsrUiStyleAnimation> second)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first.Count != second.Count) return false;
        for (int index = 0; index < first.Count; index++)
        {
            var left = first[index]; var right = second[index];
            if (left.Property != right.Property || left.Duration != right.Duration || left.Repeat != right.Repeat
                || left.Alternate != right.Alternate || !left.Keyframes.SequenceEqual(right.Keyframes)) return false;
        }
        return true;
    }

    private void RecordThemeAnimationScene(IReadOnlyList<XsrUiSceneNode> nodes)
    {
        _themeAnimationVisible.Clear();
        DateTimeOffset now = _gestureTime.GetUtcNow();
        foreach (var node in nodes)
        {
            if (node.Rect.Width <= 0 || node.Rect.Height <= 0 || !_themeAnimationClocks.TryGetValue(node.Entity, out var clock)) continue;
            _themeAnimationVisible.Add(node.Entity);
            _themeAnimationClocks[node.Entity] = clock with { LastRendered = now };
        }
        foreach (var dead in _themeAnimationClocks.Keys.Where(entity => !_tree.IsAlive(entity)).ToArray())
            _themeAnimationClocks.Remove(dead);
    }

    private XsrUiVisualStyleSnapshot ProjectThemeStyle(XsrUiEntityId entity)
    {
        ThemeStyleEntry? themed = GetThemeStyle(entity);
        if (themed is null) return ProjectStyle(_tree.GetComponent<XsrUiVisualStyle>(entity));
        // Host compatibility colors may inherit the built-in palette. Explicit third-party colors are literal.
        XsrUiComputedStyle style = themed.Style;
        if (!HasThemeVisualOverrides(style)) return ProjectStyle(_tree.GetComponent<XsrUiVisualStyle>(entity));
        XsrUiVisualStyleSnapshot source = _colorScheme.Project(_tree.GetComponent<XsrUiVisualStyle>(entity)?.Snapshot()
            ?? new XsrUiVisualStyle().Snapshot());
        XsrUiVisualStyleSnapshot visual = themed.Visual.Snapshot() with
        {
            Background = style.Background ?? source.Background,
            Foreground = style.Foreground ?? source.Foreground,
            Border = style.Border ?? source.Border,
            Hover = style.Hover ?? source.Hover,
            IsComputed = true,
        };
        foreach (XsrUiStyleAnimation animation in style.Animations ?? [])
        {
            double value = SampleStyleAnimation(entity, animation);
            visual = animation.Property switch
            {
                XsrUiStyleAnimationProperty.Opacity => visual with { Opacity = Math.Clamp(value, 0, 1) },
                XsrUiStyleAnimationProperty.CornerRadius => visual with { CornerRadius = Math.Max(0, value) },
                XsrUiStyleAnimationProperty.BlurRadius => visual with { BlurRadius = Math.Max(0, value) },
                _ => visual,
            };
        }
        return visual;
    }

    private static bool HasThemeVisualOverrides(XsrUiComputedStyle style) =>
        style.Background is not null || style.Foreground is not null || style.Border is not null || style.Hover is not null
        || style.Surface is not null || style.Opacity is not null || style.CornerRadius is not null || style.BorderWidth is not null
        || style.BlurRadius is not null || style.FontSize is not null || style.FontWeight is not null
        || style.TextAlignment is not null || style.WrapText is not null
        || style.Animations?.Any(animation => animation.Property is XsrUiStyleAnimationProperty.Opacity
            or XsrUiStyleAnimationProperty.CornerRadius or XsrUiStyleAnimationProperty.BlurRadius) == true;

    private XsrUiSceneTransform? ThemeTransform(XsrUiEntityId entity, XsrUiRect rect)
    {
        XsrUiComputedStyle? style = GetThemeStyle(entity)?.Style;
        XsrUiStyleTransform transform = style?.Transform ?? new(ScaleX: 1, ScaleY: 1);
        bool defined = style?.Transform is not null;
        foreach (XsrUiStyleAnimation animation in style?.Animations ?? [])
        {
            double value = SampleStyleAnimation(entity, animation);
            XsrUiStyleTransform next = animation.Property switch
            {
                XsrUiStyleAnimationProperty.TranslateX => transform with { TranslateX = value },
                XsrUiStyleAnimationProperty.TranslateY => transform with { TranslateY = value },
                XsrUiStyleAnimationProperty.ScaleX => transform with { ScaleX = Math.Clamp(value, .001, 100) },
                XsrUiStyleAnimationProperty.ScaleY => transform with { ScaleY = Math.Clamp(value, .001, 100) },
                XsrUiStyleAnimationProperty.Rotation => transform with { Rotation = value },
                _ => transform,
            };
            if (next != transform) defined = true;
            transform = next;
        }
        if (!defined) return null;
        double radians = transform.Rotation * Math.PI / 180;
        double cosine = Math.Cos(radians), sine = Math.Sin(radians);
        double m11 = transform.ScaleX * cosine, m12 = transform.ScaleX * sine;
        double m21 = -transform.ScaleY * sine, m22 = transform.ScaleY * cosine;
        double pivotX = rect.X + rect.Width / 2, pivotY = rect.Y + rect.Height / 2;
        return new(m11, m12, m21, m22,
            pivotX + transform.TranslateX - pivotX * m11 - pivotY * m21,
            pivotY + transform.TranslateY - pivotX * m12 - pivotY * m22);
    }

    private double SampleStyleAnimation(XsrUiEntityId entity, XsrUiStyleAnimation animation)
    {
        if (EffectiveReducedMotion) return animation.Keyframes[^1].Value;
        double cycles = Math.Max(0, (_gestureTime.GetUtcNow() - PrepareThemeAnimationClock(entity)).TotalMilliseconds / animation.Duration.TotalMilliseconds);
        double progress = animation.Repeat ? cycles % 1 : Math.Min(1, cycles);
        if (animation.Repeat && animation.Alternate && ((long)Math.Floor(cycles) & 1) != 0) progress = 1 - progress;
        IReadOnlyList<XsrUiStyleKeyframe> frames = animation.Keyframes;
        for (int index = 1; index < frames.Count; index++)
        {
            if (progress > frames[index].Position) continue;
            XsrUiStyleKeyframe left = frames[index - 1], right = frames[index];
            double relative = (progress - left.Position) / (right.Position - left.Position);
            return left.Value + (right.Value - left.Value) * relative;
        }
        return frames[^1].Value;
    }
}
