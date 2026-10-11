using System.Globalization;
using System.Text.Json.Nodes;
using Nexa.Services.Accounts;
using Nexa.Services.Logging;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Libraries;
using Nexa.Services.Minecraft.ModLoaders;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Scheduling;
using Nexa.Services.Settings;
using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Launch;

public sealed partial class MinecraftLaunchCoordinator
{

    public ValueTask<XsrResult> StartAsync(
        string instanceId,
        int accountIndex,
        CancellationToken cancellationToken = default)
        => StartAsync(instanceId, accountIndex, _minecraftRootDirectory, cancellationToken);

    public ValueTask<XsrResult> StartServerAsync(string instanceId, int accountIndex, string? minecraftRootDirectory, string serverAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serverAddress) || serverAddress.Length > 512 || serverAddress.Any(char.IsWhiteSpace)
            || serverAddress.Any(char.IsControl) || serverAddress.Contains("://", StringComparison.Ordinal))
            return ValueTask.FromResult(XsrResult.Failure(MinecraftErrors.InvalidRequest("服务器地址无效。")));
        return StartCoreAsync(instanceId, accountIndex, minecraftRootDirectory ?? _minecraftRootDirectory, serverAddress, cancellationToken);
    }

    public ValueTask<XsrResult> StartAsync(
        string instanceId, int accountIndex, string minecraftRootDirectory,
        CancellationToken cancellationToken = default)
        => StartCoreAsync(instanceId, accountIndex, minecraftRootDirectory, null, cancellationToken);

    private async ValueTask<XsrResult> StartCoreAsync(string instanceId, int accountIndex, string minecraftRootDirectory, string? serverAddress,
        CancellationToken cancellationToken)
    {
        using LogOperation? operation = _log?.BeginOperation("Launch", "StartMinecraft", $"instance={instanceId} account_index={accountIndex}");
        CancellationTokenSource launchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_launchGate)
        {
            if (_activeLaunch is not null)
            {
                operation?.Reject(MinecraftErrors.LaunchAlreadyActiveCode.Value);
                launchCancellation.Dispose();
                return XsrResult.Failure(MinecraftErrors.LaunchAlreadyActive());
            }

            _activeLaunch = launchCancellation;
        }

        IWorkQuietLease? quiet = null;
        IDisposable? priority = null;
        try
        {
            priority = WorkScheduler?.UsePriority(WorkPriority.Critical);
            quiet = WorkScheduler?.EnterQuiet();
            XsrResult result = await StartLockedAsync(instanceId, accountIndex, minecraftRootDirectory, serverAddress, launchCancellation, operation, quiet).ConfigureAwait(false);
            if (result.IsSuccess) quiet = null; // Session termination / confirmed-window grace owns it now.
            return result;
        }
        finally
        {
            // Only this pipeline's registration may be cleared: a newer launch keeps its own.
            lock (_launchGate)
            {
                if (ReferenceEquals(_activeLaunch, launchCancellation))
                {
                    _activeLaunch = null;
                }
            }

            try { quiet?.Dispose(); }
            finally { priority?.Dispose(); launchCancellation.Dispose(); }
        }
    }

    private async ValueTask<XsrResult> StartLockedAsync(
        string instanceId,
        int accountIndex,
        string minecraftRootDirectory,
        string? serverAddress,
        CancellationTokenSource launchCancellation,
        LogOperation? operation,
        IWorkQuietLease? quiet)
    {
        CancellationToken launchToken = launchCancellation.Token;
        // Set as soon as the game process exists, so a cancellation before the window
        // confirmation can terminate the process we created (the game must not pop up after
        // the user has already cancelled the launch).
        Process.MinecraftProcessSession? startedSession = null;
        try
        {
            _progress?.Start(instanceId, minecraftRootDirectory);
            operation?.Stage("prepare");
            XsrResult<MinecraftLaunchPreparation> preparation = await PrepareAsync(
                instanceId, accountIndex, minecraftRootDirectory, launchToken).ConfigureAwait(false);
            if (!preparation.IsSuccess)
            {
                operation?.Reject(preparation.Error!.Code.Value);
                _progress?.Stop();
                return XsrResult.Failure(preparation.Error!);
            }

            if (serverAddress is not null) preparation = XsrResult.Success(preparation.Value with { Request = preparation.Value.Request with { Server = serverAddress } });
            string method = preparation.Value.Request.IdentityMode.ToString().ToLowerInvariant();
            operation?.Stage("create_plan");
            MinecraftLaunchPlan plan = default!;
            await RunStageAsync(
                MinecraftLaunchStages.GetArguments,
                MinecraftLaunchStages.LoginWeight
                    + MinecraftLaunchStages.CompleteFilesWeight
                    + MinecraftLaunchStages.GetJavaWeight,
                MinecraftLaunchStages.GetArgumentsWeight,
                method,
                token =>
                {
                    plan = MinecraftLaunchPlanner.CreatePlan(preparation.Value.Request);
                    return Task.CompletedTask;
                },
                launchToken).ConfigureAwait(false);

            if (_preflight is not null)
            {
                operation?.Stage("preflight");
                _progress?.Report(new MinecraftLaunchStageReport("preflight", MinecraftLaunchStages.ProgressAt(36), Method: method));
                if (!await _preflight.CheckAsync(minecraftRootDirectory, instanceId, plan, launchToken).ConfigureAwait(false))
                {
                    operation?.Reject("preflight_declined"); _progress?.Stop();
                    return XsrResult.Failure(MinecraftErrors.LaunchPreparationFailed("启动预检未通过或已取消。"));
                }
            }
            operation?.Stage("execute_plan", $"native_archives={plan.NativeLibraries.Count}");
            // Stage boundaries as named constants: every hand-written +Weight chain eventually
            // desynced from the weight table, so the math is written exactly once here.
            const double afterLogin = MinecraftLaunchStages.LoginWeight;
            const double afterCompleteFiles = afterLogin + MinecraftLaunchStages.CompleteFilesWeight;
            const double afterJava = afterCompleteFiles + MinecraftLaunchStages.GetJavaWeight;
            const double afterArguments = afterJava + MinecraftLaunchStages.GetArgumentsWeight;
            const double afterExtract = afterArguments + MinecraftLaunchStages.ExtractNativesWeight;
            const double afterPreLaunch = afterExtract + MinecraftLaunchStages.PreLaunchWeight;
            const double afterCustomCommand = afterPreLaunch + MinecraftLaunchStages.CustomCommandWeight;
            const double afterStart = afterCustomCommand + MinecraftLaunchStages.StartProcessWeight;
            _progress?.Report(new MinecraftLaunchStageReport(
                MinecraftLaunchStages.ExtractNatives,
                MinecraftLaunchStages.ProgressAt(afterArguments),
                Method: method));
            Process.MinecraftProcessSession session = startedSession = await _executor.ExecuteAsync(
                plan,
                preparation.Value.Instance.Id,
                stage: stageToken =>
                {
                    double? completed = stageToken switch
                    {
                        MinecraftLaunchStages.PreLaunch => afterExtract,
                        MinecraftLaunchStages.CustomCommand => afterPreLaunch,
                        MinecraftLaunchStages.StartProcess => afterCustomCommand,
                        _ => null,
                    };
                    if (completed is { } weight)
                        _progress?.Report(new MinecraftLaunchStageReport(stageToken,
                            MinecraftLaunchStages.ProgressAt(weight), Method: method));
                },
                launchToken)
                .ConfigureAwait(false);
            Guid sessionId = session.Snapshot.SessionId;
            // The narration truth must not outlive the game. A JVM can die between process
            // creation and this point, and the terminal Changed event has then already fired —
            // so this is the classic subscribe-then-recheck: the handler is named so it can
            // unsubscribe, and the current snapshot is re-run after subscribing to catch a
            // transition that happened before we listened.
            void OnSessionChanged(MinecraftProcessSnapshot snapshot)
            {
                if (snapshot.SessionId != sessionId
                    || snapshot.State is not (MinecraftProcessState.Exited
                        or MinecraftProcessState.Failed
                        or MinecraftProcessState.Cancelled))
                {
                    return;
                }

                session.Changed -= OnSessionChanged;
                quiet?.Dispose();
                _progress?.Stop(sessionId);
            }

            session.Changed += OnSessionChanged;
            // wait_window ENTERS at its legacy boundary (42/44); its completion folds into
            // the end report (44/44).
            // From here on every report carries the session id, so a Stop(sessionId) reset
            // can match the narration it belongs to.
            _progress?.Report(new MinecraftLaunchStageReport(
                MinecraftLaunchStages.WaitWindow,
                MinecraftLaunchStages.ProgressAt(afterStart),
                Method: method,
                SessionId: sessionId));
            GameWindowWaitResult wait = await WaitForGameWindowAsync(session, launchToken).ConfigureAwait(false);
            if (wait == GameWindowWaitResult.ProcessExited)
            {
                // The JVM died before presenting a window: this is a failed launch, not a
                // launched one — the failure path closes the launching page and feeds crash
                // analysis downstream. The narration reset here also covers the exit that
                // happened before the terminal callback could be observed by readers.
                _progress?.Stop(sessionId);
                operation?.Reject(MinecraftErrors.ExitedBeforeWindowCode.Value);
                return XsrResult.Failure(MinecraftErrors.ExitedBeforeWindow());
            }

            if (wait == GameWindowWaitResult.Visible) quiet?.ReleaseAfter(TimeSpan.FromSeconds(15));
            else quiet?.Dispose(); // Fallback is not a window/stability confirmation.
            _progress?.Report(new MinecraftLaunchStageReport(
                MinecraftLaunchStages.End,
                MinecraftLaunchStages.ProgressAt(MinecraftLaunchStages.Total),
                IsLaunched: true,
                Method: method,
                SessionId: sessionId));
            OnSessionChanged(session.Snapshot);
            operation?.Complete($"session={sessionId} pid={session.Snapshot.ProcessId} state={session.Snapshot.State}");
            return XsrResult.Success();
        }
        catch (OperationCanceledException) when (launchToken.IsCancellationRequested)
        {
            operation?.Cancel();
            // Pre-confirmation cancellation kills the process this pipeline created; once the
            // game is confirmed running the UI's back action handles it instead.
            if (startedSession is { } session
                && session.Snapshot.State is MinecraftProcessState.Created or MinecraftProcessState.Running)
            {
                _log?.Info("Launch", $"Cancelling the pre-confirmation game process session={session.Snapshot.SessionId}.");
                session.Cancel();
            }

            _progress?.Stop();
            return XsrResult.Failure(MinecraftErrors.LaunchFailed("the launch was cancelled."));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or System.ComponentModel.Win32Exception)
        {
            operation?.Fail(exception);
            _progress?.Stop();
            return XsrResult.Failure(MinecraftErrors.LaunchFailed(exception.Message));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not AccessViolationException)
        {
            operation?.Fail(exception);
            _progress?.Stop();
            throw;
        }
    }

    internal enum GameWindowWaitResult
    {
        Visible,
        Unsupported,
        TimedOut,
        ProcessExited,
    }

    /// <summary>
    /// The legacy wait-for-window stage: the narration stays at the wait stage until the game
    /// process presents a visible window (or the limit lapses — a headless or slow-windowing
    /// game still counts as launched rather than blocking the flow forever).
    /// </summary>
    private ValueTask<GameWindowWaitResult> WaitForGameWindowAsync(
        Process.MinecraftProcessSession session,
        CancellationToken cancellationToken) =>
        WaitForGameWindowAsync(_windowProbe, _log, session, cancellationToken, _gameWindowAppeared);

    internal static async ValueTask<GameWindowWaitResult> WaitForGameWindowAsync(
        IMinecraftWindowProbe windowProbe,
        LogService? log,
        Process.MinecraftProcessSession session,
        CancellationToken cancellationToken,
        Action<int>? gameWindowAppeared = null)
    {
        int processId = session.Snapshot.ProcessId;
        long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A JVM that already died will never present a window; the terminal callback below
            // resets the narration, so waiting would only burn the limit.
            if (session.Snapshot.State is MinecraftProcessState.Exited
                or MinecraftProcessState.Failed
                or MinecraftProcessState.Cancelled)
            {
                log?.Info("Launch", $"Game process already ended before its window appeared pid={processId}.");
                return GameWindowWaitResult.ProcessExited;
            }

            MinecraftWindowProbeResult probe = await windowProbe
                .ProbeAsync(processId, cancellationToken)
                .ConfigureAwait(false);
            if (probe == MinecraftWindowProbeResult.Visible)
            {
                session.ConfirmGameWindow();
                log?.Info("Launch", $"Game window confirmed pid={processId}.");
                // The window exists now: the host detaches it from the launcher's taskbar group
                // while the game keeps its own icon. Calling earlier races window creation.
                try
                {
                    gameWindowAppeared?.Invoke(processId);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and not AccessViolationException)
                {
                    log?.Warn("Launch", $"Game window integration failed pid={processId}: {exception.GetType().Name}: {exception.Message}");
                }
                return GameWindowWaitResult.Visible;
            }

            if (probe == MinecraftWindowProbeResult.Unsupported)
            {
                // No window detection on this platform: a wait could only burn its limit, so
                // the legacy behavior degrades to "process started counts as launched".
                log?.Info("Launch", $"Window detection unsupported; skipping the wait pid={processId}.");
                return GameWindowWaitResult.Unsupported;
            }

            if (System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= GameWindowWaitLimit)
            {
                log?.Warn("Launch", $"No game window appeared within the limit pid={processId}; continuing.");
                return GameWindowWaitResult.TimedOut;
            }

            await Task.Delay(GameWindowPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs one pipeline stage with the legacy heartbeat: the stage enters at its completed
    /// weight, soft progress advances by one step per heartbeat (clamped inside the stage),
    /// and the stage completes at its full weight. Stages without a progress publisher run
    /// unchanged.
    /// </summary>
    private async ValueTask RunStageAsync(
        string stage,
        double completedBefore,
        double stageWeight,
        string method,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken,
        bool heartbeat = true)
    {
        if (_progress is null)
        {
            await work(cancellationToken).ConfigureAwait(false);
            return;
        }

        _progress.Report(new MinecraftLaunchStageReport(
            stage,
            MinecraftLaunchStages.ProgressAt(completedBefore),
            Method: method));
        Task workTask = work(cancellationToken);
        double softFraction = 0d;
        // Stages that publish their own real progress opt out of the heartbeat: the two
        // clocks fight, and the display flickers between the real value and the heartbeat's
        // ceiling for as long as the stage runs.
        while (heartbeat && !workTask.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            softFraction = Math.Min(StageHeartbeatCeiling, softFraction + StageHeartbeatStep);
            _progress.Report(new MinecraftLaunchStageReport(
                stage,
                MinecraftLaunchStages.ProgressAt(completedBefore + (stageWeight * softFraction)),
                Method: method));
            try
            {
                await Task.WhenAny(workTask, Task.Delay(StageHeartbeatInterval, cancellationToken))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (workTask.IsCompleted)
            {
                break;
            }
        }

        await workTask.ConfigureAwait(false);
        _progress.Report(new MinecraftLaunchStageReport(
            stage,
            MinecraftLaunchStages.ProgressAt(completedBefore + stageWeight),
            Method: method));
    }
}
