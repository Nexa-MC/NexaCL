using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Install;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed record InstallRecoveryIdentity(PluginIdentity Owner, MinecraftInstallRecoveryRecord Record);
    private readonly Dictionary<EntityId, InstallRecoveryIdentity> _installRecoveries = [];
    private async ValueTask<MinecraftInstallRecoveryRecord> FindInstallRecovery(HostInvocation c, EntityId id, CancellationToken token)
    {
        InstallRecoveryIdentity identity;
        lock (_planGate) if (!_installRecoveries.TryGetValue(id, out identity!) || identity.Owner != c.Identity) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        if (!Library().Directories.Any(d => Nexa.Core.PathIdentity.Comparer.Equals(d.Path, identity.Record.RootDirectory))) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        var current = await Query<MinecraftInstallRecoveryListQuery, MinecraftInstallRecoveryListing>(_installRecoveryQueries, MinecraftInstallRecoveryRecordsContract.List, new([identity.Record.RootDirectory]), token).ConfigureAwait(false);
        var record = current.Items.FirstOrDefault(r => r.Identity == identity.Record.Identity);
        if (record is null || record.Revision != identity.Record.Revision) throw new AbiHostException(AbiErrors.STALE_REVISION);
        return record;
    }
    private IEnumerable<HostCapabilityRoute> InstallRecoveryRoutes => [
        HostRoutes.Typed<PageRequest, Page<RecoveryRecord>>("Minecraft.Install", "ListRecoveries", async (c, v) =>
        {
            var listing = await Query<MinecraftInstallRecoveryListQuery, MinecraftInstallRecoveryListing>(_installRecoveryQueries, MinecraftInstallRecoveryRecordsContract.List,
                new(Library().Directories.Select(d => d.Path).ToArray()), c.Options.Cancellation).ConfigureAwait(false);
            var values = ImmutableArray.CreateBuilder<RecoveryRecord>();
            lock (_planGate)
                foreach (var record in listing.Items.OrderBy(r => r.RootDirectory, StringComparer.Ordinal).ThenBy(r => r.Identity, StringComparer.Ordinal))
                {
                    var id = new EntityId(Token(c, "install-recovery:" + record.RootDirectory + ":" + record.Identity + ":" + record.Revision));
                    if (!_installRecoveries.ContainsKey(id) && _installRecoveries.Count >= 512) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                    _installRecoveries[id] = new(c.Identity, record);
                    values.Add(new(id, new(Token(c, "native-install-task:" + record.Identity)), new("Recover installation " + record.Instance, []), new(record.Revision)));
                }
            return ContentPage(values.ToImmutable(), v);
        }),
        HostRoutes.Typed<EntityRequest, OperationRef>("Minecraft.Install", "ResumeRecovery", async (c, v) =>
        {
            var record = await FindInstallRecovery(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            return Services.StartHostOperation(c, "nexa.minecraft.install.write", Abi100Schemas.Empty, async token =>
            {
                await Confirm(c, "install/resume", "Resume installation " + record.Instance, EntityRequest.Write(v), token).ConfigureAwait(false);
                await FindInstallRecovery(c, v.Id, token).ConfigureAwait(false);
                await Command(_installCommands, MinecraftInstallRecoveryRecordsContract.Apply, new MinecraftInstallRecoveryRecordCommand(record.RootDirectory, record.Identity, record.Revision, false), token).ConfigureAwait(false);
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            });
        }),
        HostRoutes.Typed<EntityRequest, Empty>("Minecraft.Install", "DiscardRecovery", async (c, v) =>
        {
            var record = await FindInstallRecovery(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            await Confirm(c, "install/discard-recovery", "Dismiss recovery for " + record.Instance + "; retain existing and staged files", EntityRequest.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            await FindInstallRecovery(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            await Command(_installCommands, MinecraftInstallRecoveryRecordsContract.Apply, new MinecraftInstallRecoveryRecordCommand(record.RootDirectory, record.Identity, record.Revision, true), c.Options.Cancellation).ConfigureAwait(false);
            lock (_planGate) _installRecoveries.Remove(v.Id);
            return new Empty();
        })
    ];
}
