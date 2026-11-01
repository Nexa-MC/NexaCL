using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Settings;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed partial class RemoteMinecraftSettings
{
    public async ValueTask<XsrResult<SettingsRecoverySnapshot>> CaptureRecoveryAsync(string instanceDirectory, CancellationToken token = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.SettingsRecoveryCapture); request.String(instanceDirectory);
        byte[] bytes = await _endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.SettingsRecoverySnapshot() ?? throw new InvalidDataException("Missing recovery settings snapshot."))
            : XsrResult.Failure<SettingsRecoverySnapshot>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<RecoverySettingsPlan>> PlanRecoveryAsync(string instanceDirectory, SettingsRecoverySnapshot baseline,
        IReadOnlyList<string> selectedKeys, CancellationToken token = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.SettingsRecoveryPlan);
        request.String(instanceDirectory); request.SettingsRecoverySnapshot(baseline); request.Count(selectedKeys.Count);
        foreach (string key in selectedKeys) request.String(key);
        byte[] bytes = await _endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.RecoverySettingsPlan() ?? throw new InvalidDataException("Missing recovery settings plan."))
            : XsrResult.Failure<RecoverySettingsPlan>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult> ApplyRecoveryAsync(RecoverySettingsPlan plan, bool reverse, CancellationToken token = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.SettingsRecoveryApply); request.RecoverySettingsPlan(plan); request.Boolean(reverse);
        byte[] bytes = await _endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
}
