using System.Collections.Immutable;
using System.Text;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Services.Composition.Plugins;

public sealed record PluginUiRegistration(PluginIdentity Owner, string Kind, AbiValue Value, RegistrationRef Registration);
public sealed record PluginDialog(PluginIdentity Owner, UIDialogRef Reference, PluginPage Page);
public sealed record PluginAdvancedUi(ImmutableArray<PluginUiRegistration> Registrations, ImmutableArray<PluginDialog> Dialogs,
    ImmutableDictionary<QualifiedId, string> Components);

public sealed partial class PluginApplication
{
    public static readonly QualifiedId WorkspaceView = new("nexa:plugins/content");
    private readonly Dictionary<ulong, PluginUiRegistration> _ui = [];
    private readonly Dictionary<QualifiedId, string> _components = [];
    private readonly Dictionary<EntityId, PluginDialog> _dialogs = [];
    private readonly Dictionary<Guid, (PluginIdentity Owner, SubscriptionRef Reference, EntityId View, Guid Scope)> _viewWatches = [];
    private readonly Dictionary<QualifiedId, bool> _visiblePages = [];
    private static readonly SchemaRef WorkspaceSchema = Abi100Schemas.Empty;
    public PluginAdvancedUi AdvancedUi()
    {
        var active = Runtime.Definitions.Entries.Select(d => d.Registration.Handle).ToHashSet();
        lock (_gate) return new(_ui.Values.Where(r => active.Contains(r.Registration.Handle)).ToImmutableArray(), _dialogs.Values.ToImmutableArray(), _components.ToImmutableDictionary());
    }
    private void ClearAdvancedUi(string id)
    {
        foreach (var key in _visiblePages.Keys.Where(key => key.Value.StartsWith(id + ":", StringComparison.Ordinal)).ToArray()) _visiblePages.Remove(key);
        foreach (var key in _ui.Where(p => p.Value.Owner.PluginId == id).Select(p => p.Key).ToArray())
        { _components.Remove(_ui[key].Registration.DefinitionId); _ui.Remove(key); }
        foreach (var key in _dialogs.Where(p => p.Value.Owner.PluginId == id).Select(p => p.Key).ToArray()) _dialogs.Remove(key);
        foreach (var key in _viewWatches.Where(p => p.Value.Owner.PluginId == id).Select(p => p.Key).ToArray()) _viewWatches.Remove(key);
    }
    private RegistrationRef RegisterUi<T>(HostInvocation c, string kind, QualifiedId id, T value, ImmutableArray<Reference> dependencies) where T : IAbiData<T>
    {
        // The catalog supplies each concrete wire schema; no reflection enters registration.
        SchemaRef schema = kind switch
        {
            "components" => Abi100Schemas.UIComponent,
            "contributions" => Abi100Schemas.UIContribution,
            "replacements" => Abi100Schemas.UIViewReplacement,
            "commands" => Abi100Schemas.UICommand,
            "shortcuts" => Abi100Schemas.UIShortcut,
            _ => throw new AbiHostException(AbiErrors.SCHEMA_INVALID)
        };
        var reference = Runtime.Definitions.Stage(c, [new(id, new("nexa:ui/" + kind), schema, T.Write(value), dependencies)]).Single();
        lock (_gate) _ui.Add(reference.Handle, new(c.Identity, kind, T.Write(value), reference));
        return reference;
    }
    private string UiAsset(HostInvocation c, FileRef file)
    {
        lock (_gate)
        {
            if (file.LoadInstanceId != c.Identity.LoadInstanceId || file.Scope != "package" || !_files.TryGetValue(file.Token, out var owned) ||
                owned.Owner != c.Identity.LoadInstanceId || !_assets.TryGetValue(owned.Plugin, out var assets) || !assets.TryGetValue(owned.Path, out var bytes))
                throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            return new UTF8Encoding(false, true).GetString(bytes);
        }
    }
    private UIViewContract ViewContract(HostInvocation c, QualifiedId id)
    {
        if (id == WorkspaceView) return new(id, WorkspaceSchema, true, [], [], ["content"], false);
        lock (_gate)
        {
            if (!_pages.TryGetValue(id, out var page) || page.Owner != c.Identity) throw new AbiHostException(AbiErrors.CAPABILITY_DENIED);
            return new(id, page.Page.Schema, true, [], [], ["content"], false);
        }
    }
    private void RequireUiOwned(HostInvocation c, QualifiedId id, string kind)
    {
        lock (_gate) if (!_ui.Values.Any(r => r.Owner == c.Identity && r.Kind == kind && r.Registration.DefinitionId == id))
            throw new AbiHostException(AbiErrors.HANDLE_INVALID);
    }
    private IEnumerable<HostCapabilityRoute> AdvancedUiRoutes => [
        HostRoutes.Typed<UIComponent, RegistrationRef>("UI", "RegisterComponent", (c, v) =>
        {
            string source = UiAsset(c, v.EntryFile); _validateComponent(source);
            var reference = RegisterUi(c, "components", v.Id, v, []);
            lock (_gate) _components[v.Id] = source;
            return ValueTask.FromResult(reference);
        }),
        HostRoutes.Typed<UIContribution, RegistrationRef>("UI", "RegisterContribution", (c, v) =>
        {
            var target = ViewContract(c, v.View); RequireUiOwned(c, v.Component, "components");
            if (!target.PublicNodes.Contains(v.SemanticNode) || v.Before.Concat(v.After).Any(id => !id.Value.StartsWith(c.Identity.PluginId + ":", StringComparison.Ordinal)))
                throw new AbiHostException(AbiErrors.CAPABILITY_DENIED);
            return ValueTask.FromResult(RegisterUi(c, "contributions", v.Id, v, [new(v.Component, true)]));
        }),
        HostRoutes.Typed<UIViewReplacement, RegistrationRef>("UI", "RegisterViewReplacement", (c, v) =>
        {
            var target = ViewContract(c, v.View);
            lock (_gate)
            {
                if (!target.Replaceable || target.HostControl || target.Schema != v.ViewContract || !_pages.TryGetValue(v.Page, out var page) || page.Owner != c.Identity || page.Page.Schema != target.Schema)
                    throw new AbiHostException(AbiErrors.CAPABILITY_DENIED);
                if (_ui.Values.Any(r => r.Kind == "replacements" && UIViewReplacement.Read(r.Value).View == v.View)) throw new AbiHostException(AbiErrors.CONFLICT);
            }
            return ValueTask.FromResult(RegisterUi(c, "replacements", v.Id, v, [new(v.Page, true)]));
        }),
        HostRoutes.Typed<UICommand, RegistrationRef>("UI", "RegisterCommand", (c, v) =>
        {
            if (!v.Action.Value.StartsWith(c.Identity.PluginId + ":", StringComparison.Ordinal) || !v.Title.Arguments.IsEmpty) throw new AbiHostException(AbiErrors.CAPABILITY_DENIED);
            return ValueTask.FromResult(RegisterUi(c, "commands", v.Id, v, []));
        }),
        HostRoutes.Typed<UIShortcut, RegistrationRef>("UI", "RegisterShortcut", (c, v) =>
        {
            RequireUiOwned(c, v.Command, "commands"); _ = ViewContract(c, v.Scope);
            if (v.Gesture.Length is < 1 or > 64 || v.Gesture.Any(char.IsControl)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            return ValueTask.FromResult(RegisterUi(c, "shortcuts", v.Id, v, [new(v.Command, true)]));
        }),
        HostRoutes.Typed<PageRequest, Page<UIViewContract>>("UI", "ListExtensionTargets", (c, v) =>
        {
            if (v.Limit is < 1 or > 256 || v.Cursor is not null) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            // One stable Host extension point. Protected navigation and confirmation controls are never exposed.
            return ValueTask.FromResult(new Page<UIViewContract>([ViewContract(c, WorkspaceView)], null, new("plugin-workspace-v1")));
        }),
        HostRoutes.Typed<QualifiedRequest, UIViewContract>("UI", "DescribeViewContract", (c, v) => ValueTask.FromResult(ViewContract(c, v.Id))),
        HostRoutes.Typed<UIDialog, UIDialogRef>("UI", "ShowDialog", (c, v) =>
        {
            var page = Snapshot().Pages.SingleOrDefault(p => p.Owner == c.Identity && p.Page.Id == v.Page) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            if (!v.Id.Value.StartsWith(c.Identity.PluginId + ":", StringComparison.Ordinal) || !Abi100Schemas.Validate(page.Page.Schema, v.Model)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            lock (_gate)
            {
                if (_dialogs.Count >= 8 || _dialogs.Values.Any(d => d.Owner == c.Identity)) throw new AbiHostException(AbiErrors.CONFLICT);
                var reference = new UIDialogRef(new(Guid.NewGuid().ToString("N")), Guid.NewGuid());
                _dialogs.Add(reference.Id, new(c.Identity, reference, page with { Model = v.Model, Revision = new(Guid.NewGuid().ToString("N")) }));
                return ValueTask.FromResult(reference);
            }
        }),
        HostRoutes.Typed<EntityRequest, Empty>("UI", "CloseDialog", (c, v) =>
        {
            ClosePluginDialog(c.Identity, v.Id); return ValueTask.FromResult(new Empty());
        }),
        HostRoutes.Typed<UIViewWatch, SubscriptionRef>("UI", "WatchViewLifecycle", (c, v) =>
        {
            lock (_gate)
            {
                string state;
                if (_dialogs.TryGetValue(v.View, out var dialog) && dialog.Owner == c.Identity && dialog.Reference.ViewScope == v.ViewScope) state = "open";
                else if (v.ViewScope == c.Identity.LoadInstanceId && _pages.TryGetValue(new(v.View.Value), out var page) && page.Owner == c.Identity)
                    state = _visiblePages.GetValueOrDefault(page.Page.Id) ? "open" : "hidden";
                else throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                if (_viewWatches.Count >= 128) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                var reference = Runtime.Services.CreateHostSubscription(c, "nexa.ui/100", "nexa.ui.read", TextResult.Write(new(state)));
                _viewWatches.Add(reference.Token, (c.Identity, reference, v.View, v.ViewScope)); return ValueTask.FromResult(reference with { ResyncRequired = v.Cursor is not null });
            }
        })
    ];
    public void ReportVisiblePages(ImmutableArray<QualifiedId> visible)
    {
        lock (_gate)
            foreach (var page in _pages.Values)
            {
                bool shown = visible.Contains(page.Page.Id);
                if (_visiblePages.GetValueOrDefault(page.Page.Id) == shown) continue;
                _visiblePages[page.Page.Id] = shown;
                foreach (var watch in _viewWatches.Values.Where(w => w.Owner == page.Owner && w.View.Value == page.Page.Id.Value).ToArray())
                    if (!Runtime.Services.PublishHostSubscription(watch.Owner, watch.Reference, TextResult.Write(new(shown ? "open" : "hidden")))) _viewWatches.Remove(watch.Reference.Token);
            }
    }
    public void ClosePluginDialog(PluginIdentity owner, EntityId id)
    {
        lock (_gate)
        {
            if (!_dialogs.TryGetValue(id, out var dialog) || dialog.Owner != owner) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            _dialogs.Remove(id);
            foreach (var key in _drawings.Where(p => p.Value.Owner == owner && p.Value.Surface.ViewScope == dialog.Reference.ViewScope).Select(p => p.Key).ToArray()) _drawings.Remove(key);
            foreach (var key in _drawingInputs.Where(p => p.Value.Owner == owner && p.Value.Request.Surface.ViewScope == dialog.Reference.ViewScope).Select(p => p.Key).ToArray()) _drawingInputs.Remove(key);
            foreach (var watch in _viewWatches.Values.Where(w => w.Owner == owner && w.View == id))
                Runtime.Services.PublishHostSubscription(owner, watch.Reference, TextResult.Write(new("closed")));
        }
    }
}
