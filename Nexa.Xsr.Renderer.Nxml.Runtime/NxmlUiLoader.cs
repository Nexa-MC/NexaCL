using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>
/// Reports one deterministic load failure: unknown state paths are the runtime's discovery.
/// </summary>
public sealed class NxmlLoadException(string message) : InvalidOperationException(message)
{
}

/// <summary>
/// Loads a compiled NXML host IR into a live entity tree. Validated semantic IDs resolve
/// against the concrete store, bindings become binding records, commands become semantic-ID
/// command bindings. The loader is a fixed mapping over closed IR data — no reflection, no
/// runtime parsing.
/// </summary>
public sealed partial class NxmlUiLoader
{
    public static XsrUiEntityId LoadDetached(NxmlHostIr ir, XsrUiTree tree, XsrStateStore state)
    {
        var host = tree.Create("nxml-page-loader");
        try
        {
            var page = Load(ir, tree, state, host);
            tree.Detach(page);
            return page;
        }
        finally { tree.Destroy(host); }
    }

    public static XsrUiEntityId Load(
        NxmlHostIr ir,
        XsrUiTree tree,
        XsrStateStore state,
        XsrUiEntityId parent)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(state);
        if (!tree.IsAlive(parent))
        {
            throw new NxmlLoadException($"The load parent '{parent}' is not alive.");
        }

        bool dynamicSlot = tree.GetComponent<NxmlDynamicSlot>(parent) is not null;
        if (ir.IsDynamic != dynamicSlot)
            throw new NxmlLoadException("Dynamic slots accept only .d.nxml fragments, and fragments require a Dynamic slot.");

