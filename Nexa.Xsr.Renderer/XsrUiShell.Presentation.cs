using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiShell
{
    private readonly Dictionary<XsrUiEntityId, (double Size, double Weight)> _declaredTitleTypography = [];

    private void CaptureDeclaredTitleTypography()
    {
        Tree.Walk(TitleBar, entity =>
        {
            if (Tree.GetComponent<XsrUiText>(entity) is not null && Tree.GetComponent<XsrUiVisualStyle>(entity) is { } visual)
                _declaredTitleTypography[entity] = (visual.FontSize, visual.FontWeight);
            return true;
        });
    }

    /// <summary>
    /// Shared composition tail for both construction paths: the shell-owned rail toggle, the
    /// palette, the canonical item margins, and the collapsed rail presentation.
    /// </summary>
    private void FinishComposition()
    {
        // PXML omits default-only layout components. Native window metrics need these
        // layouts in both the compiled-template and programmatic construction paths.
        if (Tree.GetComponent<XsrUiElement>(Root) is null) Tree.SetComponent(Root, new XsrUiElement());
        if (Tree.GetComponent<XsrUiElement>(TitleBar) is null) Tree.SetComponent(TitleBar, new XsrUiElement());
        XsrUiEntityId[] titleChildren = [.. Tree.Children(TitleBar)];
        _titleBarSafeArea = Tree.Create("title-bar-native-safe-area");
        Tree.SetComponent(_titleBarSafeArea, new XsrUiElement { Width = 0 });
        Tree.Attach(_titleBarSafeArea, TitleBar);
        foreach (var child in titleChildren) Tree.Attach(child, TitleBar);
        _navigationToggle = CreateNavigationToggle();
        ApplyNavigationItemMargins();
        ApplyPalette();
        ApplyNavigationExpansion();
        ApplyRailPresentation();
    }

    /// <summary>
    /// Commits the canonical static item margins once: full rail width in both states, with the
    /// first item inset from the title bar. Expansion animates the rail width, never margins.
    /// </summary>
    private void ApplyNavigationItemMargins()
    {
        for (int index = 0; index < NavigationItems.Count; index++)
        {
            XsrUiEntityId entity = _navigationEntities[NavigationItems[index].Id];
            XsrUiElement? element = Tree.GetComponent<XsrUiElement>(entity);
            XsrUiThickness margin = ItemMargin(index);
            if (element is not null && element.Margin != margin)
            {
                element.Margin = margin;
                Tree.MarkDirty(entity, XsrUiDirtyKinds.Layout);
            }
        }
    }

    private XsrUiEntityId CreateNavigationToggle()
    {
        XsrUiEntityId toggle = Tree.Create("navigation-toggle");
        Tree.SetComponent(toggle, new XsrUiElement
        {
            Height = NavigationItemHeight,

            // The rail stretches its last child over the leftover height; end alignment pins the
            // toggle to the bottom of the rail like the legacy hamburger.
            VerticalAlignment = XsrUiAlignment.End,
            Margin = new XsrUiThickness(0, 6, 0, RailBottomInset),
            HorizontalAlignment = XsrUiAlignment.Stretch,
        });
        Tree.SetComponent(toggle, new XsrUiImage("lucide/menu"));
        Tree.SetComponent(toggle, new XsrUiText(string.Empty));
        Tree.SetComponent(toggle, new XsrUiSemantic(XsrUiSemanticRole.Button, "导航开关"));
        Tree.SetComponent(toggle, new XsrUiInput { Focusable = true, Clickable = true });
        Tree.SetComponent(toggle, new XsrUiCommandBinding(XsrUiShellIds.NavigationExpand));
        Tree.SetComponent(toggle, new XsrUiVisualStyle());
        Tree.Attach(toggle, Navigation);
        return toggle;
    }

    private static XsrUiThickness ItemMargin(int index)
    {
        // Rail items always span the full rail width: the selection pill stays pinned to the
        // rail's left edge and the icon/label offsets come from the backend presentation, so
        // expansion never shifts the chrome sideways.
        double top = index == 0 ? RailTopInset : 0;
        return new XsrUiThickness(0, top, 0, 0);
    }

    /// <summary>
    /// Re-commits the rail geometry from the ephemeral presentation progress. Width is the only
    /// animated fact: item labels and the collapse label are target-time decisions committed by
    /// <see cref="ApplyNavigationExpansion"/>.
    /// </summary>
    private void ApplyRailPresentation()
    {
        XsrUiElement? rail = Tree.GetComponent<XsrUiElement>(Navigation);
        if (rail is null)
        {
            return;
        }

        double width = RailWidthFor(RailPresentationProgress);
        if (rail.Width != width)
        {
            rail.Width = width;
            Tree.MarkDirty(Navigation, XsrUiDirtyKinds.Layout);
        }
    }

    /// <summary>
    /// Commits the target-time presentation facts of the rail state: the toggle carries the
    /// collapse label ("收起") while expanded and collapses to icon-only. Item labels are always
    /// the destination text; whether the backend draws them beside the icon follows the
    /// committed item width, which passes the label-reveal threshold as the rail animates.
    /// </summary>
    private void ApplyNavigationExpansion()
    {
        if (_navigationToggle != default
            && Tree.GetComponent<XsrUiText>(_navigationToggle) is { } toggleText)
        {
            // Like destination labels, this is revealed by committed row width, not target time.
            string content = NavigationToggleCollapseLabel;
            if (!string.Equals(toggleText.Content, content, StringComparison.Ordinal))
            {
                toggleText.Content = content;
                Tree.MarkDirty(_navigationToggle, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
            }
        }
    }

    private void ApplyPalette()
    {
        ApplyVisual(
            Root,
            Palette.WindowBackground,
            Palette.PrimaryText,
            XsrUiColor.Transparent,
            XsrUiColor.Transparent,
            XsrUiSurfaceKind.Solid,
            cornerRadius: 0,
            blurRadius: 0,
            borderWidth: 0);
        ApplyVisual(
            TitleBar,
            Palette.TitleBarBackground,
            Palette.TitleBarText,
            Palette.SurfaceBorder,
            XsrUiColor.Transparent,
            Palette.TitleBarSurface,
            0,
            Palette.BlurRadius,
            Palette.BorderWidth);
        ApplyVisual(
            Navigation,
            Palette.NavigationBackground,
            Palette.PrimaryText,
            Palette.SurfaceBorder,
            XsrUiColor.Transparent,
            Palette.NavigationSurface,
            0,
            Palette.BlurRadius,
            Palette.BorderWidth);
        ApplyVisual(
            Content,
            Palette.ContentBackground,
            Palette.PrimaryText,
            XsrUiColor.Transparent,
            XsrUiColor.Transparent,
            Palette.ContentSurface,
            0,
            0,
            0);

        foreach (XsrUiShellNavigationItem item in NavigationItems)
        {
            ApplyItemVisual(_navigationEntities[item.Id], item.Id == SelectedNavigationId);
        }

        if (_navigationToggle != default)
        {
            ApplyVisual(
                _navigationToggle,
                XsrUiColor.Transparent,
                Palette.PrimaryText,
                XsrUiColor.Transparent,
                Palette.NavigationHover,
                XsrUiSurfaceKind.None,
                cornerRadius: XsrUiCornerRadii.Inset,
                blurRadius: 0,
                borderWidth: 0);
            XsrUiVisualStyle toggleStyle = Tree.GetComponent<XsrUiVisualStyle>(_navigationToggle)!;
            toggleStyle.NavigationLayout = true;
            toggleStyle.FontSize = 12;
        }

        ApplyTitleTextStyles();
    }

    /// <summary>
    /// Applies the title-bar typography: the product title is the legacy 17 px semibold white
    /// title, and any further text (for example a version label) drops to the 12 px secondary
    /// size. Both keep the title-bar text color from the palette.
    /// </summary>
    private void ApplyTitleTextStyles()
    {
        int textIndex = 0;
        Tree.Walk(TitleBar, child =>
        {
            if (Tree.GetComponent<XsrUiText>(child) is null)
            {
                return true;
            }

            bool primary = textIndex == 0;
            XsrUiVisualStyle? visual = Tree.GetComponent<XsrUiVisualStyle>(child);
            if (visual is null)
            {
                visual = new XsrUiVisualStyle();
                Tree.SetComponent(child, visual);
            }

            visual.Foreground = Palette.TitleBarText;
            if (_declaredTitleTypography.TryGetValue(child, out var declared))
            {
                visual.FontSize = declared.Size;
                visual.FontWeight = declared.Weight;
            }
            else
            {
                visual.FontSize = primary ? TitleFontSize : TitleSecondaryFontSize;
                visual.FontWeight = primary ? TitleFontWeight : 400;
            }
            Tree.MarkDirty(child, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
            textIndex++;
            return true;
        });
    }

    private void ApplyItemVisual(XsrUiEntityId entity, bool selected)
    {
        ApplyVisual(
            entity,
            XsrUiColor.Transparent,
            selected ? Palette.SelectedNavigationText : Palette.PrimaryText,
            selected ? Palette.Accent : XsrUiColor.Transparent,
            Palette.NavigationHover,
            XsrUiSurfaceKind.None,
            cornerRadius: XsrUiCornerRadii.Inset,
            blurRadius: 0,
            borderWidth: 0);
        Tree.GetComponent<XsrUiVisualStyle>(entity)!.NavigationLayout = true;
    }

    private void SetSelection(XsrUiEntityId entity, bool selected)
    {
        XsrUiSelection selection = Tree.GetComponent<XsrUiSelection>(entity)
            ?? throw new InvalidOperationException("A shell navigation entity lost its selection component.");
        selection.IsSelected = selected;
        Tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
        ApplyItemVisual(entity, selected);
    }

    private void ApplyVisual(
        XsrUiEntityId entity,
        XsrUiColor background,
        XsrUiColor foreground,
        XsrUiColor border,
        XsrUiColor hover,
        XsrUiSurfaceKind surface,
        double cornerRadius,
        double blurRadius,
        double borderWidth)
    {
        XsrUiVisualStyle? visual = Tree.GetComponent<XsrUiVisualStyle>(entity);
        if (visual is null)
        {
            visual = new XsrUiVisualStyle();
            Tree.SetComponent(entity, visual);
        }
        visual.Background = background;
        visual.Foreground = foreground;
        visual.Border = border;
        visual.Hover = hover;
        visual.Surface = surface;
        // Background alpha expresses translucency; the element itself remains opaque so a
        // transparent navigation highlight does not make its label disappear.
        visual.Opacity = 1;
        visual.CornerRadius = cornerRadius;
        visual.BlurRadius = blurRadius;
        visual.BorderWidth = borderWidth;
        Tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
    }
}
