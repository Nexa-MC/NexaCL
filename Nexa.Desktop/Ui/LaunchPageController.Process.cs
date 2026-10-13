using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Crash;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{
    private readonly Dictionary<Guid, (XsrUiEntityId Power, XsrUiEntityId Logs)> _processDocks = [];
    private readonly HashSet<Guid> _shownFailures = [];
    private XsrUiEntityId _javaChoicePage;
    private int _showJavaChoices, _returnFromJavaChoice;
    private NxmlDynamicContent? _javaChoiceContent;
    private XsrUiEntityId _javaChoiceWorkspace;
    private long _processRevision = -1;
    private string _processCopyStamp = "";
    private ProcessLogPageController? _processLogPage;
    private DesktopDynamicLayer? _processLayer;
    internal Func<string, Task>? CopyProcessLogText { get; set; }
    internal Task WaitForProcessLogsAsync() => _processLogPage?.WaitUntilIdle() ?? Task.CompletedTask;

    private void ShowJavaVersionChoice()
    {
        Interlocked.Exchange(ref _showJavaChoices, 1);
        Publish(LaunchPageState.LaunchingStageKey, DesktopPresentationText.Raw("java.choose"));
    }

    private void ProjectProcessFeedback()
    {
        if (Interlocked.Exchange(ref _returnFromJavaChoice, 0) != 0 && _shell.Stage.Navigation.Current == _javaChoicePage)
        { _shell.Stage.Navigation.Pop(); _returnFocus.TryPop(out _); UpdateTitleBar(); }
        if (Interlocked.Exchange(ref _showJavaChoices, 0) != 0)
        {
            var choices = _store.ReadAppliedValue(_store.Resolve(MinecraftLaunchProgressState.JavaChoicesKey)) as IReadOnlyList<int>;
            var data = new Dictionary<string, string>();
            foreach (int major in new[] { 8, 16, 17, 21, 25 })
                data["visible" + major] = (choices is null || choices.Contains(major)) ? "true" : "false";
            var fragment = DesktopMarkup.Dynamic("JavaChoices.d.nxml", data);
            if (!_javaChoicePage.IsAssigned)
            {
                _javaChoicePage = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("JavaChoicePage.nxml"), _shell.Tree, _store);
                _javaChoiceContent = new(_shell.Tree, _store);
                _javaChoiceWorkspace = _javaChoiceContent.Append(_shell.Tree.Children(_javaChoicePage).Single(), fragment);
            }
            else _javaChoiceContent!.Reload(_javaChoiceWorkspace, fragment);
            DismissAcquisitionDialog();
            OpenSubpage(_javaChoicePage, _launchingPage);
        }
        if (!_store.TryResolve(MinecraftProcessStateComposition.SessionsKey, out var sessionsId)) return;
        var sessions = _store.ReadCollection<MinecraftProcessSnapshot>(sessionsId);
        string stamp = ProcessTerm("task.empty");
        if (_processCopyStamp != stamp) { _processCopyStamp = stamp; _processRevision = -1; }
        if (_processRevision != sessions.Revision)
        {
            _processRevision = sessions.Revision;
            var running = sessions.Items
                .Where(item => item.State is MinecraftProcessState.Created or MinecraftProcessState.Running).ToArray();
            var visibleSessions = running.Length > 0 ? running : sessions.Items.OrderByDescending(item => item.StartedAt).Take(1).ToArray();
            foreach (var id in _processDocks.Keys.Except(visibleSessions.Select(item => item.SessionId)).ToArray())
            {
                var dock = _processDocks[id];
                RetireProcessButton(dock.Power);
                RetireProcessButton(dock.Logs);
                _processDocks.Remove(id);
            }
            for (int i = 0; i < visibleSessions.Length; i++)
            {
                var session = visibleSessions[i];
                if (!_processDocks.TryGetValue(session.SessionId, out var dock))
                {
                    dock = (CreateProcessButton(session, "stop"),
                        CreateProcessButton(session, "logs"));
                    _processDocks.Add(session.SessionId, dock);
                }
                else
                {
                    _processLayer!.Reload(dock.Power, "ProcessStopBubble.d.nxml", ProcessBubbleData(session, "stop"));
                    _processLayer.Reload(dock.Logs, "ProcessLogsBubble.d.nxml", ProcessBubbleData(session, "logs"));
                }
            }
        }
        bool viewingLogs = _processLogPage is { } logPage && _shell.Stage.Navigation.Current == logPage.Page;
        foreach (var pair in _processDocks)
        {
            var session = sessions.Items.FirstOrDefault(item => item.SessionId == pair.Key);
            DesktopBubbleLayout.SetVisible(_shell, _store, pair.Value.Power, !viewingLogs
                && session?.State is MinecraftProcessState.Created or MinecraftProcessState.Running);
            DesktopBubbleLayout.SetVisible(_shell, _store, pair.Value.Logs, !viewingLogs);
        }
        DesktopBubbleLayout.Arrange(_shell);
        if (_store.TryResolve(MinecraftProcessStateComposition.FailuresKey, out var failuresId))
        {
            var failures = _store.ReadCollection<MinecraftProcessFailure>(failuresId).Items;
            _shownFailures.IntersectWith(failures.Select(failure => failure.SessionId));
            foreach (var failure in failures)
                if (!_shownFailures.Contains(failure.SessionId) && TryShowCrashDialog(failure))
                { _shownFailures.Add(failure.SessionId); break; }
        }
    }

    private void RetireProcessButton(XsrUiEntityId entity)
    {
        // Hidden viewer docks no longer need an exit animation. The shared dock helper
        // intentionally leaves already-hidden entities alive, so retire them explicitly.
        if (!_shell.Tree.GetComponent<XsrUiElement>(entity)!.IsVisible
            || _shell.Tree.GetComponent<XsrUiOverlayMotion>(entity)!.IsClosing)
            _shell.Tree.Destroy(entity);
        else DesktopBubbleLayout.SetVisible(_shell, _store, entity, false, destroy: true);
    }

    private XsrUiEntityId CreateProcessButton(MinecraftProcessSnapshot session, string action)
    {
        _processLayer ??= new(_shell, _store, "ShellBubbleLayer.nxml");
        var entity = _processLayer.Append(action == "stop" ? "ProcessStopBubble.d.nxml" : "ProcessLogsBubble.d.nxml",
            ProcessBubbleData(session, action));
        DesktopBubbleLayout.Register(_shell, entity, 2);
        return entity;
    }

    private Dictionary<string, string> ProcessBubbleData(MinecraftProcessSnapshot session, string action) => new()
    {
        ["key"] = "process-" + action + "-" + session.SessionId,
        ["command"] = "ui.process." + action + "." + session.SessionId.ToString("N"),
        ["caption"] = ProcessFormat(action == "stop" ? "ProcessStopCaption" : "ProcessLogsCaption", new() { ["instance"] = session.InstanceId }),
    };

    private bool HandleProcessIntent(DesktopUiIntentEventArgs e)
    {
        string command = e.Intent.Command.Value;
        if (_processLogPage is { } logs && _shell.Stage.Navigation.Current == logs.Page
            && (command == "ui.page.back" || command.StartsWith("ui.navigation.", StringComparison.Ordinal)))
            logs.Leave();
        if (command.StartsWith("ui.java.major.", StringComparison.Ordinal) && int.TryParse(command[14..], out int major))
        {
            _ = SelectJavaMajorAsync(major); return true;
        }
        if (command.StartsWith("ui.process.stop.", StringComparison.Ordinal) && Guid.TryParseExact(command[16..], "N", out var id))
        {
            _feedback.ShowDialog("minecraft.stop." + id, ProcessTerm("process.confirm"), ProcessTerm("process.warning"), ProcessTerm("process.stop"), ProcessTerm("common.cancel"),
                confirmed => { if (confirmed) _ = StopProcessAsync(id); });
            return true;
        }
        if (command.StartsWith("ui.process.logs.", StringComparison.Ordinal)
            && Guid.TryParseExact(command[16..], "N", out var logSession))
        {
            _processLogPage ??= new(_shell, _intents, _minecraft.Queries, _store, _feedback,
                text => CopyProcessLogText?.Invoke(text) ?? Task.FromException(new InvalidOperationException(DesktopPresentationText.Raw("log.clipboard"))));
            OpenSubpage(_processLogPage.Page, e.Intent.Source);
            _processLogPage.Open(logSession);
            return true;
        }
        return command.StartsWith("ui.process.logs.", StringComparison.Ordinal);
    }

    private async Task StopProcessAsync(Guid id)
    {
        if (!_minecraft.Commands.TryResolve(MinecraftRouteIds.ProcessCancel, out var route)) return;
        try
        {
            var result = await Task.Run(async () => await _minecraft.Commands.Dispatch(route, new MinecraftCancelProcessCommand(id), cancellationToken: _lifetimeCancellation.Token).Completion.ConfigureAwait(false)).ConfigureAwait(false);
            if (!_disposed && !result.IsSuccess) _feedback.Error(result.Error?.Message ?? ProcessTerm("process.failed"));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException)
        { if (!_disposed) _feedback.Error(ProcessFormat("ProcessStopFailed", new() { ["message"] = ex.Message }), localizeMessage: false); }
    }

    private async Task SelectJavaMajorAsync(int major)
    {
        if (Interlocked.Exchange(ref _pickingJava, 1) != 0) return;
        try
        {
            if (!_minecraft.Commands.TryResolve(MinecraftRouteIds.JavaVersionSelect, out var route)) return;
            _feedback.Info(ProcessFormat("JavaSearching", new() { ["major"] = major.ToString(System.Globalization.CultureInfo.CurrentCulture) }), localizeMessage: false);
            var result = await _minecraft.Commands.Dispatch(route, new MinecraftSelectJavaVersionCommand(major), cancellationToken: _lifetimeCancellation.Token).Completion.ConfigureAwait(false);
            if (!_disposed && !result.IsSuccess) _feedback.Error(result.Error?.Message ?? ProcessTerm("java.failed"));
            else if (!_disposed)
            {
                Interlocked.Exchange(ref _returnFromJavaChoice, 1);
                Publish(LaunchPageState.LaunchingStageKey, DesktopPresentationText.Raw("java.preparing"));
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException)
        { if (!_disposed) _feedback.Error(ProcessFormat("JavaChooseFailed", new() { ["message"] = ex.Message }), localizeMessage: false); }
        finally { Interlocked.Exchange(ref _pickingJava, 0); }
    }

    private string ProcessTerm(string key) => DesktopPresentationText.Term(key, _shell.Renderer.LocalizeText);
    private string ProcessFormat(string template, Dictionary<string, string> data)
        => DesktopPresentationText.Format(template, data, _shell.Renderer.LocalizeText);

    private string CrashSummary(MinecraftLaunchFaultReport report) => report.Code switch
    {
        MinecraftLaunchFaultCode.OutOfMemory => ProcessTerm("crash.memory"),
        MinecraftLaunchFaultCode.JavaRuntimeIncompatible => ProcessTerm("crash.java"),
        MinecraftLaunchFaultCode.MissingModDependency => ProcessTerm("crash.dependency"),
        MinecraftLaunchFaultCode.ModConflict => ProcessTerm("crash.conflict"),
        MinecraftLaunchFaultCode.GraphicsInitializationFailed => ProcessTerm("crash.graphics"),
        MinecraftLaunchFaultCode.MainClassMissing or MinecraftLaunchFaultCode.ClasspathDependencyMissing => ProcessTerm("crash.files"),
        MinecraftLaunchFaultCode.NativeLibraryFailed => ProcessTerm("crash.natives"),
        _ => ProcessTerm("crash.unknown"),
    };
}
