using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Settings;
using PublicLaunchProfile = Nexa.Plugin.Sdk.Abi100.Data.LaunchProfile;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed record LaunchExtensions(PublicLaunchProfile Profile, ImmutableArray<PublishedDefinition> Definitions, ImmutableArray<QualifiedId> Accepted, ImmutableArray<ValidationIssue> Suppressed);
    private RegistrationRef RegisterLaunchExtension(HostInvocation c, LaunchContributionRegistration value, bool preflight)
    {
        if (value.Handler == 0 || value.Fields.Distinct(StringComparer.Ordinal).Count() != value.Fields.Length
            || preflight && value.Fields.Length != 0 || !preflight && (value.Fields.Length == 0 || value.Fields.Any(f => f is not ("MemoryMiB" or "Width" or "Height")))) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        return Application.Runtime.Definitions.Stage(c, [new(value.Id, new(preflight ? "nexa:launch/preflights" : "nexa:launch/contributions"), Abi100Schemas.LaunchContributionRegistration, LaunchContributionRegistration.Write(value), [])]).Single();
    }
    private async ValueTask<LaunchExtensions> ApplyLaunchExtensions(HostInvocation c, PublicLaunchProfile profile)
    {
        var definitions = Application.Runtime.Definitions.Entries.Where(e => e.Definition.Registry.Value is "nexa:launch/contributions" or "nexa:launch/preflights")
            .OrderBy(e => e.Definition.Registry.Value, StringComparer.Ordinal).ThenBy(e => e.Definition.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        if (definitions.Length > 32) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
        var accepted = ImmutableArray.CreateBuilder<QualifiedId>(); var suppressed = ImmutableArray.CreateBuilder<ValidationIssue>(); var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in definitions)
        {
            var registration = LaunchContributionRegistration.Read(entry.Definition.Body);
            try
            {
                if (entry.Definition.Registry.Value == "nexa:launch/preflights")
                {
                    var report = ValidationReport.Read(await Services.QueryDefinitionCallback(c, entry, "nexa.minecraft.launch.write", registration.Handler, PublicLaunchProfile.Write(profile), Abi100Schemas.ValidationReport).ConfigureAwait(false));
                    if (report.Issues.Length > 128) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                    if (!report.Valid || report.Issues.Any(i => i.Fatal)) throw new AbiHostException(AbiErrors.CHECKUP_FAILED);
                    suppressed.AddRange(report.Issues); accepted.Add(registration.Id); continue;
                }
                if (registration.Fields.Any(fields.Contains)) throw new AbiHostException(AbiErrors.CONFLICT);
                var next = PublicLaunchProfile.Read(await Services.QueryDefinitionCallback(c, entry, "nexa.minecraft.launch.write", registration.Handler, PublicLaunchProfile.Write(profile), Abi100Schemas.LaunchProfile).ConfigureAwait(false));
                var permitted = profile with
                {
                    MemoryMiB = registration.Fields.Contains("MemoryMiB") ? next.MemoryMiB : profile.MemoryMiB,
                    Width = registration.Fields.Contains("Width") ? next.Width : profile.Width,
                    Height = registration.Fields.Contains("Height") ? next.Height : profile.Height
                };
                if (ProjectionRevision(next) != ProjectionRevision(permitted)) throw new AbiHostException(AbiErrors.CAPABILITY_DENIED);
                foreach (var (key, value) in new[] { ("game.memory", next.MemoryMiB), ("game.width", next.Width), ("game.height", next.Height) })
                {
                    var setting = new SettingsOverride(key == "game.memory" && value == 0 ? SettingsOverrideMode.Auto : SettingsOverrideMode.Custom, key == "game.memory" && value == 0 ? null : value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (SettingsPolicySchema.ByKey[key].Validate(setting) is not null) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                }
                foreach (var field in registration.Fields) fields.Add(field);
                profile = next; accepted.Add(registration.Id);
            }
            catch (AbiHostException error) when (!registration.Required)
            { suppressed.Add(Issue(error.Code, "Optional launch extension was suppressed: " + registration.Id.Value, registration.Id.Value, false)); }
        }
        return new(profile, definitions, accepted.ToImmutable(), suppressed.ToImmutable());
    }
    private void ValidateLaunchExtensions(LaunchExtensions extensions)
    {
        var current = Application.Runtime.Definitions.Entries;
        foreach (var entry in extensions.Definitions)
            if (!current.Any(e => e.Owner == entry.Owner && e.Registration == entry.Registration && e.Revision == entry.Revision)) throw new AbiHostException(AbiErrors.PLAN_STALE);
        if (current.Count(e => e.Definition.Registry.Value is "nexa:launch/contributions" or "nexa:launch/preflights") != extensions.Definitions.Length) throw new AbiHostException(AbiErrors.PLAN_STALE);
    }
    private async ValueTask CheckLaunchPreflights(HostInvocation c, LaunchExtensions extensions)
    {
        ValidateLaunchExtensions(extensions);
        foreach (var entry in extensions.Definitions.Where(e => e.Definition.Registry.Value == "nexa:launch/preflights"))
        {
            var registration = LaunchContributionRegistration.Read(entry.Definition.Body);
            try
            {
                var result = ValidationReport.Read(await Services.QueryDefinitionCallback(c, entry, "nexa.minecraft.launch.write", registration.Handler,
                    PublicLaunchProfile.Write(extensions.Profile), Abi100Schemas.ValidationReport).ConfigureAwait(false));
                if (!result.Valid || result.Issues.Any(i => i.Fatal)) throw new AbiHostException(AbiErrors.CHECKUP_FAILED);
            }
            catch (AbiHostException) when (!registration.Required) { }
        }
    }
    private IEnumerable<HostCapabilityRoute> LaunchContributionRoutes => [
        HostRoutes.Typed<LaunchContributionRegistration, RegistrationRef>("Minecraft.Launch", "RegisterContribution", (c, v) => ValueTask.FromResult(RegisterLaunchExtension(c, v, false))),
        HostRoutes.Typed<LaunchContributionRegistration, RegistrationRef>("Minecraft.Launch", "RegisterPreflight", (c, v) => ValueTask.FromResult(RegisterLaunchExtension(c, v, true))),
        HostRoutes.Typed<EntityRequest, LaunchContributionReport>("Minecraft.Launch", "ExplainContribution", (c, v) =>
        {
            var launch = FindLaunch(c, v.Id).Plan;
            return ValueTask.FromResult(new LaunchContributionReport(v.Id, launch.ProviderVersions.Select(p => new QualifiedId(p.Name)).ToImmutableArray(), launch.Suppressed));
        })
    ];
}
