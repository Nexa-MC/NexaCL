namespace Nexa.Services.Tasks;

/// <summary>Caller-local control adapter for a separately owned task partition.</summary>
public interface IExternalTaskCenterSource
{
    string Prefix { get; }
    Task<bool> RetryAsync(string taskId, CancellationToken token = default);
    Task<TaskCenterCancelResult> CancelAsync(string taskId, CancellationToken token = default);
    Task<bool> DismissAsync(string taskId, CancellationToken token = default);
    Task<int> ClearFinishedAsync(CancellationToken token = default);
    Task<bool> DismissFinishedAsync(IReadOnlyList<TaskCenterEntry> expected, CancellationToken token = default);
}
