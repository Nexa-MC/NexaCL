namespace Nexa.Services.Scheduling;

/// <summary>Awaitable quiet admission for the scheduler owned by another runtime.</summary>
public interface IAsyncWorkQuietScheduler
{
    ValueTask<IWorkQuietLease> EnterQuietAsync(CancellationToken token = default);
}
