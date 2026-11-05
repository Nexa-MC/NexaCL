using System.Text.Json.Serialization;

namespace Nexa.Services.Accounts;

/// <summary>
/// Source-generated JSON contract for the legacy launch profile file: camelCase property
/// names, indented output, and string enum values.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(LaunchProfile))]
[JsonSerializable(typeof(LaunchProfileSet))]
internal sealed partial class LaunchProfileJsonContext : JsonSerializerContext
{
    internal static LaunchProfileSet NormalizeLoadedProfiles(LaunchProfileSet profiles)
    {
        if (profiles.Profiles is null || profiles.Profiles.Any(profile => profile is null))
            throw new InvalidDataException("The launch profile collection is invalid.");
        // Source-generated JSON construction can assign null to missing init-only
        // members. Schema-1 files from before offline textures have no such fields.
        return profiles with
        {
            Profiles = profiles.Profiles.Select(profile => profile with
            {
                OfflineSkinPng = profile.OfflineSkinPng ?? [],
                OfflineCapePng = profile.OfflineCapePng ?? []
            }).ToArray()
        };
    }
}
