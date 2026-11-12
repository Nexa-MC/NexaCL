using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Settings;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private static string SettingKey(QualifiedId id)
    {
        const string prefix = "nexa:settings/";
        if (!id.Value.StartsWith(prefix, StringComparison.Ordinal)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        string key = id.Value[prefix.Length..];
        var definition = SettingsPolicySchema.Definitions.FirstOrDefault(d => d.Key == key);
        // Proxy credentials and other non-exportable values never cross the plugin boundary.
        if (definition is null || !definition.Exportable || key is "network.proxy-user" or "network.proxy-password") throw new AbiHostException(AbiErrors.PERMISSION_DENIED);
        return key;
    }
    private ValueTask<SettingsEffectiveSnapshot> ReadSettings(string? instance, CancellationToken token) =>
        Query<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(foundationQueries, SettingsPolicyContract.EffectiveQuery, new(instance), token);
    private EffectiveSetting ProjectSetting(HostInvocation c, SettingsEffectiveSnapshot snapshot, string key)
    {
        var value = snapshot.Values.FirstOrDefault(v => v.Key == key) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        return new(new("nexa:settings/" + key), DataCodec.Map(("mode", new AbiValue.Utf8String(value.Value.Mode.ToString())),
            ("value", value.Value.Value is { } text ? new AbiValue.Utf8String(text) : new AbiValue.Null())), value.Source.ToString(), PolicyRevision(c, snapshot.Revision));
    }
    private async ValueTask<EffectiveSetting> ReadSetting(HostInvocation c, SettingQuery request)
    {
        if (CustomSetting(request.Id)) return await ReadPluginSetting(c, request).ConfigureAwait(false);
        string key = SettingKey(request.Id); string? instance = SettingsTarget(c, request.Target);
        return ProjectSetting(c, await ReadSettings(instance, c.Options.Cancellation).ConfigureAwait(false), key);
    }
    private static StateSnapshot SettingState(EffectiveSetting setting) => new(EffectiveSetting.Write(setting), Abi100Schemas.EffectiveSetting, setting.Revision, DateTimeOffset.UtcNow, null);
    private static SettingsOverride ParseOverride(AbiValue input)
    {
        var map = DataCodec.ReadMap(input);
        string mode = DataCodec.ReadString(DataCodec.Field(map, "mode"));
        var value = DataCodec.Field(map, "value");
        if (!Enum.TryParse<SettingsOverrideMode>(mode, out var parsed) || !Enum.IsDefined(parsed)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        return new(parsed, value is AbiValue.Null ? null : DataCodec.ReadString(value));
    }
    private IEnumerable<HostCapabilityRoute> SettingsRoutes => PluginSettingRoutes.Concat(BuiltInSettingRoutes);
    private IEnumerable<HostCapabilityRoute> BuiltInSettingRoutes => [
        HostRoutes.Typed<SettingQuery, StateSnapshot>("Settings", "Get", async (c, v) => SettingState(await ReadSetting(c, v).ConfigureAwait(false))),
        HostRoutes.Typed<SettingQuery, EffectiveSetting>("Settings", "GetEffective", ReadSetting),
        HostRoutes.Typed<SettingChange, MutationPlan>("Settings", "PreviewChange", async (c, v) =>
        {
            if (CustomSetting(v.Id)) return await PreviewPluginSetting(c, v).ConfigureAwait(false);
            string key = SettingKey(v.Id); string? instance = SettingsTarget(c, v.Target);
            var before = await ReadSettings(instance, c.Options.Cancellation).ConfigureAwait(false);
            if (v.ExpectedRevision != PolicyRevision(c, before.Revision)) throw new AbiHostException(AbiErrors.CONFLICT);
            var mutation = new SettingsMutation(key, instance is null ? SettingsLayer.Global : SettingsLayer.Instance, ParseOverride(v.Value), instance);
            var preview = await Query<SettingsPreviewQuery, SettingsEffectiveSnapshot>(foundationQueries, SettingsPolicyContract.PreviewQuery, new([mutation], instance), c.Options.Cancellation).ConfigureAwait(false);
            if (preview.Values.Any(s => s.ValidationError is not null)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            return Plan(c, "settings/apply", SettingChange.Write(v), "Change " + key, async token =>
            {
                SettingsTarget(c, v.Target);
                if ((await ReadSettings(instance, token).ConfigureAwait(false)).Revision != before.Revision) throw new AbiHostException(AbiErrors.CONFLICT);
            }, async token =>
            {
                await Command(foundationCommands, SettingsPolicyContract.BatchCommand, new SettingsBatchCommand([mutation], before.Revision), token).ConfigureAwait(false);
                return EffectiveSetting.Write(ProjectSetting(c, await ReadSettings(instance, token).ConfigureAwait(false), key));
            });
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Settings", "Apply", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "settings/apply", "nexa.settings.write", Abi100Schemas.EffectiveSetting))),
        HostRoutes.Typed<SettingQuery, StateSnapshot>("Settings", "Reset", async (c, v) =>
        {
            if (CustomSetting(v.Id))
            {
                var schema = PluginSettingSchema(c, v.Id);
                var pluginBefore = await ReadPluginSetting(c, v).ConfigureAwait(false);
                await Confirm(c, "settings/reset", "Reset " + v.Id.Value, SettingQuery.Write(v), c.Options.Cancellation).ConfigureAwait(false);
                return SettingState(await WritePluginSetting(c, new(v.Id, v.Target, schema.Schema.Default, pluginBefore.Revision)).ConfigureAwait(false));
            }
            string key = SettingKey(v.Id); string? instance = SettingsTarget(c, v.Target);
            var before = await ReadSettings(instance, c.Options.Cancellation).ConfigureAwait(false);
            await Confirm(c, "settings/reset", "Reset " + key, SettingQuery.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            SettingsTarget(c, v.Target);
            await Command(foundationCommands, SettingsPolicyContract.BatchCommand, new SettingsBatchCommand([
                new(key, instance is null ? SettingsLayer.Global : SettingsLayer.Instance, new(SettingsOverrideMode.Inherit), instance)], before.Revision), c.Options.Cancellation).ConfigureAwait(false);
            return SettingState(ProjectSetting(c, await ReadSettings(instance, c.Options.Cancellation).ConfigureAwait(false), key));
        }),
        HostRoutes.Typed<SettingQuery, SubscriptionRef>("Settings", "Watch", async (c, v) =>
        {
            var value = await ReadSetting(c, v).ConfigureAwait(false);
            lock (_watchGate)
            {
                if (_settingWatches.Count >= 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                var reference = Services.CreateHostSubscription(c, "nexa.settings/100", "nexa.settings.read", EffectiveSetting.Write(value), 64);
                _settingWatches.Add(reference.Token, (c, v, reference, value.Revision)); return reference;
            }
        }),
        HostRoutes.Typed<EntityRequest, Page<EffectiveSetting>>("Minecraft.Instances", "ReadEffectiveSettings", async (c, v) =>
        {
            var instance = FindInstance(c, v.Id);
            var snapshot = await ReadSettings(instance.Instance.DirectoryPath, c.Options.Cancellation).ConfigureAwait(false);
            var values = snapshot.Values.Where(v => SettingsPolicySchema.Definitions.Any(d => d.Key == v.Key && d.Exportable) && v.Key is not ("network.proxy-user" or "network.proxy-password"))
                .Select(v => ProjectSetting(c, snapshot, v.Key)).ToImmutableArray();
            return new(values, null, PolicyRevision(c, snapshot.Revision));
        })
    ];
    private readonly Dictionary<Guid, (HostInvocation Caller, SettingQuery Query, SubscriptionRef Reference, Revision Revision)> _settingWatches = [];
    private async ValueTask RefreshSettingWatchesAsync()
    {
        (HostInvocation Caller, SettingQuery Query, SubscriptionRef Reference, Revision Revision)[] watches;
        lock (_watchGate) watches = _settingWatches.Values.ToArray();
        foreach (var watch in watches)
        {
            try
            {
                var current = await ReadSetting(watch.Caller with { Options = watch.Caller.Options with { Cancellation = default } }, watch.Query).ConfigureAwait(false);
                if (current.Revision == watch.Revision) continue;
                bool live = Services.PublishHostSubscription(watch.Caller.Identity, watch.Reference, EffectiveSetting.Write(current));
                lock (_watchGate)
                {
                    if (!live) _settingWatches.Remove(watch.Reference.Token);
                    else _settingWatches[watch.Reference.Token] = (watch.Caller, watch.Query, watch.Reference, current.Revision);
                }
            }
            catch (AbiHostException)
            {
                Services.PublishHostSubscription(watch.Caller.Identity, watch.Reference, new AbiValue.Null());
                lock (_watchGate) _settingWatches.Remove(watch.Reference.Token);
            }
        }
    }
    private async ValueTask Confirm(HostInvocation c, string kind, string summary, AbiValue input, CancellationToken token)
    {
        var confirmation = _confirmation ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        var id = new QualifiedId("nexa:" + kind);
        var action = new ActionDescriptor(id, Abi100Schemas.Empty, Abi100Schemas.Empty, [], ["application"], IdempotencyMode.OutcomeMayBeUnknown,
            true, false, ConfirmationRequirement.UserConfirmation, null, new(summary, []), null);
        var request = new ActionRequest(id, new(new("nexa:application"), null, "application"), input, null, c.Options.OperationId.ToString("N"));
        if (!await confirmation.ConfirmAsync(new(Guid.NewGuid(), c.Identity, c.Identity, action, request, c.Options.CorrelationId), token).ConfigureAwait(false)) throw new AbiHostException(AbiErrors.PERMISSION_DENIED);
        token.ThrowIfCancellationRequested();
    }
}
