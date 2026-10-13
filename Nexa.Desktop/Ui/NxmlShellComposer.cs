using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>
/// Loads the Desktop shell from the checked-in NXML template and adapts its compiled handles to
/// the Nexa.Xsr.Renderer shell contract. NXML owns structure and order; the platform backend owns pixels.
/// </summary>
public static class NxmlShellComposer
{
    public static XsrUiShell Compose(
        XsrStateStore state,
        XsrUiShellOptions? options = null,
        IXsrUiIntentSink? intentSink = null)
        => ComposeCore(state, runtimeContext: null, options, intentSink);

    /// <summary>
    /// Composes the checked-in product shell into a runtime context created before the host
    /// state store. Its bridge is therefore the store observer and the renderer's frame-boundary
    /// dirty bridge, not a test-only side path.
    /// </summary>
    public static XsrUiShell Compose(
        XsrStateStore state,
        XsrUiRuntimeContext runtimeContext,
        XsrUiShellOptions? options = null,
        IXsrUiIntentSink? intentSink = null)
        => ComposeCore(state, runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext)), options, intentSink);

    private static XsrUiShell ComposeCore(
        XsrStateStore state,
        XsrUiRuntimeContext? runtimeContext,
        XsrUiShellOptions? options,
        IXsrUiIntentSink? intentSink)
    {
        ArgumentNullException.ThrowIfNull(state);
        options ??= new XsrUiShellOptions();
        state.Publish(state.Resolve(FirstRunState.NavigationVisible), true);
        state.Publish(state.Resolve(LaunchPageState.TitleRootKey), true);

        // The checked-in product shell deliberately owns its four product destinations, matching
        // the legacy experimental navigation registry. A composition root must not claim a
        // custom navigation contract when the NXML template cannot render it; future product
        // navigation changes are explicit NXML edits.
        if (options.NavigationItems is not null)
        {
            throw new ArgumentException(
                "The checked-in NXML product shell has a fixed navigation contract.",
                nameof(options));
        }

        NxmlHostIr ir = DesktopMarkup.Load("Shell.nxml");
        XsrUiTree tree = runtimeContext?.Tree ?? new();
        XsrUiEntityId root = NxmlUiLoader.LoadDetached(ir, tree, state);

        XsrUiEntityId titleBar = FindDirectChild(tree, root, XsrUiSemanticRole.TitleBar);
        XsrUiEntityId body = tree.Children(root).Single(child =>
            !child.Equals(titleBar) && tree.GetComponent<XsrUiStackPanel>(child) is not null);
        XsrUiEntityId navigation = FindDirectChild(tree, body, XsrUiSemanticRole.Navigation);
        XsrUiEntityId content = FindDescendant(tree, body, XsrUiSemanticRole.Content);

        IReadOnlyList<XsrUiShellNavigationItem> defaults = XsrUiShell.DefaultNavigationItems;
        XsrUiEntityId[] itemEntities = [.. tree.Children(navigation)];
        if (itemEntities.Length != defaults.Count)
        {
            throw new InvalidOperationException(
                $"The NXML shell declares {itemEntities.Length} navigation items; expected {defaults.Count}.");
        }

        Dictionary<XsrSemanticId, XsrUiEntityId> navigationEntities = [];
        for (int index = 0; index < itemEntities.Length; index++)
        {
            XsrUiEntityId entity = itemEntities[index];
            XsrUiSemantic semantic = tree.GetComponent<XsrUiSemantic>(entity)
                ?? throw new InvalidOperationException("A NXML navigation item has no semantic component.");
            XsrUiCommandBinding command = tree.GetComponent<XsrUiCommandBinding>(entity)
                ?? throw new InvalidOperationException("A NXML navigation item has no command binding.");
            XsrUiShellNavigationItem expected = defaults[index];
            if (!string.Equals(semantic.Label, expected.Label, StringComparison.Ordinal)
                || command.Command != expected.Command)
            {
                throw new InvalidOperationException(
                    $"NXML navigation item {index} does not match '{expected.Id}'.");
            }

            navigationEntities.Add(expected.Id, entity);
        }

        tree.Walk(titleBar, child =>
        {
            if (tree.Name(child) == "TitleMain" && tree.GetComponent<XsrUiText>(child) is { } titleText)
            {
                titleText.Content = options.Title;
                tree.MarkDirty(child, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
            }
            return true;
        });

        XsrUiShellTemplate template = new(
            tree,
            root,
            titleBar,
            body,
            navigation,
            content,
            defaults,
            navigationEntities);
        var shell = XsrUiShellComposer.Compose(
            state,
            template,
            options,
            intentSink,
            runtimeContext?.StateBridge);
        shell.SetNavigationExpanded(true);
        shell.SetRailPresentationProgress(1);
        return shell;
    }

    private static XsrUiEntityId FindDirectChild(XsrUiTree tree, XsrUiEntityId parent, XsrUiSemanticRole role) =>
        tree.Children(parent).Single(child => tree.GetComponent<XsrUiSemantic>(child)?.Role == role);

    private static XsrUiEntityId FindDescendant(XsrUiTree tree, XsrUiEntityId root, XsrUiSemanticRole role)
    {
        XsrUiEntityId found = default;
        tree.Walk(
            root,
            entity =>
            {
                if (tree.GetComponent<XsrUiSemantic>(entity)?.Role == role)
                {
                    found = entity;
                    return false;
                }

                return true;
            });
        return found.IsAssigned
            ? found
            : throw new InvalidOperationException($"The NXML shell has no '{role}' entity.");
    }

}
