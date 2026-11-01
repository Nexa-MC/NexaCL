using System.Diagnostics;
using Nexa.Services.Scheduling;

namespace Nexa.Services.Downloads;

public sealed partial class DownloadService
{
    private async ValueTask WriteMeasuredAsync(Stream target, ReadOnlyMemory<byte> buffer, CancellationToken token)
    {
        long started = Stopwatch.GetTimestamp();
        await target.WriteAsync(buffer, token).ConfigureAwait(false);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        if (WorkScheduler is IAsyncWorkResourceFeedback asynchronous)
            await asynchronous.ObserveDiskWriteAsync(buffer.Length, elapsed, token).ConfigureAwait(false);
        else if (WorkScheduler is IWorkResourceFeedback feedback)
            feedback.ObserveDiskWrite(buffer.Length, elapsed);
    }
}
