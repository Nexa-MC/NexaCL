using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private readonly Dictionary<Guid, (HostInvocation Caller, SubscriptionRef Reference, Func<CancellationToken, ValueTask<AbiValue>> Read)> _asyncWatches = [];
    private Task? _asyncWatchWorker;
    private async ValueTask<SubscriptionRef> AsyncWatch(HostInvocation c, WatchRequest request, string contract, string permission, SchemaRef schema, Func<CancellationToken, ValueTask<AbiValue>> read)
    {
        if (request.Schema != schema) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var initial = await read(c.Options.Cancellation).ConfigureAwait(false);
        lock (_watchGate)
        {
            if (_asyncWatches.Count >= 32) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var reference = Services.CreateHostSubscription(c, contract, permission, initial, 64);
            _asyncWatches.Add(reference.Token, (c, reference, read));
            return reference with { ResyncRequired = request.Cursor is not null };
        }
    }
    private async Task WatchAsyncQueries()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(_resourceStop.Token).ConfigureAwait(false))
            {
                (HostInvocation Caller, SubscriptionRef Reference, Func<CancellationToken, ValueTask<AbiValue>> Read)[] watches;
                lock (_watchGate) watches = _asyncWatches.Values.ToArray();
                foreach (var watch in watches)
                {
                    try
                    {
                        using var budget = CancellationTokenSource.CreateLinkedTokenSource(_resourceStop.Token); budget.CancelAfter(TimeSpan.FromSeconds(5));
                        var value = await watch.Read(budget.Token).ConfigureAwait(false);
                        if (!Services.PublishHostSubscription(watch.Caller.Identity, watch.Reference, value))
                            lock (_watchGate) _asyncWatches.Remove(watch.Reference.Token);
                    }
                    catch (OperationCanceledException) when (!_resourceStop.IsCancellationRequested) { }
                    catch (Exception error) when (error is AbiHostException or IOException or UnauthorizedAccessException)
                    {
                        Services.PublishHostSubscription(watch.Caller.Identity, watch.Reference, new AbiValue.Null());
                        lock (_watchGate) _asyncWatches.Remove(watch.Reference.Token);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_resourceStop.IsCancellationRequested) { }
    }
    private IEnumerable<HostCapabilityRoute> AsyncWatchRoutes => [
        HostRoutes.Typed<WatchRequest, SubscriptionRef>("Minecraft.Launch", "WatchAttempt", (c, v) => AsyncWatch(c, v, "nexa.minecraft.launch/100", "nexa.minecraft.launch.read", Abi100Schemas.LaunchAttempt,
            async token => LaunchAttempt.Write(await ReadLaunchAttempt(c with { Options = c.Options with { Cancellation = token } }, v.Target).ConfigureAwait(false)))),
        HostRoutes.Typed<WatchRequest, SubscriptionRef>("Minecraft.Content", "Watch", (c, v) => AsyncWatch(c, v, "nexa.minecraft.content/100", "nexa.minecraft.content.read", Abi100Schemas.ContentItem,
            async token => ContentItem.Write((await FindContent(c, v.Target, token).ConfigureAwait(false)).Public)))
    ];
}
