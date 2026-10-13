using System.Globalization;
using Nexa.Services.Accounts;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{

    internal void JoinServer(string instanceDirectory, string address)
    {
        if (LaunchBusy || _launchRequest is { IsCompleted: false }) return;
        string? root = Path.GetDirectoryName(Path.GetDirectoryName(instanceDirectory));
        if (!Nexa.Core.PathIdentity.Comparer.Equals(root, ReadCell(LaunchPageState.InstanceDirectoryKey))
            || Path.GetFileName(instanceDirectory) != ReadCell(LaunchPageState.SelectedInstanceKey))
        { _feedback.Warn(HomeCopy("launch.server-changed")); return; }
        _shell.Stage.Navigation.Replace(_launchPage);
        _launchRequest = StartLaunchAsync(Path.GetFileName(instanceDirectory), _launchPage, address);
    }

    private async Task StartLaunchAsync(string instanceId, XsrUiEntityId source, string? serverAddress = null)
    {
        if (_store.ReadAppliedValue(_libraryId) is MinecraftLibrarySnapshot { IsProvisional: true })
        {
            _feedback.Warn(HomeCopy("launch.refresh-required"));
            return;
        }
        IReadOnlyList<LaunchProfileView> profiles = ReadProfiles();
        int selected = SelectedAccountIndex;
        if (!profiles.Any(profile => profile.Index == selected))
        {
            _feedback.Warn(HomeCopy("account.need-login"));
            return;
        }

        if (!_minecraft.Commands.TryResolve(MinecraftRouteIds.Start, out XsrCommandId commandId))
        {
            _feedback.Error(HomeCopy("launch.route-unavailable"));
            return;
        }

        if (LaunchBusy) return;
        ShowLaunchingPage(source);

        // The launching page replicates the legacy launching card: reset facts, narrate the
        // pipeline through the launch progress cells, and return on failure or cancellation.
        Publish(LaunchPageState.LaunchingTitleKey, HomeCopy("launch.title.busy"));
        Publish(LaunchPageState.LaunchingNameKey, ReadCell(LaunchPageState.InstanceSummaryKey));
        Publish(LaunchPageState.LaunchingStageKey, HomeCopy("launch.stage.initialize"));
        Publish(LaunchPageState.LaunchingMethodKey, HomeCopy("launch.method.waiting"));
        Publish(LaunchPageState.LaunchingPercentKey, HomeFormat("Percent", new() { ["value"] = "0" }));
        Publish(LaunchPageState.LaunchingSpeedVisibleKey, false);
        Publish(LaunchPageState.LaunchingHintKey, LaunchWidgetHints.BuiltIn[_hintIndex]);
        RefreshLaunchingDisplay();

        XsrCommandDispatch dispatch = _minecraft.Commands.Dispatch(
            commandId,
            new MinecraftStartCommand(instanceId, selected)
            { MinecraftRootDirectory = ReadCell(LaunchPageState.InstanceDirectoryKey), ServerAddress = serverAddress },
            cancellationToken: _lifetimeCancellation.Token);
        XsrResult result = await dispatch.Completion.ConfigureAwait(false);
        if (_disposed)
        {
            return;
        }

        if (result.IsSuccess)
        {
            // The window confirmation (or its platform fallback) already passed: success
            // closes the page immediately so the user is back at the library while the game
            // warms up, instead of parking on a 游戏已启动 card until the session ends.
            _feedback.Info(HomeCopy("launch.started"));
            RequestCloseLaunchingPage();
            return;
        }

        _feedback.Error(HomeFormat("LaunchFailure", new() { ["error"] = result.Error?.Message ?? string.Empty }), localizeMessage: false);
        RequestCloseLaunchingPage();
    }


    private async Task CancelLaunchAsync()
    {
        if (!LaunchBusy)
        {
            return;
        }

        // Once the game is running there is no pipeline left to cancel — the button has become
        // "back" — so just leave the page without touching the process.
        bool launched = _store.ReadAppliedValue(_launchProgressId)
            is MinecraftLaunchProgressSnapshot snapshot && snapshot.IsLaunched;
        if (launched)
        {
            RequestCloseLaunchingPage();
            return;
        }

        Publish(LaunchPageState.LaunchingStageKey, HomeCopy("launch.cancel-requested"));
        if (_minecraft.Commands.TryResolve(MinecraftRouteIds.LaunchCancel, out XsrCommandId route))
        {
            await _minecraft.Commands.Dispatch(route, new MinecraftCancelLaunchCommand(),
                cancellationToken: _lifetimeCancellation.Token).Completion.ConfigureAwait(false);
        }

        if (!_disposed)
        {
            RequestCloseLaunchingPage();
        }
    }


    /// <summary>
    /// Requests the launching page to close from any thread. The close itself mutates the
    /// navigation stack, tree components, and focus — all render-thread state — so it is
    /// drained on the next frame preparation instead of running here.
    /// </summary>
    private void RequestCloseLaunchingPage()
    {
        Interlocked.Exchange(ref _pendingCloseLaunching, 1);
    }


    /// <summary>
    /// Opens the dedicated launching page (a navigation push, mirroring the subpage flow) and
    /// records where to restore focus when it closes.
    /// </summary>
    private void ShowLaunchingPage(XsrUiEntityId source)
    {
        if (_shell.Stage.Navigation.Current == _launchingPage) return;
        _launchingViaKeyboard = IsKeyboardIntent(source);
        _returnFocus.Push(source);
        _shell.Stage.Navigation.Push(_launchingPage);
        UpdateTitleBar();
        _shell.Renderer.Focus(_launchingEntities.GetValueOrDefault("LaunchingCancelButton"), _launchingViaKeyboard);
    }


    private async Task DecideAcquisitionAsync(bool approve)
    {
        if (!LaunchBusy
            || !_minecraft.Commands.TryResolve(MinecraftRouteIds.AcquireDecide, out XsrCommandId route))
        {
            _feedback.Error(HomeCopy("launch.java-decision-unavailable"));
            return;
        }

        XsrResult result = await _minecraft.Commands.Dispatch(
            route,
            new MinecraftDecideJavaAcquisitionCommand(approve),
            cancellationToken: _lifetimeCancellation.Token).Completion.ConfigureAwait(false);
        if (!_disposed && !result.IsSuccess)
        {
            _feedback.Error(HomeFormat("JavaDecisionFailure", new() { ["error"] = result.Error?.Message ?? string.Empty }), localizeMessage: false);
        }
    }


    private void CloseLaunchingPage()
    {
        UpdateLaunchButton();
        DismissAcquisitionDialog();
        if (_javaChoicePage.IsAssigned && _shell.Stage.Navigation.Current == _javaChoicePage)
        { _shell.Stage.Navigation.Pop(); _returnFocus.TryPop(out _); }
        if (_shell.Stage.Navigation.Current != _launchingPage)
        {
            return;
        }

        _ = _shell.Stage.Navigation.Pop();
        UpdateTitleBar();
        if (_returnFocus.TryPop(out XsrUiEntityId focus))
        {
            _shell.Renderer.Focus(focus, _launchingViaKeyboard);
        }
    }


    /// <summary>
    /// Projects the services launch progress cells into the overlay display strings: stage
    /// tokens become legacy stage labels, the progress fraction formats as whole percent, and
    /// the title switches to the launched state once the pipeline reports the game running.
    /// </summary>
    private void RefreshLaunchingDisplay()
    {
        string stage = ReadServiceCell(MinecraftLaunchProgressState.StageKey);
        if (stage.Length > 0)
        {
            Publish(LaunchPageState.LaunchingStageKey,
                HomeFind("launch.stage." + stage) ?? stage);
        }

        double progress = _store.ReadAppliedValue(_store.Resolve(MinecraftLaunchProgressState.ProgressKey)) is double value
            ? Math.Clamp(value, 0d, 1d)
            : 0d;
        Publish(LaunchPageState.LaunchingPercentKey, HomeFormat("Percent", new() { ["value"] = Math.Round(progress * 100).ToString(CultureInfo.InvariantCulture) }));

        string method = ReadServiceCell(MinecraftLaunchProgressState.MethodKey);
        Publish(LaunchPageState.LaunchingMethodKey,
            method.Length == 0 ? HomeCopy("launch.method.waiting") : HomeFind("launch.method." + method) ?? method);

        string speed = ReadServiceCell(MinecraftLaunchProgressState.SpeedKey);
        Publish(LaunchPageState.LaunchingSpeedKey, speed);
        Publish(LaunchPageState.LaunchingSpeedVisibleKey, speed.Length > 0);

        bool launched = _store.ReadAppliedValue(_store.Resolve(MinecraftLaunchProgressState.LaunchedKey)) is bool running && running;
        Publish(LaunchPageState.LaunchingTitleKey, launched ? HomeCopy("launch.title.started") : HomeCopy("launch.title.busy"));
        Publish(LaunchPageState.LaunchingCancelLabelKey, launched ? HomeCopy("launch.back") : HomeCopy("launch.cancel"));
    }


    /// <summary>
    /// Projects the acquisition cells into the shared window-internal dialog. The feedback
    /// service is thread-safe; its presenter performs all PXML mutations at frame preparation.
    /// </summary>
    private void RefreshAcquisitionPrompt()
    {
        bool pending = _store.ReadAppliedValue(_store.Resolve(MinecraftLaunchProgressState.AcquirePendingKey)) is bool waiting && waiting;
        string component = ReadServiceCell(MinecraftLaunchProgressState.AcquireComponentKey);
        int major = _store.ReadAppliedValue(_store.Resolve(MinecraftLaunchProgressState.AcquireMajorKey)) is int version ? version : 0;
        if (LaunchBusy && pending && major > 0 && component.Length > 0)
        {
            _javaAcquisitionDialog = _feedback.ShowDialog(
                "minecraft.java.acquire",
                HomeFormat("JavaRequiredTitle", new() { ["major"] = major.ToString(CultureInfo.InvariantCulture) }),
                HomeFormat("JavaRequiredDescription", new() { ["major"] = major.ToString(CultureInfo.InvariantCulture) }),
                HomeCopy("launch.java.download"),
                HomeCopy("launch.cancel"),
                approve => _ = DecideAcquisitionAsync(approve),
                HomeCopy("launch.java.select"), ShowJavaVersionChoice, localizeMessage: false, localizeTitle: false);
        }
        else if (!pending && _javaAcquisitionDialog is { } dialog)
        {
            _feedback.DismissDialog(dialog);
            _javaAcquisitionDialog = null;
        }
    }


    private void RefreshPreflightPrompt()
    {
        var prompt = _store.ReadAppliedValue(_store.Resolve(LaunchPreflightContract.StateKey)) as LaunchPreflightPrompt;
        if (prompt is null)
        {
            if (_preflightDialog is { } old) _feedback.DismissDialog(old);
            _preflightDialog = _preflightAttempt = null;
            return;
        }
        if (_preflightAttempt == prompt.Attempt) return;
        _preflightAttempt = prompt.Attempt;
        bool blocked = prompt.Report.Issues.Any(static issue => issue.Severity == Nexa.Services.Capabilities.PreflightSeverity.Blocked);
        string message = string.Join(HomeTerms.Value["launch.preflight.separator"], prompt.Report.CollapsedIssues.Select(issue =>
            HomeFormat("PreflightIssue", new() { ["title"] = _shell.Renderer.LocalizeText(issue.Title), ["description"] = _shell.Renderer.LocalizeText(issue.Description) })));
        _preflightDialog = _feedback.ShowDialog("minecraft.preflight", blocked ? HomeCopy("launch.preflight.blocked") : HomeCopy("launch.preflight.confirm"),
            message, blocked ? HomeCopy("launch.back") : HomeCopy("launch.preflight.proceed"), HomeCopy("launch.cancel"), approve => _ = DecidePreflightAsync(prompt.Attempt, approve && !blocked), localizeMessage: false);
    }


    private async Task DecidePreflightAsync(Guid attempt, bool proceed)
    {
        if (!_minecraft.Commands.TryResolve(LaunchPreflightContract.DecisionCommand, out XsrCommandId route)) return;
        await _minecraft.Commands.Dispatch(route, new LaunchPreflightDecision(attempt, proceed),
            cancellationToken: _lifetimeCancellation.Token).Completion.ConfigureAwait(false);
    }


    private void DismissAcquisitionDialog()
    {
        if (_javaAcquisitionDialog is not { } dialog)
        {
            return;
        }

        _feedback.DismissDialog(dialog);
        _javaAcquisitionDialog = null;
    }


    private string ReadServiceCell(XsrSemanticId key) =>
        Convert.ToString(_store.ReadAppliedValue(_store.Resolve(key)), CultureInfo.InvariantCulture) ?? string.Empty;


    /// <summary>
    /// Receives host state publications. Launch progress changes refresh the overlay; a
    /// terminal process session while the game was reported launched closes it, mirroring the
    /// legacy flow that returns to the launch page when the game exits.
    /// </summary>
    private sealed class LaunchingStateObserver(LaunchPageController owner) : IXsrStateObserver
    {
        public void OnChanged(XsrStateChange change)
        {
            if (change.SemanticId == LaunchPreflightContract.StateKey) { owner.RefreshPreflightPrompt(); return; }
            if (change.SemanticId == MinecraftLibraryContract.StateKey) { owner.ProjectLibrary(); return; }
            if (change.SemanticId.Equals(MinecraftLaunchProgressState.StageKey)
                || change.SemanticId.Equals(MinecraftLaunchProgressState.ProgressKey)
                || change.SemanticId.Equals(MinecraftLaunchProgressState.MethodKey)
                || change.SemanticId.Equals(MinecraftLaunchProgressState.SpeedKey)
                || change.SemanticId.Equals(MinecraftLaunchProgressState.LaunchedKey))
            {
                owner.RefreshLaunchingDisplay();
                return;
            }

            if (change.SemanticId.Equals(MinecraftLaunchProgressState.AcquirePendingKey)
                || change.SemanticId.Equals(MinecraftLaunchProgressState.AcquireComponentKey)
                || change.SemanticId.Equals(MinecraftLaunchProgressState.AcquireMajorKey))
            {
                owner.RefreshAcquisitionPrompt();
                return;
            }

            // The narration belongs to one session: only THAT game's terminal state closes the
            // page. Other running games must keep the flow alive.
            if (owner.LaunchBusy
                && change.SemanticId.Equals(MinecraftProcessStateComposition.SessionsKey)
                && owner.LaunchedSessionId() is { } sessionId
                && sessionId != Guid.Empty
                && owner.IsSessionTerminal(sessionId))
            {
                owner.RequestCloseLaunchingPage();
            }
        }
    }


    /// <summary>The session this narration launched, from the coherent snapshot truth.</summary>
    private Guid? LaunchedSessionId() =>
        _store.ReadAppliedValue(_launchProgressId) is MinecraftLaunchProgressSnapshot snapshot
            ? snapshot.SessionId
            : null;


    private bool IsSessionTerminal(Guid sessionId) =>
        _store.ReadCollection<MinecraftProcessSnapshot>(_store.Resolve(MinecraftProcessStateComposition.SessionsKey))
            .Items.Any(snapshot => snapshot.SessionId == sessionId
                && snapshot.State is MinecraftProcessState.Exited
                    or MinecraftProcessState.Failed
                    or MinecraftProcessState.Cancelled);


    private IReadOnlyList<LaunchProfileView> ReadProfiles() =>
        _store.ReadCollection<LaunchProfileView>(_accountProfilesId).Items;

}
