using System.Globalization;
using Nexa.Services.Settings;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal readonly record struct RuntimeDiagnosticCapture(XsrUiRendererDiagnostics Renderer,
    AvaloniaUiRuntimeCapture Native, bool ReducedMotion, bool OptionalMotionSuspended);

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId RefreshRuntimeDiagnostics = XsrSemanticId.Parse("ui.settings.diagnostics.runtime.refresh");
    private readonly HashSet<XsrUiEntityId> _runtimeDiagnosticActions = [];
    private readonly Dictionary<string, XsrUiEntityId> _runtimeDiagnosticBodies = [];
    private RuntimeDiagnosticCapture? _runtimeDiagnosticCapture;
    private bool RuntimeDiagnosticsVisible => !_disposed && _visible && _instanceDirectory is null && _developer && _selected == "appearance";
    private static bool IsRuntimeDiagnosticsIntent(XsrSemanticId command) => command == RefreshRuntimeDiagnostics;

    internal static RuntimeDiagnosticCapture CaptureRuntimeDiagnostics(XsrUiShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        return new(shell.Renderer.CaptureDiagnostics(), AvaloniaUiRuntimeDiagnostics.Capture(),
            shell.Renderer.ReducedMotion, shell.Renderer.OptionalMotionSuspended);
    }

    private void ResetRuntimeDiagnosticControls()
    {
        _runtimeDiagnosticActions.Clear();
        _runtimeDiagnosticBodies.Clear();
        _runtimeDiagnosticFields.Clear();
    }

    private void RetireRuntimeDiagnostics()
    {
        ResetRuntimeDiagnosticControls();
        _runtimeDiagnosticCapture = null;
    }

    private static readonly Lazy<Dictionary<string, string>> RuntimeTerms = new(() =>
    {
        var nodes = DesktopMarkup.Load("RuntimeTerms.nxml").Root.Children;
        if (nodes.Any(node => node.Recipe != Nexa.Xsr.Renderer.Nxml.NxmlRuntimeRecipe.Text
            || string.IsNullOrWhiteSpace(node.Key) || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new Nexa.Xsr.Renderer.Nxml.NxmlCompileException("Runtime terms must be keyed literal text.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });
    private string RuntimeTerm(string key) => _shell.Renderer.LocalizeText(RuntimeTerms.Value[key]);
    private readonly Dictionary<string, XsrUiEntityId> _runtimeDiagnosticFields = [];

    private void BuildRuntimeDiagnostics()
    {
        if (!RuntimeDiagnosticsVisible) { RetireRuntimeDiagnostics(); return; }
        _runtimeDiagnosticCapture ??= CaptureRuntimeDiagnostics(_shell);
        void Card(string id, string key)
        {
            if (!_catalog!.Entries.Any(entry => entry.Id == id && entry.DeveloperOnly
                && entry.Kind == SettingsCatalogEntryKind.State && entry.Availability == SettingsCapabilityAvailability.Available)) return;
            var root = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsRuntime" + key + ".d.nxml"));
            var nodes = CatalogNodes(root);
            _runtimeDiagnosticActions.Add(nodes["SettingsRuntime." + key + ".Refresh"]);
            _runtimeDiagnosticBodies[key] = nodes["SettingsRuntime." + key + ".Body"];
        }
        Card("global.appearance.d999201ab2ad", "Renderer");
        Card("global.appearance.6357798f5b55", "LayoutPaint");
        Card("global.appearance.d44cc0e413d3", "Scheduler");
        Card("global.appearance.d1957652c919", "Performance");
        BuildRuntimeDiagnosticFields();
    }

    private void BuildRuntimeDiagnosticFields()
    {
        if (_runtimeDiagnosticCapture is not { } capture) return;
        string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        string Known(long? value) => value.HasValue ? Number(value.Value) : RuntimeTerm("unavailable");
        var renderer = capture.Renderer;
        var native = capture.Native;
        var values = new Dictionary<string, string>
        {
            ["SceneVersion"] = Number(renderer.SceneVersion),
            ["SceneNodes"] = Number(renderer.SceneNodes),
            ["TreeCount"] = Number(renderer.TreeEntities),
            ["Scope"] = RuntimeTerm("renderer.scope"),
            ["LayoutVisits"] = Number(renderer.LayoutVisits),
            ["SceneBuilds"] = Number(renderer.SceneVersion),
            ["NativeCommits"] = Number(native.CommittedScenes),
            ["NativePaints"] = Number(native.NodePaintCallbacks),
            ["PendingState"] = Number(renderer.PendingStateEntries),
            ["ActiveTracks"] = Number(native.ActiveMotionTracks),
            ["ClockRunning"] = RuntimeTerm("clock." + Flag(native.MotionTimerRunning)),
            ["TargetRate"] = Number(native.MotionTargetFramesPerSecond),
            ["ObservedRate"] = native.MotionTickRate is { } rate ? DesktopMarkup.Dynamic("SettingsRuntimeHertz.d.nxml",
                new Dictionary<string, string> { ["value"] = rate.ToString("0.0", CultureInfo.InvariantCulture) }).Root.Content! : RuntimeTerm("unavailable"),
            ["MotionPolicy"] = RuntimeTerm("motion." + Flag(capture.ReducedMotion || capture.OptionalMotionSuspended)),
            ["PixelCharge"] = Number(native.RasterPixelChargeBytes),
            ["AdmissionBudget"] = Number(native.RasterAdmissionBudgetBytes),
            ["RasterEntries"] = Number(native.RasterEntries),
            ["RasterLeases"] = Number(native.RasterLeases),
            ["DecodeAttempts"] = Number(native.RasterDecodeAttempts),
            ["DisposedBitmaps"] = Number(native.RasterDisposedBitmaps),
            ["MemoryAvailable"] = Known(native.MemoryPressure.AvailableBytes),
            ["MemoryLimit"] = Known(native.MemoryPressure.LimitBytes),
            ["GpuResident"] = Known(native.MemoryPressure.GpuResidentBytes),
            ["MemorySource"] = native.MemoryPressure.Source is { Length: > 0 } source ? source : RuntimeTerm("unavailable"),
            ["PresentationRate"] = RuntimeTerm("unavailable")
        };
        foreach (var (key, body) in _runtimeDiagnosticBodies)
        {
            values["Scope"] = RuntimeTerm(key == "Renderer" ? "renderer.scope" : "native.scope");
            var fragment = DesktopMarkup.Dynamic("SettingsRuntime" + key + "Fields.d.nxml", values);
            if (_runtimeDiagnosticFields.TryGetValue(key, out var root)) _dynamic.Reload(root, fragment);
            else _runtimeDiagnosticFields[key] = _dynamic.Append(body, fragment);
        }
    }

    private void HandleRuntimeDiagnosticsIntent(DesktopUiIntent intent)
    {
        if (!RuntimeDiagnosticsVisible || _store.Read<long>(_revisionId).Value != _revision
            || intent.Command != RefreshRuntimeDiagnostics || !_shell.Tree.IsAlive(intent.Source)
            || !_runtimeDiagnosticActions.Contains(intent.Source)
            || _shell.Tree.GetComponent<XsrUiInput>(intent.Source)?.Enabled != true) return;
        _runtimeDiagnosticCapture = CaptureRuntimeDiagnostics(_shell);
        BuildRuntimeDiagnosticFields();
    }
}