        XsrUiEntityId root = tree.Create(ir.Root.Key ?? ir.Root.Kind.ToString());
        bool committed = false;
        try
        {
            PopulateNode(ir.Root, root, tree, state);
            ResolveSegments(ir.Root, root, tree);
            tree.Attach(root, parent);
            ResolveAnchors(ir.Root, root, tree);
            committed = true;
            return root;
        }
        finally
        {
            if (!committed && tree.IsAlive(root))
            {
                tree.Destroy(root);
            }
        }
    }

    private static void ResolveAnchors(NxmlIrNode root, XsrUiEntityId entity, XsrUiTree tree)
    {
        List<(XsrUiEntityId Entity, XsrUiEntityId Anchor, double Gap)> anchors = [];
        Visit(root, entity);
        foreach (var item in anchors) tree.SetComponent(item.Entity, new XsrUiAnchoredOverlay(item.Anchor, item.Gap));

        void Visit(NxmlIrNode node, XsrUiEntityId current)
        {
            if (node.AnchorKey is { } key)
            {
                XsrUiEntityId match = default;
                for (var scope = current; scope.IsAssigned; scope = tree.Parent(scope))
                {
                    int count = 0;
                    tree.Walk(scope, candidate => { if (tree.Name(candidate) == key) { match = candidate; count++; } return true; });
                    if (count > 1) throw new NxmlLoadException("The closest anchor scope is ambiguous.");
                    if (count == 1)
                    {
                        for (var ancestor = match; ancestor.IsAssigned; ancestor = tree.Parent(ancestor))
                            if (ancestor == current) throw new NxmlLoadException("An overlay cannot anchor to itself or its descendants.");
                        break;
                    }
                }
                if (!match.IsAssigned) throw new NxmlLoadException("The local overlay anchor is missing.");
                anchors.Add((current, match, node.AnchorGap));
            }
            var children = tree.Children(current);
            for (int index = 0; index < node.Children.Count; index++) Visit(node.Children[index], children[index]);
        }
    }

    private static void PopulateNode(
        NxmlIrNode node,
        XsrUiEntityId entity,
        XsrUiTree tree,
        XsrStateStore state)
    {
        if (node.StyleIdentity is { } identity) tree.SetComponent(entity, identity);
        tree.SetComponent(entity, new NxmlAuthoredStyleSource(node.StyleSource));
        NxmlIrBinding? visibilityBinding = node.Bindings.FirstOrDefault(
            binding => binding.Property == XsrUiStateProperty.Visibility);
        if (node.Width is not null || node.Height is not null
            || node.MinWidth is not null || node.MaxWidth is not null
            || node.MinHeight is not null || node.MaxHeight is not null
            || node.Weight != 0
            || node.HorizontalAlignment != XsrUiAlignment.Stretch
            || node.VerticalAlignment != XsrUiAlignment.Stretch
            || node.Margin != default || node.Padding != default || !node.IsVisible
            || node.SegmentWidth is not null
            || visibilityBinding is not null)
        {
            tree.SetComponent(entity, new XsrUiElement
            {
                Width = node.Width ?? node.SegmentWidth,
                Height = node.Height,
                MinWidth = node.MinWidth,
                MaxWidth = node.MaxWidth,
                MinHeight = node.MinHeight,
                MaxHeight = node.MaxHeight,
                Weight = node.Weight,
                HorizontalAlignment = node.HorizontalAlignment,
                VerticalAlignment = node.VerticalAlignment,
                Margin = node.Margin,
                Padding = node.Padding,
                IsVisible = node.IsVisible,
                BoundVisibility = visibilityBinding is null
                    ? default
                    : ResolveState(state, visibilityBinding.State),
            });
        }

        switch (node.Recipe)
        {
            case NxmlRuntimeRecipe.TextInput:
                tree.SetComponent(entity, new XsrUiTextInput { Placeholder = node.Placeholder ?? string.Empty, IsPassword = node.IsPassword, MaximumLength = node.MaximumLength, PreserveTabs = node.PreserveTabs });
                NxmlIrBinding? inputEnabled = node.Bindings.FirstOrDefault(binding => binding.Property == XsrUiStateProperty.Enabled);
                tree.SetComponent(entity, new XsrUiInput
                {
                    Focusable = true,
                    Enabled = node.Enabled,
                    BoundEnabled = inputEnabled is null ? default : ResolveState(state, inputEnabled.State),
                });
                break;
            case NxmlRuntimeRecipe.Element:
                break;
            case NxmlRuntimeRecipe.VerticalPager:
                tree.SetComponent(entity, new XsrUiPager(XsrUiOrientation.Vertical));
                tree.SetComponent(entity, new XsrUiInput { Focusable = true });
                break;
            case NxmlRuntimeRecipe.HorizontalPager:
                tree.SetComponent(entity, new XsrUiPager(XsrUiOrientation.Horizontal));
                tree.SetComponent(entity, new XsrUiInput { Focusable = true });
                break;
            case NxmlRuntimeRecipe.Progress:
                XsrUiProgress progress = new();
                progress.SetTarget(node.Value);
                NxmlIrBinding? valueBinding = node.Bindings.FirstOrDefault(
                    binding => binding.Property == XsrUiStateProperty.Value);
                if (valueBinding is not null)
                {
                    progress.BoundState = ResolveState(state, valueBinding.State);
                }

                tree.SetComponent(entity, progress);
                break;
            case NxmlRuntimeRecipe.DynamicSlot:
                tree.SetComponent(entity, new NxmlDynamicSlot());
                if (node.SlotLayout == NxmlRuntimeRecipe.Element) break;
                if (node.SlotLayout is NxmlRuntimeRecipe.HorizontalPager or NxmlRuntimeRecipe.VerticalPager)
                {
                    tree.SetComponent(entity, new XsrUiPager(node.SlotLayout == NxmlRuntimeRecipe.HorizontalPager ? XsrUiOrientation.Horizontal : XsrUiOrientation.Vertical));
                    tree.SetComponent(entity, new XsrUiInput { Focusable = true });
                    break;
                }
                goto case NxmlRuntimeRecipe.StackLayout;
            case NxmlRuntimeRecipe.StackLayout:
                tree.SetComponent(entity, new XsrUiStackPanel(node.Orientation)
                {
                    Spacing = node.Spacing,
                    StretchLastChild = node.StretchLastChild,
                });
                if (node.Scrollable)
                {
                    tree.SetComponent(entity, new XsrUiScroll
                    { ShowsVerticalIndicator = node.ShowsVerticalIndicator, ShowsHorizontalIndicator = node.ShowsHorizontalIndicator, UseVerticalWheelForHorizontalScroll = node.UseVerticalWheelForHorizontalScroll, StickToEnd = node.StickToEnd });
                    if (node.ScrollGesture) tree.SetComponent(entity, new XsrUiScrollGesture());
                }

                break;
            case NxmlRuntimeRecipe.Text:
                XsrUiText text = new(node.Content ?? string.Empty) { Localize = node.Localize, MaxLines = node.MaxLines, TrimOverflow = node.TrimOverflow };
                NxmlIrBinding? textBinding = node.Bindings.FirstOrDefault(
                    binding => binding.Property == XsrUiStateProperty.Text);
                if (textBinding is not null)
                {
                    text.BoundState = ResolveState(state, textBinding.State);
                }

                tree.SetComponent(entity, text);
                break;
            case NxmlRuntimeRecipe.RangeInput:
                ValidateRangeFacts(node);
                tree.SetComponent(entity, new XsrUiRangeInput(node.Minimum, node.Maximum, node.Step, node.RangeValue));
                goto case NxmlRuntimeRecipe.CommandInput;
            case NxmlRuntimeRecipe.CommandInput:
                XsrUiInput commandInput = new()
                {
                    Focusable = node.Focusable,
                    Clickable = node.Clickable,
                    Enabled = node.Enabled,
                };
                NxmlIrBinding? enabledBinding = node.Bindings.FirstOrDefault(
                    binding => binding.Property == XsrUiStateProperty.Enabled);
                if (enabledBinding is not null)
                {
                    // Clickability follows the bound state fact, mirroring BoundVisibility.
                    commandInput.BoundEnabled = ResolveState(state, enabledBinding.State);
                }

                tree.SetComponent(entity, commandInput);

                if (node.Command is not null)
                {
                    // The IR carries a validated semantic ID; no repeated parsing.
                    tree.SetComponent(entity, new XsrUiCommandBinding(node.Command.Value));
                }

                NxmlIrBinding? commandTextBinding = node.Bindings.FirstOrDefault(
                    binding => binding.Property == XsrUiStateProperty.Text);
                if (node.Content is not null || commandTextBinding is not null)
                {
                    XsrUiText commandText = new(node.Content ?? string.Empty) { Localize = node.Localize, MaxLines = node.MaxLines, TrimOverflow = node.TrimOverflow };
                    if (commandTextBinding is not null)
                    {
                        commandText.BoundState = ResolveState(state, commandTextBinding.State);
                    }

                    tree.SetComponent(entity, commandText);
                }

                if (node.ImageSource is not null)
                {
                    tree.SetComponent(entity, new XsrUiImage(node.ImageSource));
                }

                break;
            case NxmlRuntimeRecipe.Image:
                tree.SetComponent(entity, new XsrUiImage(node.ImageSource ?? string.Empty));
                break;
            default:
                throw new NxmlLoadException($"The runtime recipe '{node.Recipe}' is unsupported.");
        }

        if (node.Recipe is NxmlRuntimeRecipe.Element or NxmlRuntimeRecipe.StackLayout or NxmlRuntimeRecipe.DynamicSlot
            && (node.Clickable || node.Focusable || node.Command is not null))
        {
            var enabled = node.Bindings.FirstOrDefault(binding => binding.Property == XsrUiStateProperty.Enabled);
            tree.SetComponent(entity, new XsrUiInput
            {
                Clickable = node.Clickable,
                Focusable = node.Focusable,
                Enabled = node.Enabled,
                BoundEnabled = enabled is null ? default : ResolveState(state, enabled.State),
            });
            if (node.Command is { } command) tree.SetComponent(entity, new XsrUiCommandBinding(command));
        }
        if (node.OverlayMotion is { } motion) tree.SetComponent(entity, new XsrUiOverlayMotion(motion));
        if (node.LiveSetting is { } live) tree.SetComponent(entity, new XsrUiLiveRegion(live));
        if (node.DismissCommand is { } dismiss) tree.SetComponent(entity, new XsrUiDismissBinding(dismiss));

        NxmlIrBinding? transitionBinding = node.Bindings.FirstOrDefault(binding => binding.Property == XsrUiStateProperty.TransitionKey);
        if (node.TransitionKey is not null || transitionBinding is not null)
            tree.SetComponent(entity, new XsrUiTransition
            {
                Key = node.TransitionKey,
                OffsetX = node.TransitionOffsetX,
                OffsetY = node.TransitionOffsetY,
                MovesSelf = node.TransitionMovesSelf ?? node.Recipe is NxmlRuntimeRecipe.Text or NxmlRuntimeRecipe.CommandInput or NxmlRuntimeRecipe.Image,
                BoundKey = transitionBinding is null ? default : ResolveState(state, transitionBinding.State),
            });

        NxmlIrBinding? semanticBinding = node.Bindings.FirstOrDefault(
            binding => binding.Property == XsrUiStateProperty.SemanticLabel);
        if (node.Role != XsrUiSemanticRole.None || node.Label is not null || semanticBinding is not null)
        {
            XsrUiSemantic semantic = new(node.Role, node.Label) { Localize = node.Localize };
            if (semanticBinding is not null)
            {
                semantic.BoundLabel = ResolveState(state, semanticBinding.State);
            }

            tree.SetComponent(entity, semantic);
        }

        if (node.StableContent) tree.SetComponent(entity, new XsrUiStableContent());
        if (node.ModalLayer is { } modal) tree.SetComponent(entity, new XsrUiOverlayLayer(isModal: modal));

        if (node.Selected is { } selected) tree.SetComponent(entity, new XsrUiSelection { IsSelected = selected });
        if (node.Checked is { } isChecked) tree.SetComponent(entity, new XsrUiToggle(isChecked));
        if (node.SelectionRequired is { } required) tree.SetComponent(entity, new XsrUiSelectionGroup(required));
        if (node.SegmentWidth is { } segmentWidth) tree.SetComponent(entity, new XsrUiSegmentReveal(segmentWidth));

        if (node.Visual is { } visual)
            tree.SetComponent(entity, CreateVisual(visual));

        foreach (NxmlIrBinding binding in node.Bindings)
        {
            tree.BindState(entity, new XsrUiStateDependency(
                ResolveState(state, binding.State),
                binding.Property,
                binding.DirtyKinds));
        }

        foreach (NxmlIrNode child in node.Children)
        {
            XsrUiEntityId childEntity = tree.Create(child.Key ?? child.Kind.ToString());
            tree.Attach(childEntity, entity);
            PopulateNode(child, childEntity, tree, state);
        }
    }

    private static XsrStateId ResolveState(XsrStateStore state, XsrSemanticId semanticId)
    {
        // The IR already carries a validated semantic ID; load time only resolves it through
        // the registry — no repeated parsing.
        if (!state.TryResolve(semanticId, out XsrStateId stateId))
        {
            throw new NxmlLoadException(
                $"The state '{semanticId}' is not registered in the target state store.");
        }

        return stateId;
    }
}
