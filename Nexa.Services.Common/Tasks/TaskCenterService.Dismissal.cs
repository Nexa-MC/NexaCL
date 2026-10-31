namespace Nexa.Services.Tasks;

public sealed partial class TaskCenterService
{
    /// <summary>Registers durable cleanup for this exact attempt, before publishing its failure.</summary>
    public void SetDismissAction(ITaskCenterTask task, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            if (_registrations.TryGetValue(task.TaskId, out var entry)
                && entry.Cancellation.Token == task.CancellationToken && !entry.Dismissing)
            {
                var previous = entry.DismissAction;
                entry.DismissAction = previous is null ? action : async () =>
                { await previous().ConfigureAwait(false); await action().ConfigureAwait(false); };
            }
        }
    }

    public Task<bool> DismissAsync(string taskId)
    {
        Registration? registration;
        lock (_gate) _registrations.TryGetValue(taskId, out registration);
        return registration is not null ? DismissRegistrationAsync(registration) : Task.FromResult(false);
    }

    public async Task<int> ClearFinishedAsync()
    {
        Registration[] entries;
        lock (_gate) entries = _registrations.Values.Where(item => item.Entry.IsTerminal).ToArray();
        int count = 0;
        foreach (var entry in entries)
            if (await DismissRegistrationAsync(entry).ConfigureAwait(false)) count++;
        return count;
    }

    private async Task<bool> DismissRegistrationAsync(Registration registration)
    {
        lock (_gate)
        {
            if (!registration.Entry.IsTerminal || registration.Dismissing
                || !_registrations.TryGetValue(registration.Entry.TaskId, out var current)
                || !ReferenceEquals(current, registration)) return false;
            registration.Dismissing = true;
        }
        try
        {
            if (registration.DismissAction is { } action)
            {
                await registration.OwnerReleased.Task.ConfigureAwait(false);
                await Task.Run(action).ConfigureAwait(false);
            }
            lock (_gate)
            {
                if (!_registrations.TryGetValue(registration.Entry.TaskId, out var current)
                    || !ReferenceEquals(current, registration)) return false;
                _registrations.Remove(registration.Entry.TaskId);
                Enqueue(registration, remove: true);
            }
            DrainPublications();
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            lock (_gate)
            {
                if (_registrations.TryGetValue(registration.Entry.TaskId, out var current) && ReferenceEquals(current, registration))
                {
                    registration.Entry = registration.Entry with { ErrorMessage = "清理恢复记录失败：" + error.Message };
                    Enqueue(registration);
                }
            }
            DrainPublications();
            throw;
        }
        finally { lock (_gate) registration.Dismissing = false; }
    }
}
