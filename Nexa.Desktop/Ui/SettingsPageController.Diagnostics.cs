using System.Globalization;
using Nexa.Services.Settings;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal sealed record StateInspectorMetadata(string SemanticId, string Owner, string Kind, long Revision, string Availability);
internal sealed record StateInspectorCapture(int Total, IReadOnlyList<StateInspectorMetadata> Rows);

internal sealed partial class SettingsPageController
{
    private const int StateInspectorPageSize = 32;
    private const int StateInspectorMaximumRows = 2048;
    private static readonly XsrSemanticId RefreshRendererDiagnostics = XsrSemanticId.Parse("ui.settings.diagnostics.renderer.refresh");
    private static readonly XsrSemanticId RefreshStateDiagnostics = XsrSemanticId.Parse("ui.settings.diagnostics.state.refresh");
    private static readonly XsrSemanticId PreviousStateDiagnostics = XsrSemanticId.Parse("ui.settings.diagnostics.state.previous");
    private static readonly XsrSemanticId NextStateDiagnostics = XsrSemanticId.Parse("ui.settings.diagnostics.state.next");
    private readonly Dictionary<XsrUiEntityId, XsrSemanticId> _developerDiagnosticActions = [];
    private RendererDiagnosticsCapture? _rendererDiagnostics;
    private StateInspectorCapture? _stateInspector;
    private bool _stateInspectorError;
    private bool _developerDiagnosticsCaptured;
    private int _stateInspectorPage;
    private XsrUiEntityId _rendererDiagnosticsBody, _rendererDiagnosticsFields, _stateInspectorBody, _stateInspectorCard;
    private readonly Dictionary<string, XsrUiEntityId> _rendererDiagnosticVariants = [];

    private sealed record RendererDiagnosticsCapture(long SceneVersion, int LayoutVisits, int TreeCount,
        XsrUiColorScheme ColorScheme, bool ReducedMotion, bool OptionalMotionSuspended, bool EffectiveReducedMotion);

    private bool DeveloperDiagnosticsVisible => !_disposed && _visible && _instanceDirectory is null && _developer && _selected == "advanced";

    private static bool IsDeveloperDiagnosticsIntent(XsrSemanticId command) => command == RefreshRendererDiagnostics
        || command == RefreshStateDiagnostics || command == PreviousStateDiagnostics || command == NextStateDiagnostics;

    private void ResetDeveloperDiagnosticControls()
    {
        _developerDiagnosticActions.Clear();
        _rendererDiagnosticsBody = _rendererDiagnosticsFields = _stateInspectorBody = _stateInspectorCard = default;
        _rendererDiagnosticVariants.Clear();
    }

    private void RetireDeveloperDiagnostics()
    {
        ResetDeveloperDiagnosticControls();
        _rendererDiagnostics = null; _stateInspector = null; _stateInspectorError = false;
        _developerDiagnosticsCaptured = false; _stateInspectorPage = 0;
    }

    private void BuildDeveloperDiagnostics()
    {
        if (!DeveloperDiagnosticsVisible) { RetireDeveloperDiagnostics(); return; }
        bool Available(string id) => _catalog!.Entries.Any(entry => entry.Id == id && entry.DeveloperOnly
            && entry.Kind == SettingsCatalogEntryKind.State && entry.Availability == SettingsCapabilityAvailability.Available);
        bool renderer = Available("global.advanced.8d723d8dcdfb");
        bool inspector = Available("global.advanced.01c0534c8b37");
        if (!renderer && !inspector) return;
        if (!_developerDiagnosticsCaptured)
        {
            if (renderer) CaptureRendererDiagnostics();
            if (inspector) CaptureStateInspector();
            _developerDiagnosticsCaptured = true;
        }
        if (renderer) BuildRendererDiagnostics();
        if (inspector) BuildStateInspector();
    }

