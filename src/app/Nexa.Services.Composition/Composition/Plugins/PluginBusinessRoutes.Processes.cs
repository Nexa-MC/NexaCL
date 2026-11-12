using System.Collections.Immutable;
using System.Globalization;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private XsrCollectionSnapshot<MinecraftProcessSnapshot> Sessions()
    {
        var value = store.ReadCollection<MinecraftProcessSnapshot>(store.Resolve(MinecraftProcessStateComposition.SessionsKey));
        if (value.Availability != XsrStateAvailability.Available) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        return value;
    }
    private GameSession Session(HostInvocation c, MinecraftProcessSnapshot process) => new(new(Token(c, "session:" + process.SessionId)),
        new(Token(c, "instance:" + Path.GetDirectoryName(Path.GetDirectoryName(process.InstanceDirectory)) + ":" + process.InstanceId)),
        process.StartedAt, process.State is MinecraftProcessState.Created or MinecraftProcessState.Running);
    private MinecraftProcessSnapshot FindSession(HostInvocation c, EntityId id) => Sessions().Items.FirstOrDefault(p => Session(c, p).Id == id) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
    private IEnumerable<HostCapabilityRoute> ProcessRoutes => ProcessInteractionRoutes.Concat(ProcessReadRoutes);
    private async ValueTask<LogChunk> ReadProcessLog(HostInvocation c, LogRangeRequest v, CancellationToken token)
    {
        if (v.Limit is < 1 or > 256) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var session = FindSession(c, v.Session);
        if (minecraftQueries is null || !minecraftQueries.TryResolve(MinecraftProcessOutputContract.Query, out var query)) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        var result = await minecraftQueries.QueryAsync<MinecraftProcessOutputQuery, MinecraftProcessOutputSnapshot>(query, new(session.SessionId), cancellationToken: token).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        long after = 0; string prefix = Token(c, "log:" + session.SessionId) + ":";
        if (v.Cursor is { } cursor && (!cursor.StartsWith(prefix, StringComparison.Ordinal) || !long.TryParse(cursor.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out after))) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        var entries = result.Value.Entries.Where(e => e.Sequence > after).Take((int)v.Limit).ToArray();
        if (entries.Sum(e => (long)e.Text.Length) > 256 * 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
        long last = entries.Length == 0 ? after : entries[^1].Sequence;
        return new(v.Session, entries.Select(e => e.Text).ToImmutableArray(), prefix + last.ToString(CultureInfo.InvariantCulture), result.Value.DroppedEntries > after);
    }
    private IEnumerable<HostCapabilityRoute> ProcessReadRoutes => [
        HostRoutes.Typed<PageRequest, Page<GameSession>>("Minecraft.Processes", "ListSessions", (c, v) =>
        {
            var sessions = Sessions(); return ValueTask.FromResult(Page(c, v, sessions.Items.Select(p => Session(c, p)).ToImmutableArray(), "sessions", sessions.Revision));
        }),
        HostRoutes.Typed<EntityRequest, GameSession>("Minecraft.Processes", "ReadSession", (c, v) => ValueTask.FromResult(Session(c, FindSession(c, v.Id)))),
        HostRoutes.Typed<WatchRequest, SubscriptionRef>("Minecraft.Processes", "WatchSession", (c, v) => ValueTask.FromResult(Watch(c, v,
            "nexa.minecraft.processes/100", "nexa.minecraft.processes.read", Abi100Schemas.GameSession, () => GameSession.Write(Session(c, FindSession(c, v.Target)))))),
        HostRoutes.Typed<LogRangeRequest, LogChunk>("Minecraft.Processes", "ReadLogRange", (c, v) => ReadProcessLog(c, v, c.Options.Cancellation)),
        HostRoutes.Typed<EntityRequest, OperationRef>("Minecraft.Processes", "Stop", (c, v) =>
        {
            var session = FindSession(c, v.Id);
            if (minecraftCommands is null) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            var route = Resolve(minecraftCommands, MinecraftRouteIds.ProcessCancel);
            return ValueTask.FromResult(Services.StartHostOperation(c, "nexa.minecraft.processes.write", Abi100Schemas.Empty, async token =>
            {
                Require(await minecraftCommands.Dispatch(route, new MinecraftCancelProcessCommand(session.SessionId), cancellationToken: token).Completion.ConfigureAwait(false));
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            }));
        })
    ];
}
