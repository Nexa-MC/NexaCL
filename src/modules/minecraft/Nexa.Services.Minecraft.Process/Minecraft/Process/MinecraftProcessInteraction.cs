using System.Diagnostics;
using Nexa.Platform;
using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Process;

public sealed partial class MinecraftProcessService
{
    public ValueTask<XsrResult> InteractAsync(MinecraftProcessInteractionCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(command.Interaction)) return ValueTask.FromResult(XsrResult.Failure(MinecraftErrors.InvalidRequest("Invalid process interaction.")));
        if (!_sessions.TryGetValue(command.SessionId, out var session) || session.Snapshot.State is not (MinecraftProcessState.Created or MinecraftProcessState.Running))
            return ValueTask.FromResult(XsrResult.Failure(MinecraftErrors.ProcessNotFound(command.SessionId)));
        try
        {
            return ValueTask.FromResult(PlatformProcessWindow.Interact(session.Process, command.Interaction == MinecraftProcessInteraction.RevealWindow)
                ? XsrResult.Success() : XsrResult.Failure(new XsrError(XsrErrorKind.Unavailable, MinecraftProcessInteractionContract.Interact, "The game window did not accept this interaction.")));
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        { return ValueTask.FromResult(XsrResult.Failure(new XsrError(XsrErrorKind.Unavailable, MinecraftProcessInteractionContract.Interact, "Native game window interaction is unavailable."))); }
    }
    public async ValueTask<XsrResult<MinecraftProcessMetrics>> ReadMetricsAsync(MinecraftProcessMetricsQuery query, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(query.SessionId, out var session) || session.Snapshot.State != MinecraftProcessState.Running)
            return XsrResult.Failure<MinecraftProcessMetrics>(MinecraftErrors.ProcessNotFound(query.SessionId));
        try
        {
            var process = session.Process; process.Refresh(); if (process.HasExited) return XsrResult.Failure<MinecraftProcessMetrics>(MinecraftErrors.ProcessNotFound(query.SessionId));
            var cpu = process.TotalProcessorTime; long start = Stopwatch.GetTimestamp();
            await Task.Delay(100, token).ConfigureAwait(false); process.Refresh();
            if (process.HasExited || session.Snapshot.State != MinecraftProcessState.Running) return XsrResult.Failure<MinecraftProcessMetrics>(MinecraftErrors.ProcessNotFound(query.SessionId));
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            double percent = Math.Clamp((process.TotalProcessorTime - cpu).TotalMilliseconds / elapsed * 100 / Environment.ProcessorCount, 0, 100);
            return XsrResult.Success(new MinecraftProcessMetrics(process.WorkingSet64, percent, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return XsrResult.Failure<MinecraftProcessMetrics>(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        { return XsrResult.Failure<MinecraftProcessMetrics>(new(XsrErrorKind.Unavailable, MinecraftProcessInteractionContract.Metrics, "The game process cannot be sampled.")); }
    }
}
