using System.Collections.Immutable;
using System.Security.Cryptography;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private volatile bool _developerEnabled;
    private long _developerRevision = -1;
    public bool DeveloperEnabled => _developerEnabled;
    private async ValueTask RefreshDeveloperModeAsync()
    {
        if (foundationQueries is null || !store.TryResolve(SettingsPolicyContract.RevisionKey, out var id)) return;
        var revision = store.Read<long>(id);
        if (!revision.IsAvailable || revision.Value == _developerRevision) return;
        if (!foundationQueries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var route)) return;
        var result = await foundationQueries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(route, new()).ConfigureAwait(false);
        _developerEnabled = result.IsSuccess && result.Value!.Values.Any(v => v.Key == "developer.enabled" && v.Value.Value == "true");
        if (result.IsSuccess) _developerRevision = revision.Value;
    }
    private static async ValueTask<TOut> Query<TIn, TOut>(XsrQueryRouter? router, XsrSemanticId semantic, TIn input, CancellationToken token) where TIn : notnull
    {
        if (router is null || !router.TryResolve(semantic, out var route)) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        var result = await router.QueryAsync<TIn, TOut>(route, input, cancellationToken: token).ConfigureAwait(false);
        if (!result.IsSuccess) throw new AbiHostException(ErrorCode(result.Error));
        return result.Value!;
    }
    private static async ValueTask Command<T>(XsrCommandRouter? router, XsrSemanticId semantic, T input, CancellationToken token) where T : notnull
    {
        if (router is null) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        Require(await router.Dispatch(Resolve(router, semantic), input, cancellationToken: token).Completion.ConfigureAwait(false));
    }
    private sealed record PendingPlan(PluginIdentity Owner, string Kind, MutationPlan Public, DateTimeOffset Expires,
        Func<CancellationToken, ValueTask> Validate, Func<CancellationToken, ValueTask<AbiValue>> Execute, Func<HostInvocation, CancellationToken, ValueTask<RegistrationRef>>? Preview, AbiValue? PreviewBody = null);
    private readonly object _planGate = new();
    private readonly Dictionary<EntityId, PendingPlan> _plans = [];
    private IActionConfirmation? _confirmation;
    private PluginApplication? _application;
    public void BindApplication(PluginApplication application)
    {
        if (_application is not null) throw new InvalidOperationException("Application already bound.");
        _application = application; application.ResourcesRetired += RetireResources;
    }
    private void RetireResources(string pluginId)
    {
        RetireNetwork(pluginId);
        lock (_planGate)
        {
            foreach (var key in _installRecoveries.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _installRecoveries.Remove(key);
            foreach (var key in _settingsNamespaces.Where(p => p.Value.Namespace.Value == pluginId + ":host-settings").Select(p => p.Key).ToArray()) _settingsNamespaces.Remove(key);
            foreach (var key in _themeResources.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _themeResources.Remove(key);
            foreach (var key in _backupDestinations.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _backupDestinations.Remove(key);
            foreach (var key in _worldBackups.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _worldBackups.Remove(key);
            foreach (var key in _worldDestinations.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _worldDestinations.Remove(key);
            foreach (var key in _backupIdentities.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _backupIdentities.Remove(key);
            foreach (var key in _javaOffers.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _javaOffers.Remove(key);
            foreach (var key in _pluginDownloads.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _pluginDownloads.Remove(key);
            foreach (var key in _installDrafts.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _installDrafts.Remove(key);
            foreach (var key in _installPlans.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _installPlans.Remove(key);
            foreach (var key in _installOperations.Where(p => p.Value.PluginId == pluginId).Select(p => p.Key).ToArray()) _installOperations.Remove(key);
            foreach (var key in _launchGrants.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _launchGrants.Remove(key);
            foreach (var key in _launchPlans.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _launchPlans.Remove(key);
        }
        lock (_planGate) foreach (var key in _contentIdentities.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _contentIdentities.Remove(key);
        lock (_planGate) foreach (var key in _plans.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _plans.Remove(key);
        lock (_watchGate)
        {
            foreach (var key in _asyncWatches.Where(p => p.Value.Caller.Identity.PluginId == pluginId).Select(p => p.Key).ToArray()) _asyncWatches.Remove(key);
            foreach (var key in _watches.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _watches.Remove(key);
            foreach (var key in _settingWatches.Where(p => p.Value.Caller.Identity.PluginId == pluginId).Select(p => p.Key).ToArray()) _settingWatches.Remove(key);
            foreach (var key in _logWatches.Where(p => p.Value.Caller.Identity.PluginId == pluginId).Select(p => p.Key).ToArray()) _logWatches.Remove(key);
            foreach (var key in _lanWatches.Where(p => p.Value.Caller.Identity.PluginId == pluginId).Select(p => p.Key).ToArray()) _lanWatches.Remove(key);
            foreach (var key in _fileWatches.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _fileWatches.Remove(key);
        }
    }
    private PluginApplication Application => _application ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
    public void BindConfirmation(IActionConfirmation confirmation) => _confirmation = confirmation;
    private MutationPlan Plan(HostInvocation c, string kind, AbiValue inputs, string summary,
        Func<CancellationToken, ValueTask> validate, Func<CancellationToken, ValueTask<AbiValue>> execute, Func<HostInvocation, CancellationToken, ValueTask<RegistrationRef>>? preview = null)
    {
        var plan = new MutationPlan(new(Guid.NewGuid().ToString("N")), new(Guid.NewGuid().ToString("N")),
            Convert.ToHexString(SHA256.HashData(AbiValueBinary.Encode(inputs))), new(summary, []), ConfirmationRequirement.UserConfirmation, [new("input", inputs)]);
        lock (_planGate)
        {
            foreach (var expired in _plans.Where(p => p.Value.Expires < DateTimeOffset.UtcNow).Select(p => p.Key).ToArray()) _plans.Remove(expired);
            if (_plans.Count >= 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            _plans.Add(plan.Id, new(c.Identity, kind, plan, DateTimeOffset.UtcNow.AddMinutes(10), validate, execute, preview));
        }
        return plan;
    }
    private OperationRef ExecutePlan(HostInvocation c, PlanRequest request, string kind, string permission, SchemaRef resultSchema)
    {
        var confirmation = _confirmation ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        PendingPlan plan;
        lock (_planGate)
        {
            if (!_plans.TryGetValue(request.Plan, out plan!) || plan.Owner != c.Identity || plan.Kind != kind) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            if (plan.Public.Revision != request.ExpectedRevision || plan.Expires < DateTimeOffset.UtcNow) throw new AbiHostException(AbiErrors.CONFLICT);
            _plans.Remove(request.Plan);
        }
        return Services.StartHostOperation(c, permission, resultSchema, async token =>
        {
            await plan.Validate(token).ConfigureAwait(false);
            var actionId = new QualifiedId("nexa:" + kind);
            var action = new ActionDescriptor(actionId, Abi100Schemas.MutationPlan, resultSchema, [], ["application"], IdempotencyMode.OutcomeMayBeUnknown,
                true, true, ConfirmationRequirement.UserConfirmation, null, plan.Public.Summary, null);
            var requestBody = new ActionRequest(actionId, new(new("nexa:plan"), plan.Public.Id, "application"), MutationPlan.Write(plan.Public), plan.Public.Revision, plan.Public.Id.Value);
            if (!await confirmation.ConfirmAsync(new(Guid.NewGuid(), c.Identity, c.Identity, action, requestBody, c.Options.CorrelationId), token).ConfigureAwait(false))
                throw new AbiHostException(AbiErrors.PERMISSION_DENIED);
            token.ThrowIfCancellationRequested();
            await plan.Validate(token).ConfigureAwait(false);
            return AbiReply<AbiValue>.Ok(await plan.Execute(token).ConfigureAwait(false));
        });
    }
    private Revision PolicyRevision(HostInvocation c, long revision) => new(Token(c, "settings:" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    private string? SettingsTarget(HostInvocation c, ScopedTarget target)
    {
        if (target.Kind.Value == "nexa:application" && target.Scope == "global" && target.Entity is null) return null;
        if (target.Kind.Value == "nexa:instance" && target.Scope == "instance" && target.Entity is { } id) return FindInstance(c, id).Instance.DirectoryPath;
        throw new AbiHostException(AbiErrors.HANDLE_INVALID);
    }
}
