using System.Security.Cryptography;
using System.Text;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private static bool CustomSetting(QualifiedId id) => !id.Value.StartsWith("nexa:settings/", StringComparison.Ordinal);
    private readonly SemaphoreSlim _settingsNamespaceGate = new(1, 1);
    private readonly Dictionary<Guid, StorageNamespace> _settingsNamespaces = [];
    private VersionedSettingSchema PluginSettingSchema(HostInvocation c, QualifiedId id)
    {
        if (!id.Value.StartsWith(c.Identity.PluginId + ":", StringComparison.Ordinal)) throw new AbiHostException(AbiErrors.HANDLE_OWNER_MISMATCH);
        var entry = Application.Runtime.Definitions.Get(c, new(new("nexa:settings/schemas"), id));
        return VersionedSettingSchema.Read(entry.Value.Body);
    }
    private RegistrationRef RegisterSetting(HostInvocation c, VersionedSettingSchema value)
    {
        if (value.Version == 0 || !Abi100Schemas.Validate(value.Schema.ValueSchema, value.Schema.Default)
            || value.Schema.Scopes.Length is < 1 or > 2 || value.Schema.Scopes.Distinct().Count() != value.Schema.Scopes.Length
            || value.Schema.Scopes.Any(s => s is not ("global" or "instance"))) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        return Application.Runtime.Definitions.Stage(c, [new(value.Schema.Id, new("nexa:settings/schemas"), Abi100Schemas.VersionedSettingSchema, VersionedSettingSchema.Write(value), [])]).Single();
    }
    private async ValueTask<(StorageNamespace Namespace, string Key, StoredPluginSetting Value, Revision? Revision)> PluginSettingValue(HostInvocation c, SettingQuery query, VersionedSettingSchema schema)
    {
        string? instance = SettingsTarget(c, query.Target);
        if (!schema.Schema.Scopes.Contains(query.Target.Scope)) throw new AbiHostException(AbiErrors.CAPABILITY_DENIED);
        StorageNamespace ns;
        await _settingsNamespaceGate.WaitAsync(c.Options.Cancellation).ConfigureAwait(false);
        try
        {
            lock (_planGate) _settingsNamespaces.TryGetValue(c.Identity.LoadInstanceId, out ns!);
            if (ns is null)
            {
                ns = await Application.Runtime.Storage.Open(c, new(new(c.Identity.PluginId + ":host-settings"), 1)).ConfigureAwait(false);
                lock (_planGate) _settingsNamespaces[c.Identity.LoadInstanceId] = ns;
            }
        }
        finally { _settingsNamespaceGate.Release(); }
        // Entity handles are load-local; persistence uses the admitted canonical instance path.
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query.Id.Value + "\0" + (instance ?? "global"))));
        try
        {
            var stored = await Application.Runtime.Storage.Read(c, new(ns, key)).ConfigureAwait(false);
            if (stored.Schema != Abi100Schemas.StoredPluginSetting) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            return (ns, key, StoredPluginSetting.Read(stored.Value), stored.Revision);
        }
        catch (AbiHostException error) when (error.Code == AbiErrors.NOT_FOUND) { return (ns, key, new(0, schema.Schema.Default), null); }
    }
    private async ValueTask<EffectiveSetting> ReadPluginSetting(HostInvocation c, SettingQuery query)
    {
        var schema = PluginSettingSchema(c, query.Id);
        var stored = await PluginSettingValue(c, query, schema).ConfigureAwait(false);
        if (stored.Value.Version != 0 && stored.Value.Version != schema.Version || !Abi100Schemas.Validate(schema.Schema.ValueSchema, stored.Value.Value)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        return new(query.Id, stored.Value.Value, stored.Revision is null ? "default" : "plugin", stored.Revision ?? new("uninitialized"));
    }
    private async ValueTask<EffectiveSetting> WritePluginSetting(HostInvocation c, SettingChange change)
    {
        var schema = PluginSettingSchema(c, change.Id);
        if (!Abi100Schemas.Validate(schema.Schema.ValueSchema, change.Value)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var stored = await PluginSettingValue(c, new(change.Id, change.Target), schema).ConfigureAwait(false);
        if (change.ExpectedRevision != (stored.Revision ?? new Revision("uninitialized"))) throw new AbiHostException(AbiErrors.STALE_REVISION);
        var result = await Application.Runtime.Storage.Write(c, new(stored.Namespace, stored.Key, StoredPluginSetting.Write(new(schema.Version, change.Value)), Abi100Schemas.StoredPluginSetting, stored.Revision)).ConfigureAwait(false);
        _changes.Writer.TryWrite(true);
        return new(change.Id, change.Value, "plugin", result.Revision);
    }
    private async ValueTask<MutationPlan> PreviewPluginSetting(HostInvocation c, SettingChange change)
    {
        var schema = PluginSettingSchema(c, change.Id);
        if (!Abi100Schemas.Validate(schema.Schema.ValueSchema, change.Value)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        if ((await ReadPluginSetting(c, new(change.Id, change.Target)).ConfigureAwait(false)).Revision != change.ExpectedRevision) throw new AbiHostException(AbiErrors.STALE_REVISION);
        return Plan(c, "settings/apply", SettingChange.Write(change), "Change " + change.Id.Value, async token =>
        {
            var caller = c with { Options = c.Options with { Cancellation = token } };
            if (ProjectionRevision(PluginSettingSchema(caller, change.Id)) != ProjectionRevision(schema) || (await ReadPluginSetting(caller, new(change.Id, change.Target)).ConfigureAwait(false)).Revision != change.ExpectedRevision) throw new AbiHostException(AbiErrors.STALE_REVISION);
        }, async token => EffectiveSetting.Write(await WritePluginSetting(c with { Options = c.Options with { Cancellation = token } }, change).ConfigureAwait(false)));
    }
    private IEnumerable<HostCapabilityRoute> PluginSettingRoutes => [
        HostRoutes.Typed<SettingSchema, RegistrationRef>("Settings", "RegisterSchema", (c, v) => ValueTask.FromResult(RegisterSetting(c, new(v, 1)))),
        HostRoutes.Typed<VersionedSettingSchema, RegistrationRef>("Settings", "RegisterVersionedSchema", (c, v) => ValueTask.FromResult(RegisterSetting(c, v))),
        HostRoutes.Typed<SettingMigration, OperationRef>("Settings", "Migrate", async (c, v) =>
        {
            var schema = PluginSettingSchema(c, v.Id);
            if (v.Handler == 0 || v.FromVersion >= v.ToVersion || v.ToVersion != schema.Version) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var stored = await PluginSettingValue(c, new(v.Id, v.Target), schema).ConfigureAwait(false);
            if (stored.Value.Version != v.FromVersion) throw new AbiHostException(AbiErrors.STALE_REVISION);
            return Services.StartHostOperation(c, "nexa.settings.write", Abi100Schemas.EffectiveSetting, async token =>
            {
                var caller = c with { Options = c.Options with { Cancellation = token } };
                await Confirm(caller, "settings/migrate", "Migrate " + v.Id.Value + " to version " + v.ToVersion, SettingMigration.Write(v), token).ConfigureAwait(false);
                var output = await Services.QueryOwnHostCallback(caller, "nexa.settings.write", v.Handler,
                    SettingMigrationInput.Write(new(v.FromVersion, v.ToVersion, stored.Value.Value)), schema.Schema.ValueSchema).ConfigureAwait(false);
                if (ProjectionRevision(PluginSettingSchema(caller, v.Id)) != ProjectionRevision(schema)) throw new AbiHostException(AbiErrors.STALE_REVISION);
                return AbiReply<AbiValue>.Ok(EffectiveSetting.Write(await WritePluginSetting(caller, new(v.Id, v.Target, output, stored.Revision ?? new("uninitialized"))).ConfigureAwait(false)));
            });
        })
    ];
}
