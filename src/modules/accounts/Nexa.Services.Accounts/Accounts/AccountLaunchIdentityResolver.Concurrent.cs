using Nexa.Services.Minecraft.Launch;
using Nexa.Xsr;

namespace Nexa.Services.Accounts;

public sealed partial class AccountLaunchIdentityResolver
{
    private readonly object _flightGate = new();
    private Task _authentication = Task.CompletedTask;
    private readonly Dictionary<(int Index, long Generation), IdentityFlight> _flights = [];
    private sealed class IdentityFlight : IDisposable
    {
        internal readonly CancellationTokenSource Stop = new();
        internal readonly TaskCompletionSource<XsrResult<MinecraftLaunchIdentity>> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<Task> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Waiters;
        private readonly object _gate = new();
        private bool _finished;
        private Task? _cancellation;
        internal Task CancelAsync() { lock (_gate) return _finished ? Task.CompletedTask : _cancellation ??= Stop.CancelAsync(); }
        public void Dispose()
        {
            Task cancellation;
            lock (_gate) { _finished = true; cancellation = _cancellation ?? Task.CompletedTask; }
            if (cancellation.IsCompleted) Stop.Dispose();
            else _ = cancellation.ContinueWith(_ => Stop.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    public async ValueTask<XsrResult<MinecraftLaunchIdentity>> ResolveAsync(int accountIndex, LaunchProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        if (profile.Kind == LaunchProfileKind.Offline)
            return await ResolveCoreAsync(accountIndex, profile, cancellationToken).ConfigureAwait(false);
        if (!_accounts.TryCaptureRefresh(accountIndex, profile, out long generation))
            return XsrResult.Failure<MinecraftLaunchIdentity>(AccountErrors.InvalidProfile("The account changed before launch authentication."));
        var key = (accountIndex, generation);
        IdentityFlight flight; bool start = false;
        lock (_flightGate)
        {
            if (!_flights.TryGetValue(key, out flight!)) { flight = new(); _flights.Add(key, flight); start = true; }
            flight.Waiters++;
        }
        using var registration = cancellationToken.Register(() =>
        {
            bool last;
            lock (_flightGate)
            {
                last = flight.Waiters == 1 && !flight.Completion.Task.IsCompleted;
                if (last && _flights.GetValueOrDefault(key) == flight) _flights.Remove(key);
            }
            if (last) _ = flight.CancelAsync().ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        });
        if (start) flight.Started.TrySetResult(RunIdentityFlightAsync(key, profile, flight));
        try { var result = await flight.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); cancellationToken.ThrowIfCancellationRequested(); return result; }
        finally
        {
            bool cancel, last;
            lock (_flightGate)
            {
                last = --flight.Waiters == 0;
                cancel = last && !flight.Completion.Task.IsCompleted;
                if (cancel && _flights.GetValueOrDefault(key) == flight) _flights.Remove(key);
            }
            if (cancel) await flight.CancelAsync().ConfigureAwait(false);
            // Keep the last request alive until its shared worker has physically retired.
            // Native owner shutdown must not unload code with an untracked refresh queued.
            if (last) await (await flight.Started.Task.ConfigureAwait(false)).ConfigureAwait(false);
        }
    }

    private async Task RunIdentityFlightAsync((int Index, long Generation) key, LaunchProfile profile, IdentityFlight flight)
    {
        try
        {
            // Credential publication uses a roster generation. Serialize only authentication
            // writes, while each instance continues its other preparation independently.
            Task preceding;
            TaskCompletionSource turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_flightGate) { preceding = _authentication; _authentication = turn.Task; }
            try
            {
                await preceding.ConfigureAwait(false);
                flight.Stop.Token.ThrowIfCancellationRequested();
                flight.Completion.TrySetResult(await ResolveCoreAsync(key.Index, profile, flight.Stop.Token).ConfigureAwait(false));
            }
            finally
            {
                turn.TrySetResult();
                lock (_flightGate) if (ReferenceEquals(_authentication, turn.Task)) _authentication = Task.CompletedTask;
            }
        }
        catch (OperationCanceledException) { flight.Completion.TrySetCanceled(); }
        catch (Exception error) { flight.Completion.TrySetException(error); _ = flight.Completion.Task.Exception; }
        finally
        {
            lock (_flightGate) if (_flights.GetValueOrDefault(key) == flight) _flights.Remove(key);
            flight.Dispose();
        }
    }
}
