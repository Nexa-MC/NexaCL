using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed class PlannedLaunch(PluginIdentity owner, LaunchPlan plan)
    {
        public PluginIdentity Owner { get; } = owner;
        public LaunchPlan Plan { get; } = plan;
        public OperationRef? Operation;
    }
    private readonly Dictionary<EntityId, PlannedLaunch> _launchPlans = [];
    private PlannedLaunch FindLaunch(HostInvocation c, EntityId id)
    {
        lock (_planGate)
            return _launchPlans.TryGetValue(id, out var plan) && plan.Owner == c.Identity ? plan : throw new AbiHostException(AbiErrors.HANDLE_INVALID);
    }
    private async ValueTask<OperationRecord> ReadOwnedOperation(HostInvocation c, EntityId id)
    {
        var route = Services.Routes.Single(r => r.Descriptor.Group == "Tasks" && r.Descriptor.Name == "Read");
        var result = await route.Handler(c, EntityRequest.Write(new(id))).ConfigureAwait(false);
        if (!result.Success) throw new AbiHostException(result.Error?.Code ?? AbiErrors.HANDLE_INVALID);
        return OperationRecord.Read(result.Value);
    }
    private async ValueTask<OperationRef> CancelOwnedOperation(HostInvocation c, EntityId id)
    {
        var route = Services.Routes.Single(r => r.Descriptor.Group == "Tasks" && r.Descriptor.Name == "Cancel");
        var result = await route.Handler(c, OperationRequest.Write(new(id))).ConfigureAwait(false);
        if (!result.Success) throw new AbiHostException(result.Error?.Code ?? AbiErrors.HANDLE_INVALID);
        return OperationRef.Read(result.Value);
    }
    private async ValueTask<LaunchPlan> PrepareLaunch(HostInvocation c, LaunchIntent request)
    {
        var grant = FindGrant(c, request.Grant, request.Instance);
        var instance = FindInstance(c, request.Instance);
        var profile = await ReadLaunchProfile(c, request.Instance, request.Profile, c.Options.Cancellation).ConfigureAwait(false);
        if (request.Target?.World is not null) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        string? server = null;
        string? serverRevision = null;
        if (request.Target?.Server is { } serverId)
        {
            var saved = await ReadServers(instance.Instance.DirectoryPath, c.Options.Cancellation).ConfigureAwait(false);
            var records = ProjectServers(c, instance.Instance.DirectoryPath, saved);
            int index = -1;
            for (int i = 0; i < records.Servers.Length; i++) if (records.Servers[i].Id == serverId) { index = i; break; }
            if (index < 0) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            server = saved.Entries[index].Address; serverRevision = saved.Revision;
        }
        var plan = Plan(c, "launch/start", LaunchIntent.Write(request), "Launch " + instance.Summary.Name + " with " + profile.Public.Name, async token =>
        {
            FindGrant(c, request.Grant, request.Instance);
            if (FindInstance(c, request.Instance).Summary.Revision != instance.Summary.Revision) throw new AbiHostException(AbiErrors.CONFLICT);
            var current = await ReadLaunchProfile(c, request.Instance, request.Profile, token).ConfigureAwait(false);
            if (current.Public.Revision != profile.Public.Revision) throw new AbiHostException(AbiErrors.CONFLICT);
            if (serverRevision is not null && (await ReadServers(instance.Instance.DirectoryPath, token).ConfigureAwait(false)).Revision != serverRevision) throw new AbiHostException(AbiErrors.CONFLICT);
        }, async token =>
        {
            lock (_planGate)
            {
                FindGrant(c, request.Grant, request.Instance);
                if (!_launchGrants.Remove(request.Grant.Token)) throw new AbiHostException(AbiErrors.CONFLICT);
            }
            var beforeSessions = Sessions().Items.Select(p => p.SessionId).ToHashSet();
            await Command(minecraftCommands, MinecraftRouteIds.Start, new MinecraftStartCommand(instance.Instance.Id, grant.AccountIndex)
            {
                MinecraftRootDirectory = instance.Snapshot.RootDirectory,
                ServerAddress = server,
                ExpectedAccountRosterRevision = grant.RosterRevision,
                ExpectedSettingsRevision = profile.Settings.Revision,
                LaunchProfileId = profile.ProfileId
            }, token).ConfigureAwait(false);
            var session = Sessions().Items.FirstOrDefault(p => !beforeSessions.Contains(p.SessionId) && p.InstanceDirectory == instance.Instance.DirectoryPath);
            if (session is null) throw new AbiHostException(AbiErrors.OUTCOME_UNKNOWN);
            return GameSession.Write(Session(c, session));
        });
        var result = new LaunchPlan(plan, request.Instance, profile.Public, [], []);
        lock (_planGate)
        {
            if (_launchPlans.Count >= 256) { _plans.Remove(plan.Id); throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE); }
            _launchPlans.Add(plan.Id, new(c.Identity, result));
        }
        return result;
    }
    private async ValueTask<LaunchAttempt> ReadLaunchAttempt(HostInvocation c, EntityId id)
    {
        var plan = FindLaunch(c, id); OperationRef? operation;
        lock (_planGate) operation = plan.Operation;
        if (operation is null) return new(id, plan.Plan.Instance, OperationState.Pending, null, [], plan.Plan.Plan.Revision);
        var task = await ReadOwnedOperation(c, operation.Id).ConfigureAwait(false);
        EntityId? session = task.State == OperationState.Completed && task.ResultSchema == Abi100Schemas.GameSession ? GameSession.Read(task.Result).Id : null;
        return new(id, plan.Plan.Instance, task.State, session,
            task.Failure is { } failure ? [Issue(failure.Code, "The launch operation failed; inspect its task result.", id.Value)] : [], task.Revision);
    }
    private IEnumerable<HostCapabilityRoute> LaunchRoutes => [
        HostRoutes.Typed<LaunchIntent, LaunchPlan>("Minecraft.Launch", "Prepare", PrepareLaunch),
        HostRoutes.Typed<EntityRequest, LaunchPlan>("Minecraft.Launch", "ReadPlan", (c, v) => ValueTask.FromResult(FindLaunch(c, v.Id).Plan)),
        HostRoutes.Typed<PlanRequest, ValidationReport>("Minecraft.Launch", "ValidatePlan", async (c, v) =>
        {
            PendingPlan plan;
            lock (_planGate)
            {
                FindLaunch(c, v.Plan);
                if (!_plans.TryGetValue(v.Plan, out plan!) || plan.Owner != c.Identity || plan.Kind != "launch/start") throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                if (plan.Public.Revision != v.ExpectedRevision || plan.Expires <= DateTimeOffset.UtcNow) throw new AbiHostException(AbiErrors.CONFLICT);
            }
            await plan.Validate(c.Options.Cancellation).ConfigureAwait(false); return new(true, []);
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Launch", "Start", (c, v) =>
        {
            var plan = FindLaunch(c, v.Plan);
            var operation = ExecutePlan(c, v, "launch/start", "nexa.minecraft.launch.write", Abi100Schemas.GameSession);
            lock (_planGate) plan.Operation = operation;
            return ValueTask.FromResult(operation);
        }),
        HostRoutes.Typed<OperationRequest, OperationRef>("Minecraft.Launch", "Cancel", async (c, v) =>
        {
            lock (_planGate)
                if (!_launchPlans.Values.Any(p => p.Owner == c.Identity && p.Operation?.Id == v.Operation)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            return await CancelOwnedOperation(c, v.Operation).ConfigureAwait(false);
        }),
        HostRoutes.Typed<EntityRequest, LaunchAttempt>("Minecraft.Launch", "ReadAttempt", (c, v) => ReadLaunchAttempt(c, v.Id)),
        HostRoutes.Typed<ServerJoinRequest, OperationRef>("Minecraft.Servers", "Join", async (c, v) =>
        {
            if (v.Intent.Target?.World is not null || v.Intent.Target?.Server is { } server && server != v.Server) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var plan = await PrepareLaunch(c, v.Intent with { Target = new(v.Server, null) }).ConfigureAwait(false);
            var operation = ExecutePlan(c, new(plan.Plan.Id, plan.Plan.Revision, null), "launch/start", "nexa.minecraft.servers.write", Abi100Schemas.GameSession);
            lock (_planGate) FindLaunch(c, plan.Plan.Id).Operation = operation;
            return operation;
        })
    ];
}
