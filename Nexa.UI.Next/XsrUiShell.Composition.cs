using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.UI.Next;

public sealed partial class XsrUiShell
{

    public XsrUiShell(
        XsrStateStore state,
        XsrUiShellOptions? options = null,
        IXsrUiIntentSink? intentSink = null,
        XsrUiStateBridge? stateBridge = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        _windowStateStore = state;
        options ??= new XsrUiShellOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Version);

        XsrUiShellNavigationItem[] navigationItems =
            options.NavigationItems is null ? [.. BuiltInNavigationItems] : [.. options.NavigationItems];
        if (navigationItems.Length == 0)
        {
            throw new ArgumentException("The shell requires at least one navigation item.", nameof(options));
        }

        foreach (XsrUiShellNavigationItem item in navigationItems)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!_navigationEntities.TryAdd(item.Id, default))
            {
                throw new ArgumentException($"Duplicate navigation ID '{item.Id}'.", nameof(options));
            }
        }

        _externalIntentSink = intentSink;
        NavigationItems = navigationItems;
        Title = options.Title;
        Version = options.Version;
        Style = options.Style;
        Palette = XsrUiShellPalette.For(Style);

        Tree = stateBridge?.Tree ?? new XsrUiTree();
        StateBridge = stateBridge;
        Stage = new XsrUiStage(Tree, state, new ShellIntentSink(this), stateBridge);
        Root = Stage.Root;
        Content = Stage.ContentHost;

        Tree.SetComponent(Root, new XsrUiElement());
        Tree.SetComponent(Root, new XsrUiStackPanel(XsrUiOrientation.Vertical) { StretchLastChild = true });
        Tree.SetComponent(Root, new XsrUiSemantic(XsrUiSemanticRole.Page, options.Title));
        Tree.SetComponent(Root, new XsrUiVisualStyle());

        TitleBar = Tree.Create("title-bar");
        Tree.SetComponent(TitleBar, new XsrUiElement
        {
            Height = TitleBarHeight,
        });
        Tree.SetComponent(TitleBar, new XsrUiStackPanel(XsrUiOrientation.Horizontal));
        Tree.SetComponent(TitleBar, new XsrUiSemantic(XsrUiSemanticRole.TitleBar, "标题栏"));
        Tree.SetComponent(TitleBar, new XsrUiVisualStyle());

        XsrUiEntityId title = Tree.Create("title");
        Tree.SetComponent(title, new XsrUiElement
        {
            // The deterministic text metric under-reserves width for proportional fonts; the
            // title owns generous space so the real glyph run is never clipped.
            Margin = new XsrUiThickness(20, 0, 0, 0),
            Width = 240,
            VerticalAlignment = XsrUiAlignment.Center,
        });
        Tree.SetComponent(title, new XsrUiText(options.Title));
        Tree.SetComponent(title, new XsrUiSemantic(XsrUiSemanticRole.Text, options.Title));
        Tree.Attach(title, TitleBar);

        XsrUiEntityId version = Tree.Create("version");
        Tree.SetComponent(version, new XsrUiElement
        {
            Margin = new XsrUiThickness(12, 0, 0, 0),
            VerticalAlignment = XsrUiAlignment.Center,
        });
        Tree.SetComponent(version, new XsrUiText($"{options.Version}"));
        Tree.SetComponent(version, new XsrUiSemantic(XsrUiSemanticRole.Text, "版本"));
        Tree.Attach(version, TitleBar);

        Body = Tree.Create("body");
        Tree.SetComponent(Body, new XsrUiElement());
        Tree.SetComponent(Body, new XsrUiStackPanel(XsrUiOrientation.Horizontal) { StretchLastChild = true });
        Tree.Attach(TitleBar, Root);
        Tree.Attach(Body, Root);

        Navigation = Tree.Create("main-navigation");
        Tree.SetComponent(Navigation, new XsrUiElement
        {
            Width = CollapsedRailWidth,
        });
        Tree.SetComponent(Navigation, new XsrUiStackPanel(XsrUiOrientation.Vertical)
        {
            Spacing = 6,
            StretchLastChild = true,
        });
        Tree.SetComponent(Navigation, new XsrUiSemantic(XsrUiSemanticRole.Navigation, "主导航"));
        Tree.SetComponent(Navigation, new XsrUiVisualStyle());
        Tree.Attach(Navigation, Body);

        Tree.Detach(Content);
        Tree.SetComponent(Content, new XsrUiElement());
        Tree.SetComponent(Content, new XsrUiSemantic(XsrUiSemanticRole.Content, "内容区域"));
        Tree.SetComponent(Content, new XsrUiVisualStyle());
        Tree.Attach(Content, Body);

        XsrSemanticId initial = options.InitialNavigationId ?? navigationItems[0].Id;
        if (!_navigationEntities.ContainsKey(initial))
        {
            throw new ArgumentException($"Initial navigation ID '{initial}' is not registered.", nameof(options));
        }

        for (int index = 0; index < navigationItems.Length; index++)
        {
            XsrUiShellNavigationItem item = navigationItems[index];
            XsrUiEntityId entity = Tree.Create($"navigation-item:{item.Id.Value}");
            Tree.SetComponent(entity, new XsrUiElement
            {
                Height = NavigationItemHeight,
                HorizontalAlignment = XsrUiAlignment.Stretch,
                VerticalAlignment = XsrUiAlignment.Center,
            });
            Tree.SetComponent(entity, new XsrUiImage(item.Icon));
            Tree.SetComponent(entity, new XsrUiText(item.Label));
            Tree.SetComponent(entity, new XsrUiSemantic(XsrUiSemanticRole.NavigationItem, item.Label));
            Tree.SetComponent(entity, new XsrUiInput { Focusable = true, Clickable = true });
            Tree.SetComponent(entity, new XsrUiCommandBinding(item.Command));
            Tree.SetComponent(entity, new XsrUiSelection { IsSelected = item.Id == initial });
            Tree.SetComponent(entity, new XsrUiVisualStyle());
            Tree.Attach(entity, Navigation);
            _navigationEntities[item.Id] = entity;
            _navigationIds[entity] = item.Id;
        }

        SelectedNavigationId = initial;
        FinishComposition();
    }

    /// <summary>
    /// Creates a shell over a structure produced by PXML. UI.Next receives only the compiled
    /// entity handles, so the PXML compiler remains an outer composition concern.
    /// </summary>
    public XsrUiShell(
        XsrStateStore state,
        XsrUiShellTemplate template,
        XsrUiShellOptions? options = null,
        IXsrUiIntentSink? intentSink = null,
        XsrUiStateBridge? stateBridge = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        _windowStateStore = state;
        ArgumentNullException.ThrowIfNull(template);
        options ??= new XsrUiShellOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Version);
        if (options.NavigationItems is not null
            && !options.NavigationItems.Select(item => item.Id).SequenceEqual(
                template.NavigationItems.Select(item => item.Id)))
        {
            throw new ArgumentException(
                "PXML shell navigation IDs must match the supplied shell options in order.",
                nameof(options));
        }

        XsrUiShellNavigationItem[] navigationItems = [.. template.NavigationItems];
        XsrSemanticId initial = options.InitialNavigationId ?? navigationItems[0].Id;
        if (!template.NavigationEntities.ContainsKey(initial))
        {
            throw new ArgumentException($"Initial navigation ID '{initial}' is not registered.", nameof(options));
        }

        _externalIntentSink = intentSink;
        NavigationItems = navigationItems;
        Title = options.Title;
        Version = options.Version;
        Style = options.Style;
        Palette = XsrUiShellPalette.For(Style);
        Tree = template.Tree;
        if (stateBridge is not null && !ReferenceEquals(stateBridge.Tree, Tree))
        {
            throw new ArgumentException(
                "The state bridge must observe the PXML template tree.",
                nameof(stateBridge));
        }
        StateBridge = stateBridge;
        Root = template.Root;
        TitleBar = template.TitleBar;
        Body = template.Body;
        Navigation = template.Navigation;
        Content = template.Content;
        foreach ((XsrSemanticId id, XsrUiEntityId entity) in template.NavigationEntities)
        {
            _navigationEntities.Add(id, entity);
            _navigationIds.Add(entity, id);
            XsrUiSelection? selection = Tree.GetComponent<XsrUiSelection>(entity);
            if (selection is null)
            {
                selection = new XsrUiSelection();
                Tree.SetComponent(entity, selection);
            }

            selection.IsSelected = id == initial;

            if (Tree.GetComponent<XsrUiVisualStyle>(entity) is null)
            {
                Tree.SetComponent(entity, new XsrUiVisualStyle());
            }
        }

        SelectedNavigationId = initial;
        Stage = new XsrUiStage(Tree, state, Root, Content, new ShellIntentSink(this), stateBridge);
        FinishComposition();
    }
}
