using Nexa.Services.Accounts;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>General domain projections. Account credentials and runtime capability objects remain local.</summary>
public sealed class SidecarHostEffects
{
    public XsrCommandRouter Commands { get; }
    public XsrQueryRouter Queries { get; }

    public SidecarHostEffects(IAccountLaunchAccess accounts, IAccountLaunchIdentityResolver resolver,
        Func<string, CancellationToken, ValueTask<JavaRuntimeCandidate?>> inspect,
        Func<string?, bool, CancellationToken, ValueTask<JavaRuntimeCandidate?>> select,
        Func<string, CancellationToken, ValueTask<JavaRequirementResolution>> requirements,
        XsrQueryRouter foundation, IXsrDispatchObserver observer)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(inspect);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(foundation);
        XsrCommandRouterBuilder commands = new(); XsrQueryRouterBuilder queries = new();
        queries.Register<HostAccountRequest, HostAccountEligibility>(SidecarEffectsRoutes.AccountEligibility, async (request, token) =>
        {
            if (request.Index < 0 || request.ExpectedRosterRevision < 0) return Invalid<HostAccountEligibility>();
            var result = await accounts.GetProfileAsync(request.Index, request.ExpectedRosterRevision, token).ConfigureAwait(false);
            if (!result.IsSuccess) return XsrResult.Failure<HostAccountEligibility>(result.Error!);
            var profile = result.Value!;
            bool allowed = profile.Kind switch
            {
                LaunchProfileKind.Offline => !string.IsNullOrWhiteSpace(profile.Username),
                LaunchProfileKind.Microsoft => !string.IsNullOrWhiteSpace(profile.Uuid) && (Present(profile.AccessToken) || Present(profile.RefreshToken)),
                LaunchProfileKind.LittleSkin => !string.IsNullOrWhiteSpace(profile.Uuid) && (Present(profile.AccessToken) || Present(profile.ProviderAccessToken) || Present(profile.RefreshToken)),
                LaunchProfileKind.ThirdParty or LaunchProfileKind.Nide8 => Present(profile.Uuid) && Present(profile.AuthServer) && Present(profile.AccessToken),
                _ => false
            };
            return XsrResult.Success(new HostAccountEligibility(allowed, allowed && profile.Kind != LaunchProfileKind.Offline));
        });
        commands.Register<HostAccountRequest>(SidecarEffectsRoutes.AccountRefresh, async (request, token) =>
        {
            if (request.Index < 0 || request.ExpectedRosterRevision < 0) return XsrResult.Failure(InvalidError());
            var profile = await accounts.GetProfileAsync(request.Index, request.ExpectedRosterRevision, token).ConfigureAwait(false);
            if (!profile.IsSuccess) return XsrResult.Failure(profile.Error!);
            var resolved = await resolver.ResolveAsync(request.Index, profile.Value!, token).ConfigureAwait(false);
            return resolved.IsSuccess ? XsrResult.Success() : XsrResult.Failure(resolved.Error!);
        });
        queries.Register<HostJavaInspectRequest, HostJavaCandidate>(SidecarEffectsRoutes.JavaInspect, async (request, token) =>
            !Absolute(request.Executable) ? Invalid<HostJavaCandidate>() : XsrResult.Success(new HostJavaCandidate(await inspect(request.Executable, token).ConfigureAwait(false))));
        queries.Register<HostJavaSelectRequest, HostJavaCandidate>(SidecarEffectsRoutes.JavaSelect, async (request, token) =>
            request.VersionLocal && request.InstanceDirectory is null || request.InstanceDirectory is not null && !Absolute(request.InstanceDirectory)
                ? Invalid<HostJavaCandidate>() : XsrResult.Success(new HostJavaCandidate(await select(request.InstanceDirectory, request.VersionLocal, token).ConfigureAwait(false))));
        queries.Register<HostJavaRequirementsRequest, JavaRequirementResolution>(SidecarEffectsRoutes.JavaRequirements, async (request, token) =>
            !Absolute(request.InstanceDirectory) ? Invalid<JavaRequirementResolution>() : XsrResult.Success(await requirements(request.InstanceDirectory, token).ConfigureAwait(false)));
        queries.Register<HostEmptyRequest, HostPowerState>(SidecarEffectsRoutes.Power, async (_, token) =>
        {
            var facts = await Query<MachineCapabilityQuery, MachineCapabilitySnapshot>(foundation, MachineCapabilityStateContract.SnapshotQuery, new(), token).ConfigureAwait(false);
            if (!facts.IsSuccess) return XsrResult.Failure<HostPowerState>(facts.Error!);
            var source = facts.Value!.Get<string>(MachineEnvironmentCatalog.PowerSource.Id);
            var level = facts.Value.Get<int>(MachineEnvironmentCatalog.PowerBatteryLevelPercent.Id);
            var profile = facts.Value.Get<string>(MachineEnvironmentCatalog.PowerProfileCurrent.Id);
            if (source?.Availability != CapabilityAvailability.Available || source.Value is not ("电池" or "外接电源") || profile?.Availability != CapabilityAvailability.Available)
                return XsrResult.Failure<HostPowerState>(new(XsrErrorKind.Unavailable, XsrSemanticId.Parse("xsr.unavailable"), "Power information is unavailable."));
            return XsrResult.Success(new HostPowerState(source.Value == "电池", level is { Availability: CapabilityAvailability.Available, Value: >= 0 and <= 100 } ? level.Value : null,
                profile.Value is "节能" or "power-saver" or "low-power"));
        });
        queries.Register<HostPreflightRequest, HostPreflightReport>(SidecarEffectsRoutes.Preflight, async (request, token) =>
        {
            if (!Absolute(request.InstanceDirectory) || !Absolute(request.MinecraftRootDirectory) || string.IsNullOrWhiteSpace(request.InstanceId)) return Invalid<HostPreflightReport>();
            var facts = await Query<MachineCapabilityQuery, MachineCapabilitySnapshot>(foundation, MachineCapabilityStateContract.SnapshotQuery,
                new(request.InstanceDirectory, request.InstanceId, request.MinecraftRootDirectory) { RefreshInstance = true }, token).ConfigureAwait(false);
            if (!facts.IsSuccess) return XsrResult.Failure<HostPreflightReport>(facts.Error!);
            var report = await Query<LaunchPreflightQuery, CapabilityPreflightReport>(foundation, MachineCapabilityStateContract.PreflightQuery, new(facts.Value!), token).ConfigureAwait(false);
            if (!report.IsSuccess) return XsrResult.Failure<HostPreflightReport>(report.Error!);
            static HostPreflightIssue Project(CapabilityPreflightIssue issue) => new(issue.Code, issue.Title, issue.Description,
                issue.Severity, issue.Category, issue.Certainty, issue.HardConstraint, issue.Evidence, issue.Causes,
                issue.Remediations, issue.CanBypass, issue.Suppressible);
            return XsrResult.Success(new HostPreflightReport(report.Value!.Issues.Select(Project).ToArray(), report.Value.CollapsedIssues.Select(Project).ToArray(), report.Value.OverallSeverity));
        });
        queries.Register<HostEmptyRequest, HostSettingsDefinitions>(SidecarEffectsRoutes.SettingsDefinitions, (_, _) =>
            ValueTask.FromResult(XsrResult.Success(new HostSettingsDefinitions(SettingsPolicySchema.Definitions.Where(PublicSetting).ToArray(), SettingsPolicySchema.InstanceJavaRuntime))));
        queries.Register<HostSettingsValidateRequest, HostSettingsValidation>(SidecarEffectsRoutes.SettingsValidate, (request, _) =>
        {
            if (!SettingsPolicySchema.ByKey.TryGetValue(request.Key, out var definition) || !PublicSetting(definition)) return ValueTask.FromResult(Invalid<HostSettingsValidation>());
            string? error = definition.Validate(request.Value);
            return ValueTask.FromResult(XsrResult.Success(new HostSettingsValidation(error is null, error)));
        });
        queries.Register<HostWorldSnapshotsRequest, HostWorldSnapshots>(SidecarEffectsRoutes.WorldSnapshots, async (request, token) =>
        {
            if (!Absolute(request.InstanceDirectory)) return Invalid<HostWorldSnapshots>();
            var snapshots = await Query<InstanceWorldSnapshotListQuery, IReadOnlyList<InstanceWorldSnapshot>>(foundation,
                InstanceWorldSnapshotContract.List, new(request.InstanceDirectory, request.WorldName), token).ConfigureAwait(false);
            return snapshots.IsSuccess ? XsrResult.Success(new HostWorldSnapshots(snapshots.Value!)) : XsrResult.Failure<HostWorldSnapshots>(snapshots.Error!);
        });
        Commands = commands.Build(observer); Queries = queries.Build(observer);
    }

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);
    private static bool Absolute(string? value) => !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value) && !value.Any(char.IsControl);
    private static bool PublicSetting(SettingsPolicyDefinition definition) => definition.Exportable && definition.Key is not ("network.proxy-user" or "network.proxy-password");
    private static XsrError InvalidError() => new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), "Invalid Host request.");
    private static XsrResult<T> Invalid<T>() => XsrResult.Failure<T>(InvalidError());
    private static ValueTask<XsrResult<TResult>> Query<TRequest, TResult>(XsrQueryRouter router, XsrSemanticId id, TRequest request, CancellationToken token) where TRequest : notnull
        => router.TryResolve(id, out var route) ? router.QueryAsync<TRequest, TResult>(route, request, cancellationToken: token)
            : ValueTask.FromResult(XsrResult.Failure<TResult>(XsrRuntimeErrors.RouteNotFound()));
}
