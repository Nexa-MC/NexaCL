using System.Collections.Immutable;
using System.Globalization;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Logging;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Settings;
using PublicLaunchProfile = Nexa.Plugin.Sdk.Abi100.Data.LaunchProfile;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private async ValueTask<(PublicLaunchProfile Public, SettingsEffectiveSnapshot Settings, string? ProfileId)> ReadLaunchProfile(HostInvocation c, EntityId instance, EntityId? profileId, CancellationToken token)
    {
        string directory = FindInstance(c, instance).Instance.DirectoryPath;
        var profiles = await ReadProfiles(directory, token).ConfigureAwait(false);
        string? raw = profiles.SelectedProfileId;
        if (profileId is { } requested)
        {
            raw = profiles.Profiles.FirstOrDefault(p => ProfileId(c, directory, p.Id) == requested)?.Id;
            if (raw is null && requested != ProfileId(c, directory, "default")) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            // The default identity is only offered when no named profile is selected.
            if (raw is null && profiles.SelectedProfileId is not null) throw new AbiHostException(AbiErrors.CONFLICT);
        }
        var effective = await Query<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(foundationQueries, SettingsPolicyContract.EffectiveQuery, new(directory) { ProfileId = raw }, token).ConfigureAwait(false);
        if (effective.Revision != profiles.Revision) throw new AbiHostException(AbiErrors.CONFLICT);
        string name = raw is null ? "Instance settings" : profiles.Profiles.Single(p => p.Id == raw).Name;
        return (ProjectLaunchProfile(c, directory, raw ?? "default", name, effective), effective, raw);
    }
    private PublicLaunchProfile ProjectLaunchProfile(HostInvocation c, string directory, string id, string name, SettingsEffectiveSnapshot effective)
    {
        SettingsEffectiveValue Get(string key) => effective.Values.FirstOrDefault(v => v.Key == key) ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        string Text(string key)
        {
            var setting = Get(key);
            if (setting.ValidationError is not null) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            return setting.Value.Value ?? SettingsPolicySchema.ByKey[key].DefaultValue;
        }
        long Number(string key) => long.TryParse(Text(key), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var arguments = ImmutableArray.CreateBuilder<LaunchArgument>();
        foreach (var (key, scope) in new[] { ("game.jvm", LaunchArgumentScope.Jvm), ("game.arguments", LaunchArgumentScope.Game) })
        {
            foreach (string value in Get(key).ArgumentRows.Select(value => value.Replace("\"", "", StringComparison.Ordinal)))
            {
                if (LogRedactor.Redact(value) != value) throw new AbiHostException(AbiErrors.PERMISSION_DENIED);
                arguments.Add(new(new("nexa:launch-argument/" + arguments.Count.ToString(CultureInfo.InvariantCulture)), value, scope));
            }
        }
        var environment = MinecraftLaunchHooks.ParseEnvironment(Text("game.environment"));
        if (environment.Any(p => LogRedactor.Redact(p.Key + "=" + p.Value) != p.Key + "=" + p.Value)) throw new AbiHostException(AbiErrors.PERMISSION_DENIED);
        var java = Get("java.runtime");
        EntityId? runtime = java.Value.Mode == SettingsOverrideMode.Custom && !string.IsNullOrWhiteSpace(java.Value.Value) && Path.IsPathFullyQualified(java.Value.Value)
            ? new(Token(c, "java:" + java.Value.Value)) : null;
        return new(ProfileId(c, directory, id), name, Get("game.memory").Value.Mode == SettingsOverrideMode.Auto ? 0 : Number("game.memory"), runtime,
            Number("game.width"), Number("game.height"), arguments.ToImmutable(), environment.Select(p => new NamedValue(p.Key, new AbiValue.Utf8String(p.Value))).ToImmutableArray(), PolicyRevision(c, effective.Revision));
    }
    private static string JoinArguments(IEnumerable<LaunchArgument> arguments)
    {
        // The launch policy tokenizer uses double quotes; ambiguous embedded quotes/newlines
        // must not turn one argument into additional JVM or game options.
        var values = arguments.Select(a => a.Value).ToArray();
        if (values.Any(v => v.Length == 0 || v.Contains('"') || v.Any(char.IsControl))) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        return string.Join(' ', values.Select(v => v.Length == 0 || v.Any(char.IsWhiteSpace) ? "\"" + v + "\"" : v));
    }
    private IEnumerable<HostCapabilityRoute> LaunchProfileRoutes => [
        HostRoutes.Typed<EntityRequest, PublicLaunchProfile>("Minecraft.Launch", "ReadEffectiveProfile", async (c, v) => (await ReadLaunchProfile(c, v.Id, null, c.Options.Cancellation).ConfigureAwait(false)).Public),
        HostRoutes.Typed<EntityRequest, Page<PublicLaunchProfile>>("Minecraft.Launch", "ListProfiles", async (c, v) =>
        {
            string directory = FindInstance(c, v.Id).Instance.DirectoryPath;
            var profiles = await ReadProfiles(directory, c.Options.Cancellation).ConfigureAwait(false);
            if (profiles.Profiles.Count > 255) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var result = ImmutableArray.CreateBuilder<PublicLaunchProfile>();
            if (profiles.SelectedProfileId is null) result.Add((await ReadLaunchProfile(c, v.Id, null, c.Options.Cancellation).ConfigureAwait(false)).Public);
            foreach (var profile in profiles.Profiles)
                result.Add((await ReadLaunchProfile(c, v.Id, ProfileId(c, directory, profile.Id), c.Options.Cancellation).ConfigureAwait(false)).Public);
            if (result.Any(p => p.Revision != PolicyRevision(c, profiles.Revision))) throw new AbiHostException(AbiErrors.CONFLICT);
            return new(result.ToImmutable(), null, PolicyRevision(c, profiles.Revision));
        }),
        HostRoutes.Typed<LaunchProfileWrite, PublicLaunchProfile>("Minecraft.Launch", "SaveProfile", async (c, v) =>
        {
            string directory = FindInstance(c, v.Instance).Instance.DirectoryPath;
            var profiles = await ReadProfiles(directory, c.Options.Cancellation).ConfigureAwait(false);
            if (v.ExpectedRevision != PolicyRevision(c, profiles.Revision)) throw new AbiHostException(AbiErrors.CONFLICT);
            var p = v.Profile;
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 128 || p.Name.Any(char.IsControl) || p.Arguments.Length > 128 || p.Environment.Length > 128 || p.Arguments.Any(a => !Enum.IsDefined(a.Scope))) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var prior = profiles.Profiles.FirstOrDefault(item => ProfileId(c, directory, item.Id) == p.Id);
            string raw = prior?.Id ?? Guid.NewGuid().ToString("N");
            var values = prior?.Values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal) ?? new Dictionary<string, SettingsOverride>(StringComparer.Ordinal);
            values["game.memory"] = new(p.MemoryMiB == 0 ? SettingsOverrideMode.Auto : SettingsOverrideMode.Custom, p.MemoryMiB == 0 ? null : p.MemoryMiB.ToString(CultureInfo.InvariantCulture));
            values["game.width"] = new(SettingsOverrideMode.Custom, p.Width.ToString(CultureInfo.InvariantCulture));
            values["game.height"] = new(SettingsOverrideMode.Custom, p.Height.ToString(CultureInfo.InvariantCulture));
            values["game.jvm"] = new(SettingsOverrideMode.Custom, JoinArguments(p.Arguments.Where(a => a.Scope == LaunchArgumentScope.Jvm)));
            values["game.arguments"] = new(SettingsOverrideMode.Custom, JoinArguments(p.Arguments.Where(a => a.Scope == LaunchArgumentScope.Game)));
            string environment = string.Join('\n', p.Environment.Select(item => item.Name + "=" + DataCodec.ReadString(item.Value)));
            if (p.Environment.Any(item => item.Name.Contains('=') || item.Name.Any(char.IsControl) || DataCodec.ReadString(item.Value).Any(char.IsControl))) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            _ = MinecraftLaunchHooks.ParseEnvironment(environment); values["game.environment"] = new(SettingsOverrideMode.Custom, environment);
            string? java = null;
            if (p.Java is { } selected)
            {
                var inventory = await JavaInventory(c.Options.Cancellation).ConfigureAwait(false);
                java = inventory.Runtimes.FirstOrDefault(j => j.IsAvailable && j.IsEnabled && new EntityId(Token(c, "java:" + j.Installation.JavaExecutablePath)) == selected)?.Installation.JavaExecutablePath ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            }
            values["java.runtime"] = new(java is null ? SettingsOverrideMode.Auto : SettingsOverrideMode.Custom, java);
            if (values.Any(pair => SettingsPolicySchema.ByKey[pair.Key].Validate(pair.Value) is not null)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            await Confirm(c, "launch/save-profile", "Save launch profile " + p.Name, LaunchProfileWrite.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            await Command(foundationCommands, SettingsLaunchProfileContract.Save, new SettingsLaunchProfileSaveCommand(directory, raw, p.Name, values, prior?.Overlay ?? new(), profiles.Revision), c.Options.Cancellation).ConfigureAwait(false);
            return (await ReadLaunchProfile(c, v.Instance, ProfileId(c, directory, raw), c.Options.Cancellation).ConfigureAwait(false)).Public;
        }),
        HostRoutes.Typed<LaunchProfileSelection, PublicLaunchProfile>("Minecraft.Launch", "SelectProfile", async (c, v) =>
        {
            string directory = FindInstance(c, v.Instance).Instance.DirectoryPath;
            var profiles = await ReadProfiles(directory, c.Options.Cancellation).ConfigureAwait(false);
            if (v.ExpectedRevision != PolicyRevision(c, profiles.Revision)) throw new AbiHostException(AbiErrors.CONFLICT);
            string? raw = profiles.Profiles.FirstOrDefault(p => ProfileId(c, directory, p.Id) == v.Profile)?.Id;
            if (raw is null && v.Profile != ProfileId(c, directory, "default")) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            await Command(foundationCommands, SettingsLaunchProfileContract.Select, new SettingsLaunchProfileSelectCommand(directory, raw, profiles.Revision), c.Options.Cancellation).ConfigureAwait(false);
            return (await ReadLaunchProfile(c, v.Instance, null, c.Options.Cancellation).ConfigureAwait(false)).Public;
        })
    ];
}
