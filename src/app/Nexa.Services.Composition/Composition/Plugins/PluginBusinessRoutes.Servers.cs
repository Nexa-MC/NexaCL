using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private string SelectedInstanceDirectory() => Library().SelectedInstance?.DirectoryPath ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
    private ValueTask<InstanceServerList> ReadServers(string path, CancellationToken token) => Query<InstanceServerListQuery, InstanceServerList>(foundationQueries, InstanceServerListContract.Read, new(path), token);
    private static (string Host, int Port) ServerAddress(string address)
    {
        if (!Uri.TryCreate("minecraft://" + address, UriKind.Absolute, out var parsed) || parsed.UserInfo.Length != 0 || parsed.AbsolutePath is not ("" or "/") || parsed.Query.Length != 0 || parsed.Fragment.Length != 0)
            throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        return (parsed.Host.Trim('[', ']'), parsed.Port < 0 ? 25565 : parsed.Port);
    }
    private ServerList ProjectServers(HostInvocation c, string path, InstanceServerList value) => new(value.Entries.Select(entry =>
    {
        var address = ServerAddress(entry.Address);
        return new ServerRecord(new(Token(c, "server:" + path + ":" + value.Revision + ":" + entry.SourceIndex)), entry.Name, address.Host, address.Port);
    }).ToImmutableArray(), new(Token(c, "servers:" + path + ":" + value.Revision)));
    private IEnumerable<HostCapabilityRoute> ServerRoutes => [
        HostRoutes.Typed<Empty, ServerList>("Minecraft.Servers", "ListSaved", async (c, _) =>
        { string path = SelectedInstanceDirectory(); return ProjectServers(c, path, await ReadServers(path, c.Options.Cancellation).ConfigureAwait(false)); }),
        HostRoutes.Typed<ServerList, ServerList>("Minecraft.Servers", "SaveList", async (c, v) =>
        {
            string path = SelectedInstanceDirectory(); var before = await ReadServers(path, c.Options.Cancellation).ConfigureAwait(false);
            var projected = ProjectServers(c, path, before);
            if (projected.Revision != v.Revision || v.Servers.Length > 1024) throw new AbiHostException(AbiErrors.CONFLICT);
            var entries = v.Servers.Select(server =>
            {
                if (server.Port is < 1 or > 65535 || server.Name.Length > 256 || server.Address.Length > 253 || server.Name.Any(char.IsControl)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                string host = server.Address.Contains(':') ? "[" + server.Address + "]" : server.Address;
                string address = host + ":" + server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture); ServerAddress(address);
                int index = -1; for (int i = 0; i < projected.Servers.Length; i++) if (projected.Servers[i].Id == server.Id) { index = i; break; }
                return index < 0 ? new InstanceServerEntry(-1, server.Name, address) : before.Entries[index] with { Name = server.Name, Address = address };
            }).ToArray();
            await Confirm(c, "servers/save", "Replace saved server list", ServerList.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            await Command(foundationCommands, InstanceServerListContract.Save, new InstanceServerListSaveCommand(path, before.Revision, entries), c.Options.Cancellation).ConfigureAwait(false);
            return ProjectServers(c, path, await ReadServers(path, c.Options.Cancellation).ConfigureAwait(false));
        }),
        HostRoutes.Typed<EntityRequest, ServerStatus>("Minecraft.Servers", "ReadStatus", async (c, v) =>
        {
            string path = SelectedInstanceDirectory(); var saved = await ReadServers(path, c.Options.Cancellation).ConfigureAwait(false); var projected = ProjectServers(c, path, saved);
            int index = -1; for (int i = 0; i < projected.Servers.Length; i++) if (projected.Servers[i].Id == v.Id) { index = i; break; }
            if (index < 0) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            var status = await Query<InstanceServerStatusQuery, InstanceServerStatus>(foundationQueries, InstanceServerListContract.Status, new(path, saved.Revision, saved.Entries[index].SourceIndex), c.Options.Cancellation).ConfigureAwait(false);
            return new(v.Id, status.Reachable, status.Description, status.OnlinePlayers ?? 0, status.MaxPlayers ?? 0, status.Version);
        }),
        HostRoutes.Typed<Empty, SubscriptionRef>("Minecraft.Servers", "DiscoverLan", async (c, _) =>
        {
            var result = await Query<LanGameDiscoveryQuery, LanGameDiscoverySnapshot>(foundationQueries, LanGameDiscoveryContract.Discover, new(3000), c.Options.Cancellation).ConfigureAwait(false);
            lock (_watchGate)
            {
                if (_lanWatches.Count >= 16) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                var subscription = Services.CreateHostSubscription(c, "nexa.minecraft.servers/100", "nexa.minecraft.servers.read", ServerList.Write(ProjectLan(c, result)), 64);
                _lanWatches.Add(subscription.Token, (c, subscription)); return subscription;
            }
        })
    ];
    private readonly Dictionary<Guid, (HostInvocation Caller, SubscriptionRef Subscription)> _lanWatches = [];
    private Task? _lanWatchWorker;
    private ServerList ProjectLan(HostInvocation c, LanGameDiscoverySnapshot result)
    {
        var servers = result.Games.OrderBy(g => g.Address, StringComparer.Ordinal).Select(game =>
        { var a = ServerAddress(game.Address); return new ServerRecord(new(Token(c, "lan:" + game.Address)), game.Name, a.Host, a.Port); }).ToImmutableArray();
        return new(servers, new(Token(c, "lan:" + string.Join("|", result.Games.OrderBy(g => g.Address, StringComparer.Ordinal).Select(g => g.Address + ":" + g.Name)))));
    }
    private async Task WatchLanAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(_resourceStop.Token).ConfigureAwait(false))
            {
                (HostInvocation Caller, SubscriptionRef Subscription)[] watches;
                lock (_watchGate) watches = _lanWatches.Values.ToArray();
                if (watches.Length == 0) continue;
                try
                {
                    var result = await Query<LanGameDiscoveryQuery, LanGameDiscoverySnapshot>(foundationQueries, LanGameDiscoveryContract.Discover, new(3000), _resourceStop.Token).ConfigureAwait(false);
                    foreach (var watch in watches)
                        if (!Services.PublishHostSubscription(watch.Caller.Identity, watch.Subscription, ServerList.Write(ProjectLan(watch.Caller, result))))
                            lock (_watchGate) _lanWatches.Remove(watch.Subscription.Token);
                }
                catch (Exception e) when (e is AbiHostException or IOException or System.Net.Sockets.SocketException)
                {
                    foreach (var watch in watches)
                        if (!Services.PublishHostSubscription(watch.Caller.Identity, watch.Subscription, new AbiValue.Null()))
                            lock (_watchGate) _lanWatches.Remove(watch.Subscription.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_resourceStop.IsCancellationRequested) { }
    }

}
