using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Settings;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed class RemoteMinecraftSettings(ServiceEndpoint endpoint) : ISettingsPolicyReader, ISettingsSnapshotReader
{
    public async ValueTask<CapturedSettingsSnapshot> CaptureAsync(CancellationToken token = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.SettingsSnapshot);
        byte[] bytes = await endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.CapturedSettingsSnapshot() ?? throw new InvalidDataException("Missing canonical settings snapshot.");
        reader.End(); return result;
    }
    public async ValueTask<XsrResult<SettingsEffectiveSnapshot>> ReadAsync(SettingsEffectiveQuery query, CancellationToken token = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.SettingsPolicyRead); request.SettingsEffectiveQuery(query);
        byte[] bytes = await endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.SettingsEffectiveSnapshot() ?? throw new InvalidDataException("Missing canonical settings policy."))
            : XsrResult.Failure<SettingsEffectiveSnapshot>(error); reader.End(); return result;
    }
}
