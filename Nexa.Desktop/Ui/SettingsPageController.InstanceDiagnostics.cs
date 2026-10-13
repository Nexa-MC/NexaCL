using System.Globalization;
using Nexa.Core;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private sealed record InstanceRuntimeCapture(MinecraftProcessSnapshot? Session, JvmHostObservation? Observation, JvmRunSample? Sample);
    private InstanceRuntimeCapture? _instanceDiagnosticCapture;
    private string? _instanceDiagnosticTarget;
    private XsrUiEntityId _instanceDiagnosticRoot, _instanceSystemRows;
    private readonly Dictionary<XsrUiEntityId, (string Template, Dictionary<string, string> Data)> _instanceDiagnosticViews = [];
    private bool InstanceDiagnosticsVisible => !_disposed && _visible && _instance is not null && _selected == "diagnostics" && InstanceDevelopmentMode;
    private static readonly Lazy<Dictionary<string, string>> InstanceDiagnosticTerms = new(() =>
    {
        var nodes = DesktopMarkup.Load("InstanceDiagnosticTerms.nxml").Root.Children;
        if (nodes.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
            || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("Instance diagnostic terms must be keyed literal text.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });
    private string InstanceDiagnosticTerm(string key) => _shell.Renderer.LocalizeText(InstanceDiagnosticTerms.Value[key]);
    private static string InstanceNumber(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string InstanceTime(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.CurrentCulture);
    private string InstanceUnit(string template, string value) => DesktopMarkup.Dynamic("SettingsInstance" + template + ".d.nxml",
        new Dictionary<string, string> { ["value"] = value }, _shell.Renderer.LocalizeText).Root.Content!;
    private string InstanceBytes(long? bytes) => bytes is >= 0
        ? InstanceUnit(bytes >= 1024 * 1024 ? "Megabytes" : "Kilobytes",
            (bytes.Value / (bytes >= 1024 * 1024 ? 1024d * 1024 : 1024d)).ToString("N1", CultureInfo.CurrentCulture))
        : InstanceDiagnosticTerm("unavailable");
    private string InstanceProcessState(MinecraftProcessState state) => InstanceDiagnosticTerm(state switch
    {
        MinecraftProcessState.Created => "created",
        MinecraftProcessState.Running => "running",
        MinecraftProcessState.Exited => "exited",
        MinecraftProcessState.Cancelled => "cancelled",
        _ => "failed"
    });
    private XsrUiEntityId AppendInstanceDiagnostic(XsrUiEntityId slot, string template, Dictionary<string, string> data)
    {
        var root = _dynamic.Append(slot, DesktopMarkup.Dynamic(template, data, _shell.Renderer.LocalizeText));
        _instanceDiagnosticViews[root] = (template, data);
        return root;
    }
    private void ReloadInstanceDiagnostic(XsrUiEntityId root, Dictionary<string, string> data)
    {
        if (!_shell.Tree.IsAlive(root) || !_instanceDiagnosticViews.TryGetValue(root, out var view)) return;
        if (view.Data.Count == data.Count && data.All(item => view.Data.GetValueOrDefault(item.Key) == item.Value)) return;
        _dynamic.Reload(root, DesktopMarkup.Dynamic(view.Template, data, _shell.Renderer.LocalizeText));
        _instanceDiagnosticViews[root] = (view.Template, data);
    }
    private void ResetInstanceDiagnosticControls()
    {
        _instanceDiagnosticViews.Clear();
        _instanceDiagnosticRoot = _instanceSystemRows = _instanceHistoryRoot = _advancedLaunchRoot = default;
    }
    private void BindInstanceDiagnosticAction(Dictionary<string, XsrUiEntityId> nodes, string key, Action action)
    {
        var source = nodes[key]; string? scope = _instance;
        _managementActions[source] = () =>
        {
            if (!InstanceDiagnosticsVisible || !PathIdentity.Comparer.Equals(scope, _instance)
                || !PathIdentity.Comparer.Equals(_instanceDiagnosticTarget, _instance)
                || _store.Read<long>(_revisionId).Value != _revision || !_shell.Tree.IsAlive(source)
                || _shell.Tree.GetComponent<XsrUiInput>(source)?.Enabled != true) return;
            for (var entity = source; entity.IsAssigned; entity = _shell.Tree.Parent(entity))
                if (_shell.Tree.GetComponent<XsrUiElement>(entity)?.IsVisible == false) return;
            action();
        };
    }
    private void CancelInstanceDiagnostics()
    {
        _instanceDiagnosticCapture = null; _instanceDiagnosticTarget = null;
        _instanceDiagnosticRoot = _instanceSystemRows = default;
        CancelInstanceDiagnosticHistory();
    }
    private InstanceRuntimeCapture CaptureInstanceDiagnostics(string instance)
    {
        IReadOnlyList<T> Read<T>(XsrSemanticId key) => _store.TryResolve(key, out var id) ? _store.ReadCollection<T>(id).Items : [];
        var session = Read<MinecraftProcessSnapshot>(MinecraftProcessStateComposition.SessionsKey)
            .Where(item => PathIdentity.Comparer.Equals(item.InstanceDirectory, instance)).OrderByDescending(item => item.StartedAt).FirstOrDefault();
        if (session is null) return new(null, null, null);
        var observation = Read<JvmHostObservation>(JvmHostStateContract.ObservationsKey).FirstOrDefault(item => item.SessionId == session.SessionId);
        var sample = Read<JvmRunSample>(JvmHostStateContract.SamplesKey).Where(item => item.SessionId == session.SessionId).OrderByDescending(item => item.Sequence).FirstOrDefault();
        return new(session, observation, sample);
    }
    private void BuildInstanceDiagnostics()
    {
        if (!InstanceDiagnosticsVisible) return;
        if (!PathIdentity.Comparer.Equals(_instanceDiagnosticTarget, _instance)) { CancelInstanceDiagnostics(); _instanceDiagnosticTarget = _instance; }
        _instanceDiagnosticCapture ??= CaptureInstanceDiagnostics(_instance!);
        _instanceDiagnosticRoot = AppendInstanceDiagnostic(_sections, "SettingsInstanceDiagnostics.d.nxml", InstanceDiagnosticData());
        var nodes = CatalogNodes(_instanceDiagnosticRoot);
        _instanceSystemRows = nodes["InstanceDiagnostics.System.Rows"];
        BindInstanceDiagnosticAction(nodes, "InstanceDiagnostics.Refresh", () =>
        {
            _instanceDiagnosticCapture = CaptureInstanceDiagnostics(_instance!);
            CancelInstanceDiagnosticHistory();
            ProjectInstanceCapture();
            ProjectInstanceHistory();
        });
        BindInstanceDiagnosticAction(nodes, "InstanceDiagnostics.HistoryMode", () =>
        {
            _instanceOperationView = !_instanceOperationView;
            ReloadInstanceDiagnostic(_instanceDiagnosticRoot, InstanceDiagnosticData());
            ProjectInstanceHistory();
        });
        BindInstanceDiagnosticAction(nodes, "InstanceDiagnostics.Export", () => { if (ExportDiagnostics is not null) _ = ExportDiagnosticsAsync(); });
        ProjectInstanceSystemEvents();
        BuildInstanceDiagnosticHistory();
    }
    private Dictionary<string, string> InstanceDiagnosticData()
    {
        var capture = _instanceDiagnosticCapture!;
        var session = capture.Session; var observation = capture.Observation; var sample = capture.Sample;
        string Unknown() => InstanceDiagnosticTerm("unavailable");
        string Milliseconds(long? value) => value is >= 0 ? InstanceUnit("Milliseconds", InstanceNumber(value.Value)) : Unknown();
        string Mean(double? value, string unit) => value.HasValue && double.IsFinite(value.Value) && value >= 0
            ? InstanceUnit(unit, value.Value.ToString("0.##", CultureInfo.InvariantCulture)) : Unknown();
        bool hasSamples = sample is { SampleCount: > 0 };
        return new()
        {
            ["historyMode"] = InstanceDiagnosticTerm("history." + Flag(_instanceOperationView)),
            ["canExport"] = Flag(ExportDiagnostics is not null),
            ["hasSession"] = Flag(session is not null),
            ["noSession"] = Flag(session is null),
            ["hasExit"] = Flag(session?.ExitCode is not null),
            ["Session"] = session?.SessionId.ToString("N") ?? Unknown(),
            ["State"] = session is null ? Unknown() : InstanceProcessState(session.State),
            ["Window"] = session is null ? Unknown() : InstanceDiagnosticTerm("window." + Flag(session.GameWindowConfirmed)),
            ["ExitCode"] = session?.ExitCode is { } exit ? InstanceNumber(exit) : Unknown(),
            ["StartedAt"] = session is null ? Unknown() : InstanceTime(session.StartedAt),
            ["LaunchDuration"] = Milliseconds(observation?.LaunchDurationMilliseconds),
            ["LaunchWindow"] = observation is { LaunchWindowMilliseconds: > 0 } window ? Milliseconds(window.LaunchWindowMilliseconds) : Unknown(),
            ["Physical"] = InstanceBytes(observation is { CoreMetricsObserved: true } ? observation.PeakWorkingSetBytes : null),
            ["Private"] = InstanceBytes(observation is { CoreMetricsObserved: true } ? observation.PeakPrivateBytes : null),
            ["Heap"] = InstanceBytes(observation?.MeasuredHeapPeakBytes),
            ["Native"] = InstanceBytes(observation?.MeasuredNativePeakBytes),
            ["Commit"] = InstanceBytes(observation?.MeasuredCommitPeakBytes),
            ["GpuLocal"] = InstanceBytes(observation?.MeasuredGpuLocalPeakBytes),
            ["GpuShared"] = InstanceBytes(observation?.MeasuredGpuSharedPeakBytes),
            ["TreePhysical"] = InstanceBytes(observation?.PeakTreeWorkingSetBytes),
            ["PhysicalP95"] = InstanceBytes(observation is { CoreMetricsObserved: true } ? observation.RuntimePhysicalP95Bytes : null),
            ["CpuPeak"] = observation is { CpuPercentObserved: true } ? Mean(observation.CpuPeakPercent, "Percent") : Unknown(),
            ["hasSamples"] = Flag(hasSamples),
            ["noSamples"] = Flag(!hasSamples),
            ["SampleCount"] = hasSamples ? InstanceNumber(sample!.SampleCount) : Unknown(),
            ["SampleWindow"] = hasSamples ? Milliseconds(sample!.WindowMilliseconds) : Unknown(),
            ["WorkingMean"] = hasSamples ? Mean(sample!.WorkingMeanMiB, "Mebibytes") : Unknown(),
            ["CpuMean"] = hasSamples ? Mean(sample!.CpuMeanPercent, "Percent") : Unknown(),
            ["hasSystem"] = Flag(observation is { SystemEventsObserved: true }),
            ["noSystem"] = Flag(observation is not { SystemEventsObserved: true }),
            ["emptySystem"] = Flag(observation is { SystemEventsObserved: true, SystemEvents.Count: 0 }),
        };
    }
    private void ProjectInstanceCapture()
    {
        ReloadInstanceDiagnostic(_instanceDiagnosticRoot, InstanceDiagnosticData());
        ProjectInstanceSystemEvents();
    }
    private void ProjectInstanceSystemEvents()
    {
        if (!_shell.Tree.IsAlive(_instanceSystemRows)) return;
        _dynamic.Clear(_instanceSystemRows);
        if (_instanceDiagnosticCapture?.Observation is not { SystemEventsObserved: true } observation) return;
        foreach (string message in observation.SystemEvents.Take(32))
            _dynamic.Append(_instanceSystemRows, DesktopMarkup.Dynamic("SettingsInstanceSystemEvent.d.nxml",
                new Dictionary<string, string> { ["message"] = message.Length > 2048 ? message[..2048] + "…" : message }));
    }
}
