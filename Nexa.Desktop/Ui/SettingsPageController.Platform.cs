using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal static class SettingsPresentationState
{
    public static readonly XsrSemanticId WakeKey = XsrSemanticId.Parse("ui.settings.wake");
    public static void DeclareState(XsrStateStoreBuilder builder) => builder.Cell<long>(WakeKey, "Nexa.Desktop.Settings");
}

internal sealed partial class SettingsPageController
{
    private static readonly Lazy<Dictionary<string, string>> PlatformTerms = new(ReadPlatformTerms);
    private static readonly Lazy<string[]> PlatformGroups = new(() => PlatformTerms.Value
        .Where(pair => pair.Key.StartsWith("group.", StringComparison.Ordinal) && pair.Key != "group.memory")
        .Select(pair => pair.Value).ToArray());
    private static Dictionary<string, string> ReadPlatformTerms()
    {
        var nodes = DesktopMarkup.Load("PlatformTerms.nxml").Root.Children;
        if (nodes.Any(node => node.Recipe != Nexa.Xsr.Renderer.Nxml.NxmlRuntimeRecipe.Text
            || string.IsNullOrWhiteSpace(node.Key) || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new Nexa.Xsr.Renderer.Nxml.NxmlCompileException("Platform terms must be keyed, literal text.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    }
    private static string PlatformTerm(string key) => PlatformTerms.Value[key];
    private enum PlatformReadError { Detection, Read }
    private readonly Dictionary<XsrUiEntityId, (string Id, MachineCapabilitySnapshot Snapshot, MachineCapabilityQuery Scope, long Generation)> _platformRemediationActions = [];
    private long _platformActionGeneration;
    private static readonly XsrSemanticId RefreshPlatform = XsrSemanticId.Parse("ui.settings.platform.refresh");
    private Task<XsrResult<MachineCapabilitySnapshot>>? _machineReading;
    private MachineCapabilityQuery? _machineReadingScope;
    private MachineCapabilityQuery _machineScope = new();
    private Task<XsrResult>? _machineRefreshing;
    private MachineCapabilitySnapshot? _machine;
    private long _machineSourceRevision = -1;
    private PlatformReadError? _machineError;
    private long _platformWakeRevision;
    private void WakeOnPlatformCompletion(Task task)
    {
        _ = task.ContinueWith(_ =>
        {
            if (_disposed) return;
            try { _store.Publish(_store.Resolve(SettingsPresentationState.WakeKey), Interlocked.Increment(ref _platformWakeRevision)); }
            catch (ObjectDisposedException) { }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void RefreshPlatformCapabilities()
    {
        if (_machineRefreshing is not null || !_commands.TryResolve(MachineCapabilityStateContract.RefreshCommand, out var route)) return;
        _machineError = null;
        _machineRefreshing = _commands.Dispatch(route, new MachineCapabilityRefresh()).Completion;
        WakeOnPlatformCompletion(_machineRefreshing);
        if (_selected == "platform") BuildSections();
    }
    private void UpdatePlatformCapabilities()
    {
        MachineCapabilityQuery selectedScope = ResolveMachineScope();
        if (selectedScope != _machineScope)
        {
            _machineScope = selectedScope;
            _machine = null;
            _machineSourceRevision = -1;
            _machineError = null;
        }
        if (_machineRefreshing is { IsCompleted: true } refreshed)
        {
            _machineRefreshing = null;
            if (!PendingQuery.Succeeded(refreshed)) { _machineError = PlatformReadError.Detection; if (_selected == "platform") BuildSections(); }
            else _machine = null;
        }
        if (_selected != "platform" || !_sections.IsAssigned) return;
        long revision = _store.Read<long>(_store.Resolve(MachineCapabilityStateContract.RevisionKey)).Value;
        if (_machineReading is null && (_machine is null || _machineSourceRevision != revision) && _machineError is null && _queries.TryResolve(MachineCapabilityStateContract.SnapshotQuery, out var route))
        {
            _machineReadingScope = _machineScope;
            _machineReading = _queries.QueryAsync<MachineCapabilityQuery, MachineCapabilitySnapshot>(route, _machineScope).AsTask();
            WakeOnPlatformCompletion(_machineReading);
        }
        if (_machineReading is not { IsCompleted: true } reading) return;
        _machineReading = null;
        if (_machineReadingScope != _machineScope)
        {
            _machineReadingScope = null;
            return;
        }
        _machineReadingScope = null;
        if (PendingQuery.Succeeded(reading))
        {
            _machine = reading.Result.Value;
            _machineSourceRevision = _store.Read<long>(
                _store.Resolve(MachineCapabilityStateContract.RevisionKey)).Value;
        }
        else _machineError = PlatformReadError.Read;
        BuildSections();
    }

    internal MachineCapabilityQuery CurrentMachineScope => ResolveMachineScope();

    private MachineCapabilityQuery ResolveMachineScope()
    {
        if (!_store.TryResolve(MinecraftLibraryContract.StateKey, out var libraryState)
            || _store.ReadAppliedValue(libraryState)
            is not MinecraftLibrarySnapshot library)
        {
            return new();
        }

        MinecraftInstanceDescriptor? instance = library.SelectedInstance;
        return instance is null
            ? new(MinecraftRootDirectory: library.RootDirectory)
            : new(instance.DirectoryPath, instance.Id, library.RootDirectory);
    }
    private bool MemoryAnalysisUnavailable => _machine?.Get<string>("estimate.status") is { Availability: CapabilityAvailability.TemporarilyUnavailable };

    private void BuildPlatformCapabilities()
    {
        _platformRemediationActions.Clear();
        Interlocked.Increment(ref _platformActionGeneration);
        var toolbar = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsPlatformToolbar.d.nxml", new Dictionary<string, string>
        { ["enabled"] = Flag(_machineRefreshing is null) }));
        var statusSlot = CatalogNodes(toolbar)["PlatformStatus"];
        string statusTemplate = _machineError switch
        {
            PlatformReadError.Detection => "SettingsPlatformDetectionError.d.nxml",
            PlatformReadError.Read => "SettingsPlatformReadError.d.nxml",
            _ => _machineRefreshing is not null || _machine is null ? "SettingsPlatformLoading.d.nxml" : "SettingsPlatformReady.d.nxml"
        };
        _dynamic.Append(statusSlot, DesktopMarkup.Dynamic(statusTemplate, new Dictionary<string, string>
        { ["timestamp"] = _machine?.Timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture) ?? "" }, _shell.Renderer.LocalizeText));

        if (_machine is not null)
        {
            BuildPreflightCard();
            BuildHardwareAdvice();
            // Real facts only: definitions with no wired provider are registry placeholders
            // (they rendered as endless 尚未接入检测提供方 rows), and remediation-style
            // Action definitions are operations, not observations.
            var visible = _machine.Values
                .Where(value => !MemoryAnalysisUnavailable || !value.Id.StartsWith("estimate.", StringComparison.Ordinal) || value.Id == "estimate.status")
                .Where(value => !(MemoryAnalysisUnavailable && value.Availability == CapabilityAvailability.TemporarilyUnavailable
                    && (value.Id.StartsWith("preflight.memory.", StringComparison.Ordinal)
                        || value.Id.StartsWith("preflight.estimator.", StringComparison.Ordinal) || value.Id == "preflight.gpu.vram.launch_critical")))
                .Where(value => value.Definition.Kind != CapabilityKind.Action && value.Definition.Kind != CapabilityKind.Policy)
                .Where(value => value.Id is not "input.gyroscope.available" and not "input.haptics.available"
                    and not "mod.metadata.fingerprint" and not "minecraft.settings.fingerprint")
                .Where(value => value.Availability != CapabilityAvailability.NotImplemented
                    || value.Reason != "尚未接入检测提供方")
                .GroupBy(value => value.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            var orderedGroups = visible.Select(value => value.Definition.Group).Distinct()
                .OrderBy(group =>
                {
                    int index = Array.IndexOf(PlatformGroups.Value, group);
                    return index < 0 ? int.MaxValue : index;
                }).ThenBy(group => group, StringComparer.CurrentCulture)
                .ToList();
            foreach (var group in orderedGroups)
            {
                var section = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsPlatformGroup.d.nxml", new Dictionary<string, string>
                {
                    ["group"] = "PlatformGroup." + group, ["title"] = MemoryAnalysisUnavailable && group == PlatformTerm("group.18") ? PlatformTerm("group.memory") : group,
                    ["card"] = "PlatformCard." + group, ["body"] = "PlatformCard." + group + ".Body"
                }));
                var card = CatalogNodes(section)["PlatformCard." + group + ".Body"];
                foreach (var value in visible.Where(value => value.Definition.Group == group))
                    BuildCapabilityRow(card, value);
            }
        }

        _shell.Tree.GetComponent<XsrUiScroll>(_sections)!.OffsetY = _scrollPositions.GetValueOrDefault("platform");
        _shell.Tree.MarkDirty(_sections, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }

    private static string StatusText(ICapability value)
    {
        if (value.Availability == CapabilityAvailability.Available)
            return string.IsNullOrWhiteSpace(value.DisplayValue) ? PlatformTerm("status.default")
                : PlatformTerms.Value.GetValueOrDefault("value." + value.DisplayValue, value.DisplayValue);
        if (!string.IsNullOrEmpty(value.Reason))
        {
            int colon = value.Reason.IndexOf('：');
            return colon > 0 ? value.Reason[..colon] : value.Reason;
        }
        return PlatformTerms.Value.GetValueOrDefault("status." + value.Availability, PlatformTerm("status.default"));
    }

    private void BuildCapabilityRow(XsrUiEntityId card, ICapability value)
    {
        if (value is Capability<IReadOnlyList<InputDeviceFeature>> devices)
        {
            var list = BuildCapabilityList(card, value);
            if (value.Availability != CapabilityAvailability.Available) { AppendListStatus(list, value); return; }
            if (devices.Value is not { Count: > 0 })
                _dynamic.Append(list, DesktopMarkup.Dynamic("SettingsPlatformDevicesEmpty.d.nxml"));
            else foreach (var device in devices.Value)
                _dynamic.Append(list, DesktopMarkup.Dynamic(device.Available ? "SettingsPlatformDeviceAvailable.d.nxml" : "SettingsPlatformDeviceUnavailable.d.nxml",
                    new Dictionary<string, string> { ["name"] = device.DeviceName }, _shell.Renderer.LocalizeText));
            return;
        }
        if (value is Capability<IReadOnlyList<string>> items && value.Id == "minecraft.settings.resource_packs")
        {
            var list = BuildCapabilityList(card, value);
            if (value.Availability != CapabilityAvailability.Available) { AppendListStatus(list, value); return; }
            if (items.Value is not { Count: > 0 })
                _dynamic.Append(list, DesktopMarkup.Dynamic("SettingsPlatformResourceDefault.d.nxml"));
            else foreach (string item in items.Value)
                _dynamic.Append(list, DesktopMarkup.Dynamic("SettingsPlatformListEntry.d.nxml", new Dictionary<string, string> { ["value"] = item }));
            return;
        }
        _dynamic.Append(card, DesktopMarkup.Dynamic("SettingsPlatformCapability.d.nxml", new Dictionary<string, string>
        {
            ["row"] = "PlatformCapability." + value.Id, ["label"] = value.Definition.Label,
            ["badgeKey"] = "PlatformCapabilityBadge." + value.Id, ["badge"] = _shell.Renderer.LocalizeText(StatusText(value)),
            ["badgeStyle"] = value.Availability == CapabilityAvailability.Available ? "platform-badge platform-badge-available" : "platform-badge",
            ["evidence"] = value.Availability == CapabilityAvailability.Available ? value.Source : value.Reason
        }));
    }

    private XsrUiEntityId BuildCapabilityList(XsrUiEntityId card, ICapability value)
    {
        string key = "PlatformCapabilityList." + value.Id;
        var root = _dynamic.Append(card, DesktopMarkup.Dynamic("SettingsPlatformList.d.nxml", new Dictionary<string, string>
        { ["row"] = "PlatformCapability." + value.Id, ["label"] = value.Definition.Label, ["list"] = key }));
        return CatalogNodes(root)[key];
    }

    private void AppendListStatus(XsrUiEntityId list, ICapability value)
        => _dynamic.Append(list, DesktopMarkup.Dynamic("SettingsPlatformListEntry.d.nxml", new Dictionary<string, string>
        { ["value"] = _shell.Renderer.LocalizeText(StatusText(value)) }));

    private void BuildPreflightCard()
    {
        if (_machine is not { } machine || !ReferenceEquals(_hardwareAdviceSource, machine)
            || _hardwareAdviceReport is not { OverallSeverity: not PreflightSeverity.None } report) return;
        var card = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsPlatformPreflight.d.nxml", new Dictionary<string, string>
        { ["severity"] = _shell.Renderer.LocalizeText(SeverityLabel(report.OverallSeverity)) }, _shell.Renderer.LocalizeText));
        var issues = CatalogNodes(card)["PlatformPreflightIssues"];
        foreach (var issue in report.CollapsedIssues)
        {
            string rowKey = "PlatformIssue." + issue.Code;
            var row = _dynamic.Append(issues, DesktopMarkup.Dynamic("SettingsPlatformIssue.d.nxml", new Dictionary<string, string>
            {
                ["row"] = rowKey, ["actions"] = rowKey + ".Actions", ["title"] = _shell.Renderer.LocalizeText(issue.Title),
                ["severity"] = _shell.Renderer.LocalizeText(SeverityLabel(issue.Severity))
            }, _shell.Renderer.LocalizeText));
            foreach (string id in issue.Remediations.Take(1))
            {
                var definition = RemediationCatalog.Get(id);
                bool enabled = _machineRefreshing is null && machine.Get<bool>(id) is { Availability: CapabilityAvailability.Available, Value: true };
                var button = _dynamic.Append(CatalogNodes(row)[rowKey + ".Actions"], DesktopMarkup.Dynamic("SettingsPlatformRemediation.d.nxml", new Dictionary<string, string>
                {
                    ["key"] = "PlatformRemediation." + id, ["label"] = _shell.Renderer.LocalizeText(definition.Label),
                    ["issue"] = _shell.Renderer.LocalizeText(issue.Title), ["enabled"] = Flag(enabled)
                }, _shell.Renderer.LocalizeText));
                _platformRemediationActions[button] = (id, machine, _machineScope, _platformActionGeneration);
            }
        }
    }

    private static readonly XsrSemanticId RemediationExecuted = XsrSemanticId.Parse("ui.settings.platform.remediation");
    internal Func<Task<string?>>? PickRemediationJava { get; set; }

    /// <summary>Remediation button activation → typed execute route; the result surfaces as
    /// an in-window notification (definitions requiring confirmation dispatch confirmed).</summary>
    private void OnPlatformRemediation(object? sender, DesktopUiIntentEventArgs args)
    {
        if (_disposed || !_visible || _selected != "platform" || !_shell.Tree.IsAlive(args.Intent.Source)
            || _shell.Tree.GetComponent<XsrUiInput>(args.Intent.Source)?.Enabled != true
            || !_platformRemediationActions.TryGetValue(args.Intent.Source, out var action)
            || !CurrentRemediationContext(action.Snapshot, action.Scope, action.Generation)
            || !_commands.TryResolve(MachineCapabilityStateContract.RemediationCommand, out var route)) return;
        string id = action.Id;
        Dictionary<string, string> arguments = new(StringComparer.Ordinal);
        if (_machine?.Get<long>("estimate.heap.recommended") is { Availability: CapabilityAvailability.Available } memory)
            arguments["memoryMiB"] = memory.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(_machineScope.InstanceDirectory))
            arguments["instanceDirectory"] = _machineScope.InstanceDirectory;
        void Execute()
        {
            if (CurrentRemediationContext(action.Snapshot, action.Scope, action.Generation))
                _ = ExecuteRemediationAsync(id, arguments, route, action.Snapshot, action.Scope, action.Generation);
        }
        if (RemediationCatalog.Get(id).RequiresConfirmation)
            _feedback.ShowDialog("preflight.confirm." + id, RemediationCatalog.Get(id).Label, "将对当前选中的实例执行此操作。", "继续", "取消", accepted => { if (accepted) Execute(); });
        else Execute();
    }

    private bool CurrentRemediationContext(MachineCapabilitySnapshot snapshot, MachineCapabilityQuery scope, long generation)
        => generation == Interlocked.Read(ref _platformActionGeneration) && !_disposed && _visible && _selected == "platform" && _machineRefreshing is null
            && ReferenceEquals(snapshot, _machine) && scope == ResolveMachineScope();

    private async Task ExecuteRemediationAsync(string id, Dictionary<string, string> arguments, XsrCommandId route,
        MachineCapabilitySnapshot snapshot, MachineCapabilityQuery scope, long generation)
    {
        try
        {
            if (id == "remediation.java.select")
            {
                if (PickRemediationJava is null) return;
                string? executable = await PickRemediationJava().ConfigureAwait(false);
                if (executable is null || !CurrentRemediationContext(snapshot, scope, generation)) return;
                arguments["executable"] = executable;
            }
            if (!CurrentRemediationContext(snapshot, scope, generation)) return;
            var result = await _commands.Dispatch(route, new RemediationRequest(id, arguments, Confirmed: true)).Completion.ConfigureAwait(false);
            if (_disposed) return;
            if (result.IsSuccess) _feedback.Info("修复操作已完成。");
            else _feedback.Error(result.Error?.Message ?? "修复操作未完成。");
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { if (!_disposed) _feedback.Error("修复操作未完成，请查看任务详情。"); }
    }

    private static string SeverityLabel(PreflightSeverity severity)
        => PlatformTerms.Value.GetValueOrDefault("severity." + severity, PlatformTerm("severity.None"));

}
