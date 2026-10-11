using Nexa.Services.Minecraft.Java;
using Nexa.Services.Settings;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId ScanJava = XsrSemanticId.Parse("ui.settings.java.scan");
    private static readonly XsrSemanticId ChooseJava = XsrSemanticId.Parse("ui.settings.java.choose");
    private static readonly XsrSemanticId AddJava = XsrSemanticId.Parse("ui.settings.java.add");
    private Task<string?>? _javaPick;
    private Task<XsrResult>? _javaManagementWrite;
    private CancellationTokenSource? _javaManagementStop;
    private long _javaPickRevision;
    private long _javaRegistryGeneration;
    private bool JavaManagementBusy => _javaPick is not null || _javaManagementWrite is not null;
    private Task<XsrResult<JavaRuntimeInventorySnapshot>>? _javaInventoryRead;
    private CancellationTokenSource? _javaInventoryStop;
    private JavaRuntimeInventorySnapshot? _javaInventory;
    private XsrUiEntityId _javaInventoryGroup;
    private bool _javaInventoryRequested, _javaInventoryFailed;
    private readonly Dictionary<XsrUiEntityId, JavaRuntimeCandidate> _javaChoices = [];
    private readonly Dictionary<XsrUiEntityId, (string Template, bool Selected, bool Enabled)> _javaChoiceViews = [];
    private static bool IsJavaInventoryIntent(XsrSemanticId command) => command == ScanJava || command == ChooseJava || command == AddJava;
    private bool IsJavaInventoryPage => _instanceDirectory is null ? _selected == "java" : _selected == "game";

    private void BuildJavaInventory(XsrUiEntityId parent = default)
    {
        if (!IsJavaInventoryPage || !_queries.TryResolve(JavaRuntimeInventoryContract.Query, out _)) return;
        string? focus = _shell.Tree.IsAlive(_shell.Renderer.Focused) ? _shell.Tree.Name(_shell.Renderer.Focused) : null;
        var projection = new Dictionary<string, string>
        {
            ["titleStyle"] = "settings-java-title" + (_instanceDirectory is null ? "" : " settings-java-instance-title"),
            ["addVisible"] = Flag(_instanceDirectory is null && PickRemediationJava is not null && _commands.TryResolve(JavaRuntimeInventoryContract.Manage, out _)),
            ["addEnabled"] = Flag(!JavaManagementBusy && _javaInventoryRead is null && _javaInventory is not null),
            ["scanEnabled"] = Flag(_javaInventoryRead is null && !JavaManagementBusy),
        };
        if (!_shell.Tree.IsAlive(_javaInventoryGroup))
        {
            _javaInventoryGroup = _dynamic.Append(parent.IsAssigned ? parent : _sections, DesktopMarkup.Dynamic("SettingsJavaInventory.d.nxml", projection));
            _shell.Tree.SetComponent(_javaInventoryGroup, new XsrUiSelectionGroup(isSelectionRequired: false));
        }
        else _dynamic.Reload(_javaInventoryGroup, DesktopMarkup.Dynamic("SettingsJavaInventory.d.nxml", projection));
        var nodes = CatalogNodes(_javaInventoryGroup);
        var body = nodes["SettingsJavaInventory.Body"];
        _shell.Tree.Walk(body, entity => { _managementActions.Remove(entity); _contentActions.Remove(entity); return true; });
        _dynamic.Clear(body);
        _javaChoices.Clear(); _javaChoiceViews.Clear();
        if (_javaInventory is null)
            _dynamic.Append(body, DesktopMarkup.Dynamic(_javaInventoryFailed ? "SettingsJavaFailed.d.nxml" : "SettingsJavaLoading.d.nxml"));
        else
        {
            if (_javaInventory.Runtimes.Count == 0) _dynamic.Append(body, DesktopMarkup.Dynamic("SettingsJavaEmpty.d.nxml"));
            int index = 0;
            foreach (var candidate in _javaInventory.Runtimes)
            {
                var runtime = candidate.Installation;
                string brand = runtime.Brand switch
                {
                    JavaBrand.EclipseTemurin => "Temurin",
                    JavaBrand.IbmSemeru => "IBM Semeru",
                    JavaBrand.OpenJdk => "OpenJDK",
                    JavaBrand.GraalVmCommunity => "GraalVM",
                    JavaBrand.JetBrains => "JetBrains",
                    JavaBrand.TencentKona => "Tencent Kona",
                    JavaBrand.Unknown => _shell.Renderer.LocalizeText("未知发行方"),
                    _ => runtime.Brand.ToString()
                };
                string architecture = runtime.Architecture switch
                { JavaArchitecture.X86 => "x86", JavaArchitecture.X64 => "x64", JavaArchitecture.Arm => "ARM", JavaArchitecture.Arm64 => "ARM64", _ => _shell.Renderer.LocalizeText("未知架构") };
                bool managed = _javaInventory.ManagedRuntimes.Any(entry => Nexa.Core.PathIdentity.Comparer.Equals(entry.Executable, runtime.JavaExecutablePath));
                var row = _dynamic.Append(body, DesktopMarkup.Dynamic("SettingsJavaRuntime.d.nxml", new Dictionary<string, string>
                {
                    ["row"] = "SettingsJavaRuntime." + index,
                    ["labels"] = "SettingsJavaRuntime.Label." + index,
                    ["name"] = $"Java {runtime.Version} · {brand} · {architecture}",
                    ["path"] = runtime.JavaExecutablePath,
                    ["managed"] = Flag(managed),
                    ["external"] = Flag(!managed),
                }));
                var rowNodes = CatalogNodes(row);
                var choose = _dynamic.Append(rowNodes["SettingsJavaRuntime.Choice"], DesktopMarkup.Dynamic("SettingsJavaChoiceAvailable.d.nxml", new Dictionary<string, string>
                { ["key"] = "SettingsJavaChoose." + index++, ["selected"] = "false", ["enabled"] = "false" }));
                _javaChoices[choose] = candidate;
                BuildJavaManagement(rowNodes["SettingsJavaRuntime.Management"], runtime.JavaExecutablePath, candidate.IsEnabled);
            }
            foreach (var entry in _javaInventory.Registrations.Where(entry => entry.Custom && !_javaInventory.Runtimes.Any(candidate =>
                Nexa.Core.PathIdentity.Comparer.Equals(entry.Executable, candidate.Installation.JavaExecutablePath))))
                AppendMissingJava(body, "SettingsJavaMissing", entry.Executable, entry.Enabled, managed: false);
            foreach (var managed in _javaInventory.ManagedRuntimes.Where(entry => !_javaInventory.Runtimes.Any(candidate =>
                Nexa.Core.PathIdentity.Comparer.Equals(entry.Executable, candidate.Installation.JavaExecutablePath))
                && !_javaInventory.Registrations.Any(registered => registered.Custom && Nexa.Core.PathIdentity.Comparer.Equals(registered.Executable, entry.Executable))))
                AppendMissingJava(body, "SettingsJavaManagedMissing", managed.Executable, _javaInventory.Registrations.FirstOrDefault(entry =>
                    Nexa.Core.PathIdentity.Comparer.Equals(entry.Executable, managed.Executable))?.Enabled ?? true, managed: true);
            UpdateJavaChoices();
        }
        if (focus is not null) _shell.Tree.Walk(_javaInventoryGroup, entity =>
        { if (_shell.Tree.Name(entity) == focus) _shell.Renderer.Focus(entity, showIndicator: false); return true; });
    }

    private void AppendMissingJava(XsrUiEntityId body, string key, string executable, bool enabled, bool managed)
    {
        var row = _dynamic.Append(body, DesktopMarkup.Dynamic("SettingsJavaMissing.d.nxml", new Dictionary<string, string>
        { ["row"] = key, ["path"] = executable, ["managed"] = Flag(managed), ["external"] = Flag(!managed) }));
        BuildJavaManagement(CatalogNodes(row)["SettingsJavaRuntime.Management"], executable, enabled);
    }

    internal static string JavaRuntimeControlKey(string action, string executable)
        => "SettingsJava" + action + "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(executable)));

    private void BuildJavaManagement(XsrUiEntityId row, string executable, bool enabled)
    {
        if (_instanceDirectory is not null || _javaInventory is null || !_commands.TryResolve(JavaRuntimeInventoryContract.Manage, out _)) return;
        bool interactive = !JavaManagementBusy && _javaInventoryRead is null;
        string key = JavaRuntimeControlKey("Enabled", executable);
        const string optionStyle = "settings-catalog-option settings-java-enabled-option";
        var controls = _dynamic.Append(row, DesktopMarkup.Dynamic("SettingsJavaManagement.d.nxml", new Dictionary<string, string>
        {
            ["track"] = key,
            ["on"] = key + ".true",
            ["off"] = key + ".false",
            ["isOn"] = Flag(enabled),
            ["isOff"] = Flag(!enabled),
            ["enabled"] = Flag(interactive),
            ["onStyle"] = optionStyle + (enabled ? " settings-catalog-option-selected" : ""),
            ["offStyle"] = optionStyle + (!enabled ? " settings-catalog-option-selected" : ""),
        }));
        var nodes = CatalogNodes(controls);
        var thumb = nodes["SettingsJavaEnabled.Thumb"];
        _shell.Tree.SetComponent(thumb, new XsrUiTransition());
        _shell.Tree.SetComponent(nodes[key], new XsrUiSegmentedTrack(thumb) { Selected = nodes[key + (enabled ? ".true" : ".false")] });
        RegisterContentAction(nodes[key + ".true"], () => { if (!enabled) ManageJava(executable, JavaRuntimeManagementAction.Enable); });
        RegisterContentAction(nodes[key + ".false"], () => { if (enabled) ManageJava(executable, JavaRuntimeManagementAction.Disable); });
        var actions = nodes["SettingsJavaManagement.Actions"];
        if (_javaInventory.Registrations.Any(entry => entry.Custom && Nexa.Core.PathIdentity.Comparer.Equals(entry.Executable, executable)))
        {
            var remove = _dynamic.Append(actions, DesktopMarkup.Dynamic("SettingsJavaRemove.d.nxml", new Dictionary<string, string>
            { ["key"] = JavaRuntimeControlKey("Remove", executable), ["enabled"] = Flag(interactive) }));
            RegisterContentAction(remove, () =>
            {
                long revision = _javaInventory.RegistryRevision;
                long generation = _javaRegistryGeneration;
                _feedback.ShowDialog("java.registry.remove", "移除 Java 登记", "移除此 Java 的手动登记，保留磁盘上的文件。\n" + executable,
                    "移除", "取消", accepted =>
                    {
                        if (accepted && generation == _javaRegistryGeneration && IsJavaInventoryPage && _instanceDirectory is null && _javaInventory?.RegistryRevision == revision)
                            ManageJava(executable, JavaRuntimeManagementAction.Remove);
                    });
            });
        }
        if (_javaInventory.ManagedRuntimes.FirstOrDefault(entry => Nexa.Core.PathIdentity.Comparer.Equals(entry.Executable, executable)) is { } managed)
        {
            var remove = _dynamic.Append(actions, DesktopMarkup.Dynamic("SettingsJavaDelete.d.nxml", new Dictionary<string, string>
            { ["key"] = JavaRuntimeControlKey("Delete", executable), ["enabled"] = Flag(interactive) }));
            RegisterContentAction(remove, () =>
            {
                long revision = _javaInventory.RegistryRevision;
                long generation = _javaRegistryGeneration;
                _feedback.ShowDialog("java.managed.delete", "删除托管 Java", "删除此启动器安装的 Java 文件。正在使用、已修改或包含额外文件的运行时会保留。\n" + managed.Directory,
                    "删除", "取消", accepted =>
                    {
                        if (accepted && generation == _javaRegistryGeneration && IsJavaInventoryPage && _instanceDirectory is null && _javaInventory?.RegistryRevision == revision
                            && _javaInventory.ManagedRuntimes.Any(entry => entry == managed))
                            ManageJava(executable, JavaRuntimeManagementAction.DeleteManaged, revision, managed.Identity);
                    });
            });
        }

    }

    private void ManageJava(string executable, JavaRuntimeManagementAction action, long? expectedRevision = null, string? managedIdentity = null)
    {
        if (_instanceDirectory is not null || !IsJavaInventoryPage || JavaManagementBusy || _javaInventory is null
            || !_commands.TryResolve(JavaRuntimeInventoryContract.Manage, out var route)) return;
        _javaManagementStop ??= new();
        _javaManagementWrite = _commands.Dispatch(route, new JavaRuntimeManageCommand(executable, action,
            expectedRevision ?? _javaInventory.RegistryRevision)
        { ExpectedManagedIdentity = managedIdentity }, cancellationToken: _javaManagementStop.Token).Completion;
        ObserveTransfer(_javaManagementWrite); BuildJavaInventory();
    }

    private async Task<string?> PickRegisteredJavaAsync(CancellationToken token)
    {
        string? path = await PickRemediationJava!().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return path;
    }

    private void HandleJavaInventory(XsrSemanticId command, XsrUiEntityId source)
    {
        if (!IsJavaInventoryPage || !_shell.Tree.IsAlive(source)) return;
        if (command == AddJava && _instanceDirectory is null && _shell.Tree.Name(source) == "SettingsJavaAdd"
            && !JavaManagementBusy && _javaInventoryRead is null && _javaInventory is not null && PickRemediationJava is not null)
        {
            _javaManagementStop?.Dispose(); _javaManagementStop = new(); _javaPickRevision = _javaInventory.RegistryRevision;
            _javaPick = PickRegisteredJavaAsync(_javaManagementStop.Token); ObserveTransfer(_javaPick); BuildJavaInventory(); return;
        }
        if (JavaManagementBusy) return;
        if (command == ScanJava && _shell.Tree.Name(source) == "SettingsJavaScan" && _javaInventoryRead is null)
        {
            CancelJavaInventory(); StartJavaInventory(refresh: true); BuildJavaInventory(); return;
        }
        if (command == ChooseJava && _javaChoices.TryGetValue(source, out var candidate)
            && _javaInventory is not null && _javaInventoryRead is null && candidate.IsAvailable && candidate.IsEnabled && _writing is null
            && _commands.TryResolve(SettingsPolicyContract.SetCommand, out var set))
            _writing = SaveAsync(set, new("java.runtime", _instanceDirectory is null ? SettingsLayer.Global : SettingsLayer.Instance,
                new(SettingsOverrideMode.Custom, candidate.Installation.JavaExecutablePath), _instance));
    }

    private void StartJavaInventory(bool refresh)
    {
        _javaInventoryRequested = true;
        if (!_queries.TryResolve(JavaRuntimeInventoryContract.Query, out var read)) return;
        _javaInventoryStop = new();
        _javaInventoryRead = _queries.QueryAsync<JavaRuntimeInventoryQuery, JavaRuntimeInventorySnapshot>(read,
            new(refresh), cancellationToken: _javaInventoryStop.Token).AsTask();
        ObserveTransfer(_javaInventoryRead);
    }

    private void UpdateJavaInventory()
    {
        if (!IsJavaInventoryPage) { CancelJavaInventory(); return; }
        if (_javaPick is { IsCompleted: true } picking)
        {
            _javaPick = null;
            if (picking.IsCompletedSuccessfully && picking.Result is { } path) ManageJava(path, JavaRuntimeManagementAction.Add, _javaPickRevision);
            else if (!picking.IsCanceled && !picking.IsCompletedSuccessfully) _feedback.Error("无法选择 Java，请重试。");
            BuildJavaInventory();
        }
        if (_javaManagementWrite is { IsCompleted: true } writing)
        {
            _javaManagementWrite = null;
            if (!writing.IsCanceled && !PendingQuery.Succeeded(writing)) _feedback.Error(writing.IsCompletedSuccessfully ? writing.Result.Error?.Message ?? "Java 管理未完成。" : "Java 管理未完成。");
            CancelJavaInventory(); StartJavaInventory(refresh: true); BuildJavaInventory();
        }
        if (!_javaInventoryRequested) { StartJavaInventory(refresh: false); BuildJavaInventory(); }
        if (_javaInventoryRead is { IsCompleted: true } reading)
        {
            _javaInventoryRead = null;
            _javaInventoryStop?.Dispose(); _javaInventoryStop = null;
            _javaInventoryFailed = !PendingQuery.Succeeded(reading);
            _javaInventory = _javaInventoryFailed ? null : reading.Result.Value;
            BuildJavaInventory();
        }
        UpdateJavaChoices();
    }

    private void UpdateJavaChoices()
    {
        string? selected = _values?.Values.FirstOrDefault(value => value.Key == "java.runtime")?.Value.Value;
        foreach (var (entity, candidate) in _javaChoices)
        {
            bool selectedRuntime = candidate.IsAvailable && candidate.IsEnabled && string.Equals(candidate.Installation.JavaExecutablePath, selected,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            string template = !candidate.IsAvailable ? "SettingsJavaChoiceUnavailable.d.nxml" : !candidate.IsEnabled ? "SettingsJavaChoiceDisabled.d.nxml"
                : selectedRuntime ? "SettingsJavaChoiceSelected.d.nxml" : "SettingsJavaChoiceAvailable.d.nxml";
            bool enabled = candidate.IsEnabled && candidate.IsAvailable && _writing is null && _javaInventoryRead is null && !JavaManagementBusy;
            var view = (template, selectedRuntime, enabled);
            if (_javaChoiceViews.TryGetValue(entity, out var previous) && previous == view) continue;
            _dynamic.Reload(entity, DesktopMarkup.Dynamic(template, new Dictionary<string, string>
            { ["key"] = _shell.Tree.Name(entity), ["selected"] = Flag(selectedRuntime), ["enabled"] = Flag(enabled) }));
            _javaChoiceViews[entity] = view;
        }
    }

    private void CancelJavaInventory()
    {
        _javaRegistryGeneration++;
        _javaManagementStop?.Cancel(); _javaManagementStop?.Dispose(); _javaManagementStop = null;
        _javaPick = null; _javaManagementWrite = null;
        _javaInventoryStop?.Cancel(); _javaInventoryStop?.Dispose(); _javaInventoryStop = null;
        _javaInventoryRead = null; _javaInventory = null; _javaInventoryRequested = _javaInventoryFailed = false;
        _javaChoices.Clear(); _javaChoiceViews.Clear();
        int pending = _pending.Count;
        for (int i = 0; i < pending && _pending.TryDequeue(out var intent); i++)
            if (!IsJavaInventoryIntent(intent.Command)) _pending.Enqueue(intent);
    }
}
