using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Process;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed record LogWatch(HostInvocation Caller, LogRangeRequest Request, SubscriptionRef Reference);
    private readonly Dictionary<Guid, LogWatch> _logWatches = [];
    private Task? _logWatchWorker;
    private IEnumerable<HostCapabilityRoute> ProcessInteractionRoutes => [
        HostRoutes.Typed<EntityRequest, OperationRef>("Minecraft.Processes", "RequestClose", (c, v) =>
        {
            var session = FindSession(c, v.Id);
            return ValueTask.FromResult(Services.StartHostOperation(c, "nexa.minecraft.processes.write", Abi100Schemas.Empty, async token =>
            {
                await Command(minecraftCommands, MinecraftProcessInteractionContract.Interact, new MinecraftProcessInteractionCommand(session.SessionId, MinecraftProcessInteraction.RequestClose), token).ConfigureAwait(false);
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            }));
        }),
        HostRoutes.Typed<EntityRequest, Empty>("Minecraft.Processes", "RevealWindow", async (c, v) =>
        {
            var session = FindSession(c, v.Id);
            await Command(minecraftCommands, MinecraftProcessInteractionContract.Interact, new MinecraftProcessInteractionCommand(session.SessionId, MinecraftProcessInteraction.RevealWindow), c.Options.Cancellation).ConfigureAwait(false);
            return new Empty();
        }),
        HostRoutes.Typed<EntityRequest, SessionMetrics>("Minecraft.Processes", "ReadMetrics", async (c, v) =>
        {
            var session = FindSession(c, v.Id);
            var metrics = await Query<MinecraftProcessMetricsQuery, MinecraftProcessMetrics>(minecraftQueries, MinecraftProcessInteractionContract.Metrics, new(session.SessionId), c.Options.Cancellation).ConfigureAwait(false);
            return new(v.Id, metrics.WorkingSetBytes, metrics.CpuPercent, metrics.ObservedAt);
        }),
        HostRoutes.Typed<LogRangeRequest, SubscriptionRef>("Minecraft.Processes", "SubscribeLog", async (c, v) =>
        {
            var initial = await ReadProcessLog(c, v, c.Options.Cancellation).ConfigureAwait(false);
            lock (_watchGate)
            {
                if (_logWatches.Count >= 32) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                var sub = Services.CreateHostSubscription(c, "nexa.minecraft.processes/100", "nexa.minecraft.processes.read", LogChunk.Write(initial), 64);
                _logWatches.Add(sub.Token, new(c, v with { Cursor = initial.NextCursor }, sub)); return sub;
            }
        })
    ];
    private async Task WatchLogsAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_resourceStop.Token).ConfigureAwait(false))
            {
                LogWatch[] watches; lock (_watchGate) watches = _logWatches.Values.ToArray();
                foreach (var watch in watches)
                {
                    try
                    {
                        var chunk = await ReadProcessLog(watch.Caller, watch.Request, _resourceStop.Token).ConfigureAwait(false);
                        bool live = Services.PublishHostSubscription(watch.Caller.Identity, watch.Reference, LogChunk.Write(chunk));
                        lock (_watchGate)
                        {
                            if (!live) _logWatches.Remove(watch.Reference.Token);
                            else if (_logWatches.ContainsKey(watch.Reference.Token)) _logWatches[watch.Reference.Token] = watch with { Request = watch.Request with { Cursor = chunk.NextCursor } };
                        }
                    }
                    catch (AbiHostException)
                    {
                        Services.PublishHostSubscription(watch.Caller.Identity, watch.Reference, new AbiValue.Null());
                        lock (_watchGate) _logWatches.Remove(watch.Reference.Token);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_resourceStop.IsCancellationRequested) { }
    }
}
