#if NEXA_NATIVE_SERVICES
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexa.Services.Minecraft;
using Nexa.Services.Settings;
using Nexa.Xsr;

namespace Nexa.Services.Foundation;

[JsonSerializable(typeof(MinecraftLibraryConfiguration))]
internal sealed partial class MinecraftLibraryPersistenceJsonContext : JsonSerializerContext;

internal sealed class MinecraftLibraryPersistence(SettingsService settings) : IMinecraftLibraryPersistence
{
    private static string Revision(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public async ValueTask<MinecraftLibraryStoredConfiguration> ReadAsync(CancellationToken token = default)
        => await Task.Run(() => ReadCore(), token).ConfigureAwait(false);
    private MinecraftLibraryStoredConfiguration ReadCore()
    {
        var saved = settings.GetValue<string>(MinecraftLibraryContract.SettingKey);
        if (!saved.IsSuccess) return new(Revision(""), null, saved.Error);
        string raw = saved.Value ?? "";
        try
        {
            if (string.IsNullOrWhiteSpace(raw)) return new(Revision(raw), null);
            var configuration = JsonSerializer.Deserialize(raw, MinecraftLibraryPersistenceJsonContext.Default.MinecraftLibraryConfiguration) ?? throw new JsonException("Null library configuration.");
            return new(Revision(raw), configuration);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        { return new(Revision(raw), null, SettingsErrors.InvalidValue(MinecraftLibraryContract.SettingKey, "the saved library document is invalid or unsupported.")); }
    }
    public async ValueTask<XsrResult<MinecraftLibraryStoredConfiguration>> SaveAsync(MinecraftLibrarySave command, CancellationToken token = default)
        => await Task.Run(() =>
        {
            var current = settings.GetValue<string>(MinecraftLibraryContract.SettingKey);
            if (!current.IsSuccess) return XsrResult.Failure<MinecraftLibraryStoredConfiguration>(current.Error!);
            string raw = current.Value ?? "";
            if (Revision(raw) != command.ExpectedRevision) return XsrResult.Failure<MinecraftLibraryStoredConfiguration>(SettingsErrors.InvalidValue(MinecraftLibraryContract.SettingKey, "the library changed outside this session; reload before saving."));
            string updated = JsonSerializer.Serialize(command.Configuration, MinecraftLibraryPersistenceJsonContext.Default.MinecraftLibraryConfiguration);
            var result = settings.CompareExchangeText(MinecraftLibraryContract.SettingKey, raw, updated);
            return result.IsSuccess ? XsrResult.Success(new MinecraftLibraryStoredConfiguration(Revision(updated), command.Configuration)) : XsrResult.Failure<MinecraftLibraryStoredConfiguration>(result.Error!);
        }, token).ConfigureAwait(false);
}
#endif
