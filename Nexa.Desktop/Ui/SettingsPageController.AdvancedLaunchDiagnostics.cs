using System.Globalization;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private sealed record AdvancedLaunchRead(MinecraftLaunchPlanDiagnosticSnapshot? Plan, InstanceLocalDocumentsSnapshot? Documents, string? Error);
    private sealed record AdvancedLaunchLine(string Template, Dictionary<string, string> Data);
    private Task<AdvancedLaunchRead>? _advancedLaunchReading;
    private CancellationTokenSource? _advancedLaunchStop;
    private AdvancedLaunchRead? _advancedLaunch;
    private string? _advancedLaunchInstance;
    private string _advancedLaunchTab = "plan";
    private int _advancedLaunchPage;
    private XsrUiEntityId _advancedLaunchRoot;

    private void CancelAdvancedLaunchDiagnostics()
    {
        _advancedLaunchStop?.Cancel(); _advancedLaunchStop?.Dispose(); _advancedLaunchStop = null;
        if (_advancedLaunchReading is { } retired)
            _ = retired.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _advancedLaunchReading = null; _advancedLaunch = null; _advancedLaunchInstance = null;
        _advancedLaunchPage = 0; _advancedLaunchRoot = default;
    }
    private void UpdateAdvancedLaunchDiagnostics()
    {
        if (!InstanceDiagnosticsVisible || !Nexa.Core.PathIdentity.Comparer.Equals(_instance, _advancedLaunchInstance))
        { if (_advancedLaunchInstance is not null) CancelAdvancedLaunchDiagnostics(); return; }
        if (_advancedLaunchReading is not { IsCompleted: true } read) return;
        _advancedLaunchReading = null;
        _advancedLaunch = read.IsCompletedSuccessfully ? read.Result : new(null, null, "launch.failed");
        _advancedLaunchStop?.Dispose(); _advancedLaunchStop = null;
        ProjectAdvancedLaunchDiagnostics();
    }
    private void BuildAdvancedLaunchDiagnostics()
    {
        if (!InstanceDiagnosticsVisible) return;
        _advancedLaunchRoot = AppendInstanceDiagnostic(_sections, "SettingsInstanceLaunchFacts.d.nxml", AdvancedLaunchData(0));
        var nodes = CatalogNodes(_advancedLaunchRoot);
        BindInstanceDiagnosticAction(nodes, "AdvancedLaunch.Read", ReadAdvancedLaunchDiagnostics);
        foreach (string id in new[] { "plan", "classpath", "natives", "manifest", "metadata", "lockfile", "trace" })
            BindInstanceDiagnosticAction(nodes, "AdvancedLaunch.Tab." + id, () =>
            {
                if (_advancedLaunchReading is not null || _advancedLaunch is null) return;
                _advancedLaunchTab = id; _advancedLaunchPage = 0; ProjectAdvancedLaunchDiagnostics();
            });
        BindInstanceDiagnosticAction(nodes, "AdvancedLaunch.Previous", () => { _advancedLaunchPage--; ProjectAdvancedLaunchDiagnostics(); });
        BindInstanceDiagnosticAction(nodes, "AdvancedLaunch.Next", () => { _advancedLaunchPage++; ProjectAdvancedLaunchDiagnostics(); });
        ProjectAdvancedLaunchDiagnostics();
    }
    private Dictionary<string, string> AdvancedLaunchData(int count)
    {
        int pages = Math.Max(1, (count + 31) / 32);
        _advancedLaunchPage = Math.Clamp(_advancedLaunchPage, 0, pages - 1);
        string? status = _advancedLaunchReading is not null ? "launch.loading" : _advancedLaunch?.Error ?? (_advancedLaunch is null ? "launch.idle" : null);
        var data = new Dictionary<string, string>
        {
            ["loading"] = Flag(_advancedLaunchReading is not null),
            ["readEnabled"] = Flag(_advancedLaunchReading is null),
            ["status"] = status is null ? "" : InstanceDiagnosticTerm(status),
            ["showStatus"] = Flag(status is not null),
            ["hasFacts"] = Flag(_advancedLaunchReading is null && _advancedLaunch is not null),
            ["paging"] = Flag(count > 32),
            ["page"] = InstanceNumber(_advancedLaunchPage + 1),
            ["pages"] = InstanceNumber(pages),
            ["previousEnabled"] = Flag(_advancedLaunchPage > 0),
            ["nextEnabled"] = Flag(_advancedLaunchPage < pages - 1),
        };
        foreach (string id in new[] { "plan", "classpath", "natives", "manifest", "metadata", "lockfile", "trace" })
        {
            data[id + "Style"] = "settings-catalog-action instance-launch-tab" + (_advancedLaunchTab == id ? " instance-launch-tab-selected" : "");
            data[id + "Selected"] = Flag(_advancedLaunchTab == id);
        }
        return data;
    }
    private void ProjectAdvancedLaunchDiagnostics()
    {
        if (!_shell.Tree.IsAlive(_advancedLaunchRoot)) return;
        IReadOnlyList<AdvancedLaunchLine> lines = _advancedLaunchReading is null && _advancedLaunch is not null ? AdvancedLaunchLines() : [];
        ReloadInstanceDiagnostic(_advancedLaunchRoot, AdvancedLaunchData(lines.Count));
        var rows = CatalogNodes(_advancedLaunchRoot)["AdvancedLaunch.Rows"];
        _dynamic.Clear(rows);
        foreach (var line in lines.Skip(_advancedLaunchPage * 32).Take(32))
            _dynamic.Append(rows, DesktopMarkup.Dynamic(line.Template, line.Data, _shell.Renderer.LocalizeText));
    }
    private static AdvancedLaunchLine LaunchLine(string kind, params (string Key, string Value)[] values)
        => new("SettingsInstanceLaunch" + kind + ".d.nxml", values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    private static AdvancedLaunchLine RawLaunchLine(string value) => LaunchLine("Line", ("value", value));
    private IReadOnlyList<AdvancedLaunchLine> AdvancedLaunchLines()
    {
        var plan = _advancedLaunch?.Plan;
        if (_advancedLaunchTab is "manifest" or "metadata" or "lockfile")
        {
            if (_advancedLaunch?.Documents is not { } documents) return [LaunchLine("NoDocuments")];
            List<AdvancedLaunchLine> values = [];
            foreach (var document in documents.Documents.Where(item => item.Kind == _advancedLaunchTab))
            {
                values.Add(LaunchLine("Document", ("path", document.RelativePath), ("status", document.Status)));
                if (document.Truncated) values.Add(LaunchLine("DocumentTruncated"));
                values.AddRange(document.Lines.Select(RawLaunchLine));
            }
            return values.Count > 0 ? values : [LaunchLine("NoDocumentKind")];
        }
        if (_advancedLaunchTab == "trace") return AdvancedObservedLaunchTrace(plan);
        if (plan?.CapturedAt is not { } captured) return [LaunchLine("NoPlan")];
        if (_advancedLaunchTab == "classpath") return plan.ClasspathEntries.Count == 0 ? [LaunchLine("NoClasspath")] : plan.ClasspathEntries.Select(RawLaunchLine).ToArray();
        if (_advancedLaunchTab == "natives") return plan.NativeArchives.Count == 0 ? [LaunchLine("NoNatives")] : plan.NativeArchives.Select(RawLaunchLine).ToArray();
        List<AdvancedLaunchLine> result =
        [
            LaunchLine("Captured", ("timestamp", captured.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))),
            LaunchLine("Phase"), LaunchLine("Java", ("executable", plan.JavaExecutablePath)),
            LaunchLine("WorkingDirectory", ("directory", plan.WorkingDirectory)), LaunchLine("MainClass", ("mainClass", plan.MainClass)),
            LaunchLine("Loader", ("loader", plan.Loader)), LaunchLine("Heap", ("heap", InstanceNumber(plan.HeapLimitMiB))),
            LaunchLine("Safe", ("safe", InstanceDiagnosticTerm("safe." + Flag(plan.SafeLaunch)))),
            LaunchLine("Environment", ("names", string.Join(", ", plan.EnvironmentNames))), LaunchLine("Arguments")
        ];
        if (plan.Truncated) result.Add(LaunchLine("PlanTruncated"));
        result.AddRange(plan.Arguments.Select(RawLaunchLine)); return result;
    }
    private List<AdvancedLaunchLine> AdvancedObservedLaunchTrace(MinecraftLaunchPlanDiagnosticSnapshot? plan)
    {
        List<AdvancedLaunchLine> lines = [LaunchLine("TraceIntro")];
        if (plan?.CapturedAt is { } captured) lines.Add(LaunchLine("TraceCapture", ("timestamp", captured.ToString("O", CultureInfo.InvariantCulture))));
        if (_store.TryResolve(MinecraftProcessStateComposition.SessionsKey, out var state))
        {
            var sessions = _store.ReadCollection<MinecraftProcessSnapshot>(state).Items;
            for (int index = Math.Max(0, sessions.Count - 256); index < sessions.Count; index++)
            {
                var item = sessions[index];
                if (!Nexa.Core.PathIdentity.Comparer.Equals(item.InstanceDirectory, _instance)) continue;
                lines.Add(LaunchLine("TraceStart", ("timestamp", item.StartedAt.ToString("O", CultureInfo.InvariantCulture)),
                    ("session", item.SessionId.ToString()), ("state", InstanceProcessState(item.State))));
                if (item.EndedAt is { } ended) lines.Add(LaunchLine("TraceEnd", ("timestamp", ended.ToString("O", CultureInfo.InvariantCulture)),
                    ("exit", item.ExitCode is { } exit ? InstanceNumber(exit) : InstanceDiagnosticTerm("unknown"))));
            }
        }
        if (lines.Count == 1) lines.Add(LaunchLine("NoTrace"));
        return lines;
    }
    private void ReadAdvancedLaunchDiagnostics()
    {
        if (_advancedLaunchReading is not null || !InstanceDiagnosticsVisible) return;
        _advancedLaunchInstance = _instance;
        if (!_queries.TryResolve(MinecraftLaunchPlanDiagnosticContract.Query, out var planRoute)
            || !_queries.TryResolve(InstanceLocalDocumentsContract.Query, out var documentsRoute))
        { _advancedLaunch = new(null, null, "launch.missing"); ProjectAdvancedLaunchDiagnostics(); return; }
        _advancedLaunchStop?.Dispose(); _advancedLaunchStop = new(); string instance = _instance!;
        var cancellation = _advancedLaunchStop.Token;
        _advancedLaunchReading = ReadAsync(); WakeOnPlatformCompletion(_advancedLaunchReading); ProjectAdvancedLaunchDiagnostics();
        async Task<AdvancedLaunchRead> ReadAsync()
        {
            var plan = _queries.QueryAsync<MinecraftLaunchPlanDiagnosticQuery, MinecraftLaunchPlanDiagnosticSnapshot>(planRoute, new(instance), cancellationToken: cancellation).AsTask();
            var documents = _queries.QueryAsync<InstanceLocalDocumentsQuery, InstanceLocalDocumentsSnapshot>(documentsRoute, new(instance), cancellationToken: cancellation).AsTask();
            await Task.WhenAll(plan, documents).ConfigureAwait(false);
            return new(plan.Result.IsSuccess ? plan.Result.Value : null, documents.Result.IsSuccess ? documents.Result.Value : null,
                plan.Result.IsSuccess && documents.Result.IsSuccess ? null : "launch.partial");
        }
    }
}