    private void CaptureRendererDiagnostics() => _rendererDiagnostics = new(_shell.Renderer.SceneVersion,
        _shell.Renderer.LastLayoutVisits, _shell.Tree.Count, _shell.Renderer.ColorScheme,
        _shell.Renderer.ReducedMotion, _shell.Renderer.OptionalMotionSuspended, _shell.Renderer.EffectiveReducedMotion);

    internal static StateInspectorCapture CaptureDeveloperStateMetadata(XsrStateStore store)
    {
        var snapshot = store.CaptureSnapshot();
        // Never access Value or retain the raw snapshot: diagnostics cannot render arbitrary payloads.
        var rows = snapshot.Entries.OrderBy(entry => entry.SemanticId.Value, StringComparer.Ordinal)
            .Take(StateInspectorMaximumRows)
            .Select(entry => new StateInspectorMetadata(entry.SemanticId.Value, entry.Owner, entry.Kind.ToString(),
                entry.Revision, entry.Availability.ToString())).ToArray();
        return new(snapshot.Entries.Count, Array.AsReadOnly(rows));
    }

    private void CaptureStateInspector()
    {
        _stateInspectorPage = 0; _stateInspectorError = false;
        try { _stateInspector = CaptureDeveloperStateMetadata(_store); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { _stateInspector = null; _stateInspectorError = true; }
    }

    private void BuildRendererDiagnostics()
    {
        var card = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsRendererDiagnostics.d.nxml"));
        var nodes = CatalogNodes(card);
        _developerDiagnosticActions[nodes["SettingsDiagnostics.Renderer.Refresh"]] = RefreshRendererDiagnostics;
        _rendererDiagnosticsBody = nodes["SettingsDiagnostics.Renderer.Body"];
        _rendererDiagnosticsFields = default;
        _rendererDiagnosticVariants.Clear();
        BuildRendererDiagnosticFields();
    }

    private void BuildRendererDiagnosticFields()
    {
        if (_rendererDiagnostics is not { } snapshot) return;
        var fields = DesktopMarkup.Dynamic("SettingsRendererFields.d.nxml", new Dictionary<string, string>
        {
            ["SceneVersion"] = snapshot.SceneVersion.ToString(CultureInfo.InvariantCulture),
            ["LayoutVisits"] = snapshot.LayoutVisits.ToString(CultureInfo.InvariantCulture),
            ["TreeCount"] = snapshot.TreeCount.ToString(CultureInfo.InvariantCulture)
        });
        if (_shell.Tree.IsAlive(_rendererDiagnosticsFields)) _dynamic.Reload(_rendererDiagnosticsFields, fields);
        else _rendererDiagnosticsFields = _dynamic.Append(_rendererDiagnosticsBody, fields);
        var nodes = CatalogNodes(_rendererDiagnosticsFields);
        Project("ColorScheme", snapshot.ColorScheme.IsDark ? "SettingsRendererColorDark.d.nxml" : "SettingsRendererColorLight.d.nxml",
            new Dictionary<string, string> { ["accent"] = snapshot.ColorScheme.Accent.ToString() });
        Project("Motion", snapshot.EffectiveReducedMotion ? "SettingsRendererMotionReduced.d.nxml" : "SettingsRendererMotionNormal.d.nxml");
        Project("MotionPolicy", (snapshot.ReducedMotion, snapshot.OptionalMotionSuspended) switch
        {
            (false, false) => "SettingsRendererPolicy00.d.nxml",
            (false, true) => "SettingsRendererPolicy01.d.nxml",
            (true, false) => "SettingsRendererPolicy10.d.nxml",
            _ => "SettingsRendererPolicy11.d.nxml"
        });
        void Project(string name, string template, Dictionary<string, string>? data = null)
        {
            var fragment = DesktopMarkup.Dynamic(template, data, _shell.Renderer.LocalizeText);
            if (_rendererDiagnosticVariants.TryGetValue(name, out var root)) _dynamic.Reload(root, fragment);
            else _rendererDiagnosticVariants[name] = _dynamic.Append(nodes["SettingsDiagnostics.Renderer." + name + ".Slot"], fragment);
        }
    }

    private void BuildStateInspector()
    {
        _stateInspectorCard = AppendCatalog(_sections, "SettingsStateInspector.d.nxml", new Dictionary<string, string>
        { ["previous"] = "false", ["next"] = "false" });
        var nodes = CatalogNodes(_stateInspectorCard);
        _developerDiagnosticActions[nodes["SettingsDiagnostics.State.Refresh"]] = RefreshStateDiagnostics;
        _developerDiagnosticActions[nodes["SettingsDiagnostics.State.Previous"]] = PreviousStateDiagnostics;
        _developerDiagnosticActions[nodes["SettingsDiagnostics.State.Next"]] = NextStateDiagnostics;
        _stateInspectorBody = nodes["SettingsDiagnostics.State.Body"];
        BuildStateInspectorPage();
    }

    private void BuildStateInspectorPage()
    {
        var snapshot = _stateInspector;
        int count = snapshot?.Rows.Count ?? 0;
        int pages = Math.Max(1, (count + StateInspectorPageSize - 1) / StateInspectorPageSize);
        _stateInspectorPage = Math.Clamp(_stateInspectorPage, 0, pages - 1);
        ReloadCatalog(_stateInspectorCard, ("previous", Flag(_stateInspectorPage > 0)), ("next", Flag(_stateInspectorPage + 1 < pages)));
        _dynamic.Clear(_stateInspectorBody);
        if (_stateInspectorError || snapshot is null)
        {
            _dynamic.Append(_stateInspectorBody, DesktopMarkup.Dynamic(_stateInspectorError ? "SettingsStateError.d.nxml" : "SettingsStateEmpty.d.nxml"));
            return;
        }
        _dynamic.Append(_stateInspectorBody, DesktopMarkup.Dynamic(count < snapshot.Total ? "SettingsStateSummaryLimited.d.nxml" : "SettingsStateSummary.d.nxml",
            new Dictionary<string, string>
            {
                ["page"] = (_stateInspectorPage + 1).ToString(CultureInfo.InvariantCulture), ["pages"] = pages.ToString(CultureInfo.InvariantCulture),
                ["total"] = snapshot.Total.ToString(CultureInfo.InvariantCulture), ["limit"] = StateInspectorMaximumRows.ToString(CultureInfo.InvariantCulture)
            }, _shell.Renderer.LocalizeText));
        foreach (var entry in snapshot.Rows.Skip(_stateInspectorPage * StateInspectorPageSize).Take(StateInspectorPageSize))
        {
            string name = "SettingsDiagnostics.State.Entry." + entry.SemanticId;
            _dynamic.Append(_stateInspectorBody, DesktopMarkup.Dynamic("SettingsStateEntry.d.nxml", new Dictionary<string, string>
            {
                ["row"] = name, ["idKey"] = name + ".Id", ["metadataKey"] = name + ".Metadata",
                ["id"] = entry.SemanticId, ["owner"] = entry.Owner, ["kind"] = entry.Kind,
                ["revision"] = entry.Revision.ToString(CultureInfo.InvariantCulture), ["availability"] = entry.Availability
            }, _shell.Renderer.LocalizeText));
        }
    }

    private void HandleDeveloperDiagnosticsIntent(DesktopUiIntent intent)
    {
        if (!DeveloperDiagnosticsVisible || _store.Read<long>(_revisionId).Value != _revision
            || !_shell.Tree.IsAlive(intent.Source) || !_developerDiagnosticActions.TryGetValue(intent.Source, out var command)
            || command != intent.Command || _shell.Tree.GetComponent<XsrUiInput>(intent.Source)?.Enabled != true) return;
        if (command == RefreshRendererDiagnostics) { CaptureRendererDiagnostics(); BuildRendererDiagnosticFields(); }
        else if (command == RefreshStateDiagnostics) { CaptureStateInspector(); BuildStateInspectorPage(); }
        else { _stateInspectorPage += command == PreviousStateDiagnostics ? -1 : 1; BuildStateInspectorPage(); }
    }
}
