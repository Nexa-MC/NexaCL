using Nexa.Services.Updates;
using Nexa.Xsr;

namespace Nexa.Desktop;

internal sealed record DesktopStartupUpdate(NexaUpdateQuery Query, XsrResult<NexaUpdateStatus>? Discovery,
    bool InstallAttempted, bool RestartRequired)
{
    internal static async Task<DesktopStartupUpdate> RunAsync(IAutomaticUpdateControl control,
        NexaUpdateQuery query, bool check, Func<NexaUpdateQuery, CancellationToken, Task<XsrResult<NexaUpdateStatus>>> discover,
        Action<string> stage, Action<Exception> onFailure, CancellationToken token)
    {
        XsrResult<NexaUpdateStatus>? discovery = null;
        bool installing = false;
        try
        {
            var state = await control.ReadAsync(token).ConfigureAwait(false);
            if (!state.CanInstall) return new(query, null, false, false);
            string? target = state.Version;
            string? channel = state.Channel;
            if (state.Phase is not ("verifying" or "downloading" or "preparing" or "activating" or "paused"))
            {
                if (!check) return new(query, null, false, false);
                stage("startup_update_discovery");
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
                budget.CancelAfter(Ui.DesktopStartupReadiness.OnlineBudget);
                discovery = await discover(query, budget.Token).WaitAsync(budget.Token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                target = discovery.IsSuccess ? discovery.Value?.Offer?.Version : null;
                channel = query.Channel;
                // A failed bootstrap redirect must not repeatedly install and restart the same slot.
                if (state.Phase == "complete" && state.Version == target)
                    return new(query, discovery, false, false);
            }
            if (channel is not ("stable" or "beta" or "alpha") || !UpdateVersion.TryParse(target, out var offered)
                || !UpdateVersion.TryParse(query.CurrentVersion, out var running) || offered <= running)
                return new(query, discovery, false, false);
            token.ThrowIfCancellationRequested();
            installing = true;
            stage("startup_install_update");
            var committed = await control.InstallAsync(target!, channel, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (committed.Phase != "complete" || committed.Version != target || committed.Channel != channel)
                throw new IOException("The launcher update did not commit the requested version and channel.");
            return new(query, discovery, true, true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception failure) when (failure is not OutOfMemoryException and not AccessViolationException)
        {
            onFailure(failure);
            discovery ??= XsrResult.Failure<NexaUpdateStatus>(NexaUpdateContract.Unavailable);
            return new(query, discovery, installing, false);
        }
    }
}
