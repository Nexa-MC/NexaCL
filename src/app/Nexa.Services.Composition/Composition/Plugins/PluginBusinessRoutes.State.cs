using System.Threading.Channels;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Accounts;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private readonly object _watchGate = new();
    private readonly Dictionary<Guid, (PluginIdentity Owner, SubscriptionRef Reference, Func<AbiValue> Read)> _watches = [];
    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private PluginRuntimeServices? _services;
    private Task? _watchWorker;
    public void Bind(PluginRuntimeServices services)
    {
        if (_services is not null) throw new InvalidOperationException("Plugin business routes already bound.");
        _services = services; store.Changed += Changed; _watchWorker = PumpAsync();
    }
    private PluginRuntimeServices Services => _services ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
    private void Changed(XsrStateChange _) => _changes.Writer.TryWrite(true);
    private SubscriptionRef Watch(HostInvocation caller, WatchRequest request, string contract, string permission, SchemaRef schema, Func<AbiValue> read)
    {
        if (request.Schema != schema) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        lock (_watchGate)
        {
            if (_watches.Count >= 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var reference = Services.CreateHostSubscription(caller, contract, permission, read(), 64);
            _watches.Add(reference.Token, (caller.Identity, reference, read));
            return reference with { ResyncRequired = request.Cursor is not null };
        }
    }
    private async Task PumpAsync()
    {
        await foreach (var _ in _changes.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_watchGate)
                foreach (var watch in _watches.Values.ToArray())
                {
                    try
                    {
                        if (!Services.PublishHostSubscription(watch.Owner, watch.Reference, watch.Read())) _watches.Remove(watch.Reference.Token);
                    }
                    catch (AbiHostException)
                    {
                        // The source or its captured revision disappeared. Retire this watch instead
                        // of substituting a different account, instance or process into its identity.
                        Services.PublishHostSubscription(watch.Owner, watch.Reference, new AbiValue.Null());
                        _watches.Remove(watch.Reference.Token);
                    }
                }
        }
    }
    private IEnumerable<HostCapabilityRoute> StateRoutes => [
        HostRoutes.Typed<WatchRequest, SubscriptionRef>("Minecraft.Instances", "Watch", (c, v) => ValueTask.FromResult(Watch(c, v,
            "nexa.minecraft.instances/100", "nexa.minecraft.instances.read", Abi100Schemas.InstanceSummary, () => InstanceSummary.Write(FindInstance(c, v.Target).Summary)))),
        HostRoutes.Typed<WatchRequest, SubscriptionRef>("Accounts", "Watch", (c, v) => ValueTask.FromResult(Watch(c, v,
            "nexa.accounts/100", "nexa.accounts.read", Abi100Schemas.AccountPublic, () => AccountPublic.Write(FindAccount(c, v.Target).Public)))),
        HostRoutes.Typed<RootRequest, OperationRef>("Minecraft.Instances", "Discover", (c, v) =>
        {
            if (v.Root != Root(c, Library().RootDirectory)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            return ValueTask.FromResult(Services.StartHostOperation(c, "nexa.minecraft.instances.write", Abi100Schemas.Empty, async token =>
            {
                Require(await libraryCommands.Dispatch(Resolve(libraryCommands, MinecraftLibraryRoutes.Refresh), new MinecraftLibraryRefreshCommand(), cancellationToken: token).Completion.ConfigureAwait(false));
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            }));
        }),
        HostRoutes.Typed<EntityRequest, Empty>("Accounts", "Remove", async (c, v) =>
        {
            var account = FindAccount(c, v.Id);
            Require(await accountCommands.Dispatch(Resolve(accountCommands, FoundationRouteIds.AccountRemoveProfile),
                new AccountRemoveProfileCommand(account.Profile.Index, account.Revision), cancellationToken: c.Options.Cancellation).Completion.ConfigureAwait(false));
            return new Empty();
        })
    ];
    public async ValueTask DisposeAsync()
    {
        store.Changed -= Changed; _changes.Writer.TryComplete();
        if (_watchWorker is not null) await _watchWorker.ConfigureAwait(false);
        lock (_watchGate) _watches.Clear();
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(_identity);
    }
}
