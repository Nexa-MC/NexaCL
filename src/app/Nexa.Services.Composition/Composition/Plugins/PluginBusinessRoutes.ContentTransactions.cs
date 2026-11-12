using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private IEnumerable<HostCapabilityRoute> ContentTransactionRoutes => [
        HostRoutes.Typed<EntityRequest, Page<ContentTransaction>>("Minecraft.Content", "ListTransactions", async (c, v) =>
        {
            var snapshot = await Management(c, v.Id, c.Options.Cancellation, trash: true).ConfigureAwait(false);
            if (snapshot.ContentUpdates.Count > 128) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var entries = ImmutableArray.CreateBuilder<ContentTransaction>();
            foreach (var transaction in snapshot.ContentUpdates)
            {
                var id = new EntityId(Token(c, "content-transaction:" + v.Id.Value + ":" + transaction.Id));
                MutationPlan? plan;
                lock (_planGate) plan = _plans.Values.FirstOrDefault(p => p.Owner == c.Identity && p.Kind == "content/rollback"
                    && p.Expires > DateTimeOffset.UtcNow && p.Public.InputsHash == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(AbiValueBinary.Encode(EntityRequest.Write(new(id))))))?.Public;
                plan ??= Plan(c, "content/rollback", EntityRequest.Write(new(id)), "Roll back " + transaction.Items + " content updates from " + transaction.CreatedAt.ToString("u"), async token =>
                {
                    var current = await Management(c, v.Id, token, trash: true).ConfigureAwait(false);
                    if (!current.ContentUpdates.Contains(transaction) || transaction.Phase == "rolled-back") throw new AbiHostException(AbiErrors.STALE_REVISION);
                }, async token =>
                {
                    await Command(foundationCommands, InstanceManagementContract.RollbackContentUpdate, new InstanceContentUpdateRollbackCommand(snapshot.InstanceDirectory, transaction.Id), token).ConfigureAwait(false);
                    return Empty.Write(new());
                });
                lock (_planGate) _plans[plan.Id] = _plans[plan.Id] with { PreviewBody = ContentPlan.Write(new(plan, [], [])) };
                entries.Add(new(id, v.Id, transaction.Phase switch { "committed" => OperationState.Completed, "rolled-back" => OperationState.Cancelled, _ => OperationState.OutcomeUnknown }, plan.Id, plan.Revision));
            }
            var values = entries.ToImmutable();
            return new(values, null, ProjectionRevision(new Page<ContentTransaction>(values, null, new("content-transactions"))));
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Content", "Rollback", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "content/rollback", "nexa.minecraft.content.write", Abi100Schemas.Empty)))
    ];
}
