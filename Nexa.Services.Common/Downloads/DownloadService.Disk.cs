using System.Diagnostics;
using Nexa.Services.Scheduling;

namespace Nexa.Services.Downloads;

public sealed partial class DownloadService
{
    private async ValueTask WriteMeasuredAsync(Stream target, ReadOnlyMemory<byte> buffer, CancellationToken token)
    {
        long started = Stopwatch.GetTimestamp();
        await target.WriteAsync(buffer, token).ConfigureAwait(false);
        if (WorkScheduler is IWorkResourceFeedback feedback)
            feedback.ObserveDiskWrite(buffer.Length, Stopwatch.GetElapsedTime(started));
    }
}
