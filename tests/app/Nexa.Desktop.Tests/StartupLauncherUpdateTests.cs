using Nexa.Services.Updates;
using Nexa.Xsr;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void StartupUpdateWaitsForCommitBeforeRestart()
    {
        var query = new NexaUpdateQuery("2.0.0.alpha.29", "win-x64", "alpha");
        var service = new StartupInstallFixture(new(null, null, "idle", true, false));
        var stages = new List<string>();
        var offer = new NexaUpdateOffer("2.0.0.alpha.30", "", "", "");
        var run = DesktopStartupUpdate.RunAsync(service, query, true, (requested, _) =>
        {
            AssertEqual(query, requested);
            return Task.FromResult(XsrResult.Success(new NexaUpdateStatus(offer)));
        }, stages.Add, error => throw error, default);
        AssertFalse(run.IsCompleted);
        AssertEqual("2.0.0.alpha.30/alpha", service.Target);
        AssertTrue(stages.SequenceEqual(["startup_update_discovery", "startup_install_update"]));
        service.Completion.SetResult(new(offer.Version, "alpha", "complete", true, true));
        var result = run.GetAwaiter().GetResult();
        AssertTrue(result.RestartRequired && result.InstallAttempted);
        AssertEqual(offer, result.Discovery!.Value!.Offer);
    }

    private static void StartupUpdateResumesAndRejectsUncommittedResults()
    {
        var query = new NexaUpdateQuery("2.0.0.alpha.29", "win-x64", "alpha");
        var service = new StartupInstallFixture(new("2.0.0.beta.2", "beta", "paused", true, false));
        int failures = 0;
        var run = DesktopStartupUpdate.RunAsync(service, query, false,
            (_, _) => throw new InvalidOperationException("Recovery must use the transaction channel."),
            _ => { }, _ => failures++, default);
        AssertEqual("2.0.0.beta.2/beta", service.Target);
        service.Completion.SetResult(new("2.0.0.beta.1", "beta", "complete", true, false));
        AssertFalse(run.GetAwaiter().GetResult().RestartRequired);
        AssertEqual(1, failures);
    }

    private static void StartupUpdateNoOfferOrFailedRedirectNeverRestarts()
    {
        var query = new NexaUpdateQuery("2.0.0.alpha.29", "win-x64", "alpha");
        foreach (string? target in new string?[] { null, query.CurrentVersion, "2.0.0.alpha.30" })
        {
            var service = new StartupInstallFixture(new("2.0.0.alpha.30", "alpha", "complete", true, true));
            var run = DesktopStartupUpdate.RunAsync(service, query, true,
                (_, _) => Task.FromResult(XsrResult.Success(new NexaUpdateStatus(target is null ? null : new(target, "", "", "")))),
                _ => { }, error => throw error, default);
            AssertFalse(run.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult().RestartRequired);
            AssertTrue(service.Target is null);
        }
    }

    private static void StartupUpdateCancellationInterruptsPendingInstall()
    {
        var service = new StartupInstallFixture(new("2.0.0.beta.2", "beta", "downloading", true, false));
        using var stop = new CancellationTokenSource();
        var run = DesktopStartupUpdate.RunAsync(service, new("2.0.0.alpha.29", "win-x64", "alpha"), true,
            (_, _) => throw new InvalidOperationException(), _ => { }, error => throw error, stop.Token);
        AssertFalse(run.IsCompleted);
        stop.Cancel();
        try { run.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
        throw new InvalidOperationException("Startup cancellation must prevent relaunch.");
    }

    private sealed class StartupInstallFixture(AutomaticUpdateStatus status) : IAutomaticUpdateControl
    {
        internal string? Target;
        internal readonly TaskCompletionSource<AutomaticUpdateStatus> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AutomaticUpdateStatus> ReadAsync(CancellationToken token) => Task.FromResult(status);
        public Task<AutomaticUpdateStatus> InstallAsync(string version, string channel, CancellationToken token)
        {
            Target = version + "/" + channel;
            return Completion.Task.WaitAsync(token);
        }
        public Task<AutomaticUpdateStatus> RollbackAsync(CancellationToken token) => throw new NotSupportedException();
        public void Restart() => throw new InvalidOperationException("The old process still owns its single-instance lock.");
    }
}
