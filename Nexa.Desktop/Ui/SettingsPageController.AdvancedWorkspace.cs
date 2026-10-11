using System.Diagnostics;
using System.Globalization;
using Nexa.Services.Logging;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Settings;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    internal bool LauncherSafeMode { get; set; }
    internal Func<bool>? OpenAdvancedCommandPalette { get; set; }
    internal Action<DesktopCommandRoute>? NavigateAdvancedCommand { get; set; }
    internal Func<string, Task>? CopyAdvancedSettingsTextAsync { get; set; }
    internal Action? OpenAdvancedSettingsFile { get; set; }
    private RuntimeDiagnosticCapture? _advancedRuntime;
    private string[]? _advancedSettingsRows;
    private int _advancedSettingsPage;
    private Task<double>? _advancedDelayRead;
    private CancellationTokenSource? _advancedDelayStop;
    private double? _advancedDelayMilliseconds;
    private bool AdvancedWorkspaceVisible => !_disposed && _visible && _instanceDirectory is null && _selected == "advanced";

    private void CancelAdvancedWorkspace()
    {
        _advancedDelayStop?.Cancel(); _advancedDelayStop?.Dispose(); _advancedDelayStop = null;
        _advancedDelayRead = null; _advancedDelayMilliseconds = null;
        _advancedSettingsRows = null; _advancedSettingsPage = 0; _advancedRuntime = null;
    }

    private void UpdateAdvancedWorkspace()
    {
        if (!AdvancedWorkspaceVisible) { CancelAdvancedWorkspace(); return; }
        if (!_developer)
        {
            _advancedDelayStop?.Cancel(); _advancedDelayStop?.Dispose(); _advancedDelayStop = null;
            _advancedDelayRead = null; _advancedDelayMilliseconds = null;
            _advancedSettingsRows = null; _advancedSettingsPage = 0; _advancedRuntime = null;
        }
        if (_advancedDelayRead is not { IsCompleted: true } completed) return;
        _advancedDelayRead = null; _advancedDelayStop?.Dispose(); _advancedDelayStop = null;
        if (completed.IsCompletedSuccessfully) _advancedDelayMilliseconds = completed.Result;
        else _ = completed.Exception;
        ProjectAdvancedDelay();
    }

    private XsrUiEntityId _advancedRuntimeRoot, _advancedDelayRoot, _advancedSettingsRoot;

    private void BuildAdvancedWorkspace()
    {
        _advancedRuntimeRoot = _advancedDelayRoot = _advancedSettingsRoot = default;
        if (!AdvancedWorkspaceVisible) return;
        var navigation = AppendCatalog(_sections, "SettingsAdvancedNavigation.d.nxml", new Dictionary<string, string>
        { ["palette"] = Flag(OpenAdvancedCommandPalette is not null) });
        var nodes = CatalogNodes(navigation);
        _dynamic.Append(nodes["AdvancedWorkspace.Navigation.State"], DesktopMarkup.Dynamic(LauncherSafeMode ? "SettingsAdvancedSafe.d.nxml" : "SettingsAdvancedNormal.d.nxml"));
        BindAdvancedAction(nodes, "AdvancedWorkspace.Palette", () =>
        { if (OpenAdvancedCommandPalette?.Invoke() != true) _feedback.Info("请先完成当前对话框，再打开命令面板。"); }, developerOnly: false);
        foreach (var route in DesktopCommandLine.Commands)
        {
            var row = AppendCatalog(nodes["AdvancedWorkspace.Navigation.Routes"], "SettingsAdvancedRoute.d.nxml", new Dictionary<string, string>
            {
                ["row"] = "AdvancedWorkspace.Route." + route.Id,
                ["button"] = "AdvancedWorkspace.Go." + route.Id,
                ["id"] = route.Id,
                ["label"] = RuntimeTerms.Value["route." + route.Id],
                ["enabled"] = Flag(NavigateAdvancedCommand is not null)
            });
            BindAdvancedAction(CatalogNodes(row), "AdvancedWorkspace.Go." + route.Id, () => NavigateAdvancedCommand?.Invoke(route), developerOnly: false);
        }
        if (!_developer) return;
        _advancedRuntime ??= CaptureRuntimeDiagnostics(_shell);
        _advancedRuntimeRoot = AppendCatalog(_sections, "SettingsAdvancedDiagnostics.d.nxml", AdvancedRuntimeValues());
        BindAdvancedAction(CatalogNodes(_advancedRuntimeRoot), "AdvancedWorkspace.Capture", () =>
        {
            _advancedRuntime = CaptureRuntimeDiagnostics(_shell);
            _dynamic.Reload(_advancedRuntimeRoot, DesktopMarkup.Dynamic("SettingsAdvancedDiagnostics.d.nxml", AdvancedRuntimeValues()));
        });
        string Feature(bool value) => RuntimeTerm("connected." + Flag(value));
        AppendCatalog(_sections, "SettingsAdvancedFlags.d.nxml", new Dictionary<string, string>
        {
            ["java"] = Feature(_queries.TryResolve(JavaRuntimeDiagnosticsContract.Properties, out _)),
            ["trace"] = Feature(_queries.TryResolve(RuntimeTraceContract.Read, out _)),
            ["updates"] = Feature(_automaticUpdates is not null),
            ["palette"] = Feature(OpenAdvancedCommandPalette is not null)
        });
        _advancedDelayRoot = AppendCatalog(_sections, "SettingsAdvancedDelay.d.nxml", new Dictionary<string, string>
        { ["enabled"] = Flag(_advancedDelayRead is null) });
        BindAdvancedAction(CatalogNodes(_advancedDelayRoot), "AdvancedWorkspace.Delay.Start", () =>
        {
            if (_advancedDelayRead is not null) return;
            _advancedDelayStop?.Dispose(); _advancedDelayStop = new();
            _advancedDelayRead = ProbeAdvancedDelayAsync(_advancedDelayStop.Token); ObserveTransfer(_advancedDelayRead); ProjectAdvancedDelay();
        });
        ProjectAdvancedDelay();
        _advancedSettingsRoot = AppendCatalog(_sections, "SettingsAdvancedSettings.d.nxml", new Dictionary<string, string>
        { ["open"] = Flag(OpenAdvancedSettingsFile is not null), ["read"] = Flag(_values is not null), ["next"] = "false", ["captured"] = "false", ["copy"] = "false" });
        var settings = CatalogNodes(_advancedSettingsRoot);
        BindAdvancedAction(settings, "AdvancedWorkspace.Settings.OpenFile", () => OpenAdvancedSettingsFile?.Invoke());
        BindAdvancedAction(settings, "AdvancedWorkspace.Settings.Read", () =>
        {
            if (_values is null) return;
            _advancedSettingsRows = CaptureRedactedAdvancedSettings(_values); _advancedSettingsPage = 0; ProjectAdvancedSettings();
        });
        BindAdvancedAction(settings, "AdvancedWorkspace.Settings.Next", () =>
        {
            int pages = Math.Max(1, ((_advancedSettingsRows?.Length ?? 0) + 23) / 24);
            _advancedSettingsPage = (_advancedSettingsPage + 1) % pages; ProjectAdvancedSettings();
        });
        BindAdvancedAction(settings, "AdvancedWorkspace.Settings.Copy", () =>
        {
            if (_advancedSettingsRows is { } rows && CopyAdvancedSettingsTextAsync is { } copy) ObserveTransfer(copy(string.Join('\n', rows)));
        });
        ProjectAdvancedSettings();
    }

    private void BindAdvancedAction(Dictionary<string, XsrUiEntityId> nodes, string key, Action action, bool developerOnly = true)
    {
        var entity = nodes[key];
        _managementActions[entity] = () =>
        {
            if (AdvancedWorkspaceVisible && _shell.Tree.IsAlive(entity)
                && _shell.Tree.GetComponent<XsrUiInput>(entity)?.Enabled == true && (!developerOnly || _developer)) action();
        };
    }

    private Dictionary<string, string> AdvancedRuntimeValues()
    {
        var capture = _advancedRuntime!.Value;
        return new Dictionary<string, string>
        {
            ["developer"] = Flag(_developer),
            ["nodes"] = capture.Renderer.SceneNodes.ToString(CultureInfo.InvariantCulture),
            ["tracks"] = capture.Native.ActiveMotionTracks.ToString(CultureInfo.InvariantCulture),
            ["clock"] = RuntimeTerm("clock." + Flag(capture.Native.MotionTimerRunning)),
            ["budget"] = capture.Native.RasterAdmissionBudgetBytes.ToString(CultureInfo.InvariantCulture)
        };
    }

    private void ProjectAdvancedDelay()
    {
        if (!_shell.Tree.IsAlive(_advancedDelayRoot)) return;
        ReloadCatalog(_advancedDelayRoot, ("enabled", Flag(_advancedDelayRead is null)));
        var body = CatalogNodes(_advancedDelayRoot)["AdvancedWorkspace.Delay.Status"];
        _dynamic.Clear(body);
        if (_advancedDelayRead is not null) _dynamic.Append(body, DesktopMarkup.Dynamic("SettingsAdvancedDelayLoading.d.nxml"));
        else if (_advancedDelayMilliseconds is { } measured)
            _dynamic.Append(body, DesktopMarkup.Dynamic("SettingsAdvancedDelayMeasured.d.nxml", new Dictionary<string, string>
            { ["elapsed"] = measured.ToString("0.##", CultureInfo.InvariantCulture) }, _shell.Renderer.LocalizeText));
    }

    private void ProjectAdvancedSettings()
    {
        if (!_shell.Tree.IsAlive(_advancedSettingsRoot)) return;
        var rows = _advancedSettingsRows;
        int pages = Math.Max(1, ((rows?.Length ?? 0) + 23) / 24);
        _advancedSettingsPage = Math.Clamp(_advancedSettingsPage, 0, pages - 1);
        ReloadCatalog(_advancedSettingsRoot, ("next", Flag(pages > 1)), ("captured", Flag(rows is not null)),
            ("copy", Flag(CopyAdvancedSettingsTextAsync is not null)));
        var body = CatalogNodes(_advancedSettingsRoot)["AdvancedWorkspace.Settings.Rows"];
        _dynamic.Clear(body);
        if (rows is null) return;
        foreach (string line in rows.Skip(_advancedSettingsPage * 24).Take(24))
            _dynamic.Append(body, DesktopMarkup.Dynamic("SettingsAdvancedSettingsRow.d.nxml", new Dictionary<string, string> { ["line"] = line }));
    }

    internal static string[] CaptureRedactedAdvancedSettings(SettingsEffectiveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Values.Where(item => SettingsPolicySchema.ByKey.ContainsKey(item.Key)).OrderBy(item => item.Key, StringComparer.Ordinal).Take(256).Select(item =>
        {
            var definition = SettingsPolicySchema.ByKey[item.Key];
            bool expose = item.ValidationError is null && definition.Validate(item.Value) is null
                && definition.Kind is SettingsValueKind.Boolean or SettingsValueKind.Number or SettingsValueKind.Enum;
            string value = expose ? item.Value.Value ?? item.Value.Mode.ToString() : RuntimeTerms.Value["redacted"];
            return DesktopMarkup.Dynamic("SettingsAdvancedSnapshotLine.d.nxml", new Dictionary<string, string>
            {
                ["key"] = item.Key,
                ["kind"] = definition.Kind.ToString(),
                ["source"] = item.Source.ToString(),
                ["timing"] = item.Timing.ToString(),
                ["mode"] = item.Value.Mode.ToString(),
                ["value"] = value
            }).Root.Content!;
        }).ToArray();
    }

    private static async Task<double> ProbeAdvancedDelayAsync(CancellationToken token)
    {
        long started = Stopwatch.GetTimestamp(); await Task.Delay(200, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
}
