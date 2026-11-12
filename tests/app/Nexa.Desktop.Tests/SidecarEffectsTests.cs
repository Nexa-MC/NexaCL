using System.Text;
using Nexa.Services.Accounts;
using Nexa.Services.Capabilities;
using Nexa.Services.Composition.Sidecars;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Management;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Effects = Nexa.Sidecar.Host.Effects.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarEffectsAbi() => SidecarEffectsAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarEffectsAbiAsync()
    {
        var observer = new NoopDispatchObserver();
        var accounts = new EffectsAccountFixture();
        bool allowed = true, corrupt = false;
        var caller = new SidecarHostCaller("example.generic", Guid.NewGuid());
        var api = new SidecarEffectsApi((source, _) => allowed && source == caller);
        var baseline = new SidecarBusinessApi((source, _) => allowed && source == caller);
        baseline.CompleteComposition();
        var facts = new MachineCapabilitySnapshot(1, DateTimeOffset.UtcNow, [
            MachineEnvironmentCatalog.PowerSource.Observe("电池", DateTimeOffset.UtcNow, "test"),
            MachineEnvironmentCatalog.PowerBatteryLevelPercent.Observe(70, DateTimeOffset.UtcNow, "test"),
            MachineEnvironmentCatalog.PowerProfileCurrent.Observe("power-saver", DateTimeOffset.UtcNow, "test")]);
        MachineCapabilityQuery? capturedScope = null;
        XsrQueryRouterBuilder foundation = new();
        foundation.Register<MachineCapabilityQuery, MachineCapabilitySnapshot>(MachineCapabilityStateContract.SnapshotQuery,
            (request, _) => { capturedScope = request; return ValueTask.FromResult(XsrResult.Success(facts)); });
        foundation.Register<LaunchPreflightQuery, CapabilityPreflightReport>(MachineCapabilityStateContract.PreflightQuery,
            (request, _) => { AssertTrue(ReferenceEquals(facts, request.Snapshot)); return ValueTask.FromResult(XsrResult.Success(new CapabilityPreflightReport([], [], PreflightSeverity.None))); });
        foundation.Register<InstanceWorldSnapshotListQuery, IReadOnlyList<InstanceWorldSnapshot>>(InstanceWorldSnapshotContract.List,
            (request, _) => { AssertEqual("world-one", request.WorldName); return ValueTask.FromResult(XsrResult.Success<IReadOnlyList<InstanceWorldSnapshot>>([new("snapshot-one", DateTimeOffset.UnixEpoch, 2, 512)])); });
        Guid stopped = default;
        XsrCommandRouterBuilder processes = new();
        processes.Register<MinecraftCancelProcessCommand>(MinecraftRouteIds.ProcessCancel,
            (request, _) => { stopped = request.SessionId; return ValueTask.FromResult(XsrResult.Success()); });
        api.Bind("minecraft", processes.Build(observer), null);
        string executable = Path.GetFullPath("java-test/bin/java");
        var candidate = new JavaRuntimeCandidate(new(Path.GetDirectoryName(executable)!, executable, null, new(21, 0, 1), JavaBrand.Unknown, JavaArchitecture.X64, true, false));
        int inspections = 0;
        var effects = new SidecarHostEffects(accounts, accounts,
            (path, _) => { AssertEqual(executable, path); inspections++; return ValueTask.FromResult<JavaRuntimeCandidate?>(candidate); },
            (instance, local, _) => { AssertTrue(local); AssertEqual(Path.GetFullPath("games/versions/one"), instance); return ValueTask.FromResult<JavaRuntimeCandidate?>(candidate); },
            (_, _) => ValueTask.FromResult(JavaRequirementResolution.Valid(new(new(21, 0), new(21, 999)), "java-runtime-delta")),
            foundation.Build(observer), observer);
        api.Bind("effects", effects.Commands, effects.Queries);
        var routes = baseline.Routes.Concat(api.Routes).ToDictionary(route => route.ContractId);
        SidecarHostRequestClient? wire = null;
        using var requests = wire = new(async (frame, token) =>
        {
            var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
            var route = routes[request.ContractId];
            var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, token)
                : XsrResult.Failure<SidecarBinaryValue>(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.permission_denied"), "Denied"));
            if (result.IsSuccess)
            {
                AssertFalse(Encoding.UTF8.GetString(result.Value.ToArray()).Contains(EffectsAccountFixture.Secret, StringComparison.Ordinal));
                if (corrupt && route.ContractId == 511)
                { var bytes = result.Value.ToArray(); bytes[8] = (byte)'?'; result = XsrResult.Success(new SidecarBinaryValue(SidecarWireCodecs.GeneratedDto, bytes)); }
            }
            AssertTrue(wire!.TryAcceptReply(new(SidecarProtocol.Version, route.IsQuery ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
                SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                    result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value))));
        });
        requests.Activate(SidecarFeatures.BinaryPayloads | SidecarFeatures.HostRequests | SidecarFeatures.LargeGeneratedDtos);
        var client = new SidecarEffectsClient(requests, observer);
        var baseClient = new SidecarBusinessClient(requests, observer);
        AssertFalse((await client.DiscoverAsync()).IsSuccess);
        api.CompleteComposition();
        AssertTrue((await client.DiscoverAsync()).IsSuccess);
        AssertTrue((await baseClient.DiscoverAsync()).IsSuccess);
        async ValueTask<XsrResult<TOut>> Query<TIn, TOut>(XsrSemanticId id, TIn input) where TIn : notnull
        { AssertTrue(client.Queries.TryResolve(id, out var route)); return await client.Queries.QueryAsync<TIn, TOut>(route, input); }
        async ValueTask<XsrResult> Refresh(long revision)
        { AssertTrue(client.Commands.TryResolve(Effects.SidecarEffectsRoutes.AccountRefresh, out var route)); return await client.Commands.Dispatch(route, new Effects.HostAccountRequest(0, revision)).Completion; }
        var eligibility = await Query<Effects.HostAccountRequest, Effects.HostAccountEligibility>(Effects.SidecarEffectsRoutes.AccountEligibility, new(0, 4));
        AssertTrue(eligibility.IsSuccess && eligibility.Value!.Allowed && eligibility.Value.CheckSession);
        AssertTrue((await Refresh(4)).IsSuccess); AssertEqual(1, accounts.Refreshes);
        AssertFalse((await Refresh(3)).IsSuccess); AssertEqual(1, accounts.Refreshes);
        accounts.Profile = accounts.Profile with { Kind = LaunchProfileKind.Offline, Username = "offline" };
        eligibility = await Query<Effects.HostAccountRequest, Effects.HostAccountEligibility>(Effects.SidecarEffectsRoutes.AccountEligibility, new(0, 4));
        AssertTrue(eligibility.IsSuccess && eligibility.Value!.Allowed && !eligibility.Value.CheckSession);
        accounts.Profile = accounts.Profile with { Username = "" };
        AssertFalse((await Query<Effects.HostAccountRequest, Effects.HostAccountEligibility>(Effects.SidecarEffectsRoutes.AccountEligibility, new(0, 4))).Value!.Allowed);
        var java = await Query<Effects.HostJavaInspectRequest, Effects.HostJavaCandidate>(Effects.SidecarEffectsRoutes.JavaInspect, new(executable));
        AssertTrue(java.IsSuccess); AssertEqual(candidate.Installation.Version, java.Value!.Candidate!.Installation.Version);
        AssertFalse((await Query<Effects.HostJavaInspectRequest, Effects.HostJavaCandidate>(Effects.SidecarEffectsRoutes.JavaInspect, new("relative/java"))).IsSuccess);
        AssertEqual(1, inspections);
        AssertFalse((await Query<Effects.HostJavaSelectRequest, Effects.HostJavaCandidate>(Effects.SidecarEffectsRoutes.JavaSelect, new(null, true))).IsSuccess);
        AssertTrue((await Query<Effects.HostJavaSelectRequest, Effects.HostJavaCandidate>(Effects.SidecarEffectsRoutes.JavaSelect, new(Path.GetFullPath("games/versions/one"), true))).IsSuccess);
        var requirement = await Query<Effects.HostJavaRequirementsRequest, Effects.JavaRequirementResolution>(Effects.SidecarEffectsRoutes.JavaRequirements, new(Path.GetFullPath("games/versions/one")));
        AssertTrue(requirement.IsSuccess); AssertEqual(new Version(21, 0), requirement.Value!.Range.Minimum);
        var power = await Query<Effects.HostEmptyRequest, Effects.HostPowerState>(Effects.SidecarEffectsRoutes.Power, new());
        AssertTrue(power.IsSuccess && power.Value!.OnBattery && power.Value.Saving); AssertEqual(70, power.Value!.BatteryPercent!.Value);
        var preflight = await Query<Effects.HostPreflightRequest, Effects.HostPreflightReport>(Effects.SidecarEffectsRoutes.Preflight,
            new(Path.GetFullPath("games/versions/one"), "one", Path.GetFullPath("games")));
        AssertTrue(preflight.IsSuccess); AssertEqual("one", capturedScope!.InstanceId); AssertTrue(capturedScope.RefreshInstance);
        var settings = await Query<Effects.HostEmptyRequest, Effects.HostSettingsDefinitions>(Effects.SidecarEffectsRoutes.SettingsDefinitions, new());
        AssertTrue(settings.IsSuccess); AssertTrue(settings.Value!.Definitions.Count > 0);
        AssertFalse(settings.Value.Definitions.Any(d => !d.Exportable || d.Key is "network.proxy-user" or "network.proxy-password"));
        var invalid = await Query<Effects.HostSettingsValidateRequest, Effects.HostSettingsValidation>(Effects.SidecarEffectsRoutes.SettingsValidate,
            new("game.memory", new(Effects.SettingsOverrideMode.Custom, "-1")));
        AssertTrue(invalid.IsSuccess && !invalid.Value!.Valid);
        AssertFalse((await Query<Effects.HostSettingsValidateRequest, Effects.HostSettingsValidation>(Effects.SidecarEffectsRoutes.SettingsValidate,
            new("network.proxy-password", new(Effects.SettingsOverrideMode.Custom, EffectsAccountFixture.Secret)))).IsSuccess);
        var snapshots = await Query<Effects.HostWorldSnapshotsRequest, Effects.HostWorldSnapshots>(Effects.SidecarEffectsRoutes.WorldSnapshots,
            new(Path.GetFullPath("games/versions/one"), "world-one"));
        AssertTrue(snapshots.IsSuccess); AssertEqual("snapshot-one", snapshots.Value!.Items.Single().Identity);
        AssertTrue(client.Commands.TryResolve(Effects.MinecraftRouteIds.ProcessCancel, out var stop));
        var process = Guid.NewGuid();
        AssertTrue((await client.Commands.Dispatch(stop, new Effects.MinecraftCancelProcessCommand(process)).Completion).IsSuccess);
        AssertEqual(process, stopped);
        allowed = false;
        AssertFalse((await Refresh(4)).IsSuccess); AssertEqual(1, accounts.Refreshes);
        allowed = true; corrupt = true;
        AssertEqual(XsrErrorKind.ContractMismatch, (await client.DiscoverAsync()).Error!.Kind);
        AssertEqual("xsr.route_not_found", (await Refresh(4)).Error!.Code.Value);
        AssertTrue((await baseClient.DiscoverAsync()).IsSuccess);
    }

    private sealed class EffectsAccountFixture : IAccountLaunchAccess
    {
        internal const string Secret = "host-only-credential-5719";
        internal int Refreshes;
        internal LaunchProfile Profile = new() { Username = "player", Kind = LaunchProfileKind.Microsoft, Uuid = "uuid", RefreshToken = Secret };
        public XsrResult<LaunchProfile> GetProfile(int index) => throw new InvalidOperationException("Revision checks are required.");
        public ValueTask<XsrResult<LaunchProfile>> GetProfileAsync(int index, long expectedRosterRevision, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(index == 0 && expectedRosterRevision == 4 ? XsrResult.Success(Profile)
                : XsrResult.Failure<LaunchProfile>(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("accounts.revision_conflict"), "Changed")));
        }
        public ValueTask<XsrResult<MinecraftLaunchIdentity>> ResolveAsync(int accountIndex, LaunchProfile profile, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Refreshes++;
            AssertEqual(Secret, profile.RefreshToken);
            return ValueTask.FromResult(XsrResult.Success(new MinecraftLaunchIdentity("player", "uuid", Secret, MinecraftLaunchIdentityMode.Microsoft)));
        }
    }
}
