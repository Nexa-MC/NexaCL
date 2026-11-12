using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Settings;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private ValueTask<SettingsLaunchProfilesSnapshot> ReadProfiles(string instance, CancellationToken token) =>
        Query<SettingsLaunchProfilesQuery, SettingsLaunchProfilesSnapshot>(foundationQueries, SettingsLaunchProfileContract.Query, new(instance), token);
    private EntityId ProfileId(HostInvocation c, string instance, string id) => new(Token(c, "profile:" + instance + ":" + id));
    private SettingsProfile ProjectProfile(HostInvocation c, string instance, SettingsLaunchProfile profile, long revision) => new(
        ProfileId(c, instance, profile.Id), profile.Name,
        profile.Values.Where(p => SettingsPolicySchema.ByKey.TryGetValue(p.Key, out var definition) && definition.Exportable && p.Key is not ("network.proxy-user" or "network.proxy-password"))
            .Select(p => new EffectiveSetting(new("nexa:settings/" + p.Key), DataCodec.Map(("mode", new AbiValue.Utf8String(p.Value.Mode.ToString())),
                ("value", p.Value.Value is { } text ? new AbiValue.Utf8String(text) : new AbiValue.Null())), "Profile", PolicyRevision(c, revision))).ToImmutableArray(), PolicyRevision(c, revision));
    private IEnumerable<HostCapabilityRoute> ProfileRoutes => [
        HostRoutes.Typed<PageRequest, Page<SettingsProfile>>("Settings", "ListProfiles", async (c, v) =>
        {
            string instance = SelectedInstanceDirectory(); var profiles = await ReadProfiles(instance, c.Options.Cancellation).ConfigureAwait(false);
            return Page(c, v, profiles.Profiles.Select(p => ProjectProfile(c, instance, p, profiles.Revision)).ToImmutableArray(), "profiles:" + instance, profiles.Revision);
        }),
        HostRoutes.Typed<SettingsProfile, SettingsProfile>("Settings", "SaveProfile", async (c, v) =>
        {
            string instance = SelectedInstanceDirectory(); var before = await ReadProfiles(instance, c.Options.Cancellation).ConfigureAwait(false);
            if (v.Revision != PolicyRevision(c, before.Revision)) throw new AbiHostException(AbiErrors.CONFLICT);
            if (v.Name.Length is < 1 or > 128 || v.Name.Any(char.IsControl) || v.Values.Length > 256 || v.Values.Select(p => p.Id).Distinct().Count() != v.Values.Length) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var prior = before.Profiles.FirstOrDefault(p => ProfileId(c, instance, p.Id) == v.Id);
            string id = prior?.Id ?? Guid.NewGuid().ToString("N");
            var values = v.Values.ToDictionary(p => SettingKey(p.Id), p => ParseOverride(p.Value), StringComparer.Ordinal);
            await Confirm(c, "settings/save-profile", "Save launch profile " + v.Name, SettingsProfile.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            await Command(foundationCommands, SettingsLaunchProfileContract.Save,
                new SettingsLaunchProfileSaveCommand(instance, id, v.Name, values, prior?.Overlay ?? new(), before.Revision), c.Options.Cancellation).ConfigureAwait(false);
            var after = await ReadProfiles(instance, c.Options.Cancellation).ConfigureAwait(false);
            return ProjectProfile(c, instance, after.Profiles.Single(p => p.Id == id), after.Revision);
        }),
        HostRoutes.Typed<EntityRequest, SettingsProfile>("Settings", "SelectProfile", async (c, v) =>
        {
            string instance = SelectedInstanceDirectory(); var before = await ReadProfiles(instance, c.Options.Cancellation).ConfigureAwait(false);
            var profile = before.Profiles.FirstOrDefault(p => ProfileId(c, instance, p.Id) == v.Id) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            await Command(foundationCommands, SettingsLaunchProfileContract.Select, new SettingsLaunchProfileSelectCommand(instance, profile.Id, before.Revision), c.Options.Cancellation).ConfigureAwait(false);
            var after = await ReadProfiles(instance, c.Options.Cancellation).ConfigureAwait(false);
            return ProjectProfile(c, instance, after.Profiles.Single(p => p.Id == profile.Id), after.Revision);
        })
    ];
}
