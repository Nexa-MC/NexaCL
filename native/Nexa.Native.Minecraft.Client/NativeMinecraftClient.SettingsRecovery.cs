using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Settings;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private ISettingsRecoveryTransactions? _settingsRecovery;
    private async Task<byte[]> ExecuteSettingsRecoveryAsync(int operation, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out _); using var writer = new ServiceWireWriter();
        var settings = Volatile.Read(ref _settingsRecovery) ?? throw new InvalidOperationException("Canonical recovery settings are not attached.");
        if (operation == MinecraftHostOperations.SettingsRecoveryCapture)
        {
            string instance = reader.String() ?? throw new InvalidDataException("Missing recovery instance."); reader.End();
            var result = await settings.CaptureRecoveryAsync(instance, token).ConfigureAwait(false);
            writer.XsrError(result.Error); if (result.IsSuccess) writer.SettingsRecoverySnapshot(result.Value);
        }
        else if (operation == MinecraftHostOperations.SettingsRecoveryPlan)
        {
            string instance = reader.String() ?? throw new InvalidDataException("Missing recovery instance.");
            var baseline = reader.SettingsRecoverySnapshot() ?? throw new InvalidDataException("Missing recovery baseline."); int count = reader.Count();
            if (count is < 0 or > 1024) throw new InvalidDataException("Recovery setting budget exceeded.");
            string[] keys = new string[count]; for (int index = 0; index < count; index++) keys[index] = reader.String() ?? throw new InvalidDataException("Missing recovery setting key.");
            reader.End(); var result = await settings.PlanRecoveryAsync(instance, baseline, keys, token).ConfigureAwait(false);
            writer.XsrError(result.Error); if (result.IsSuccess) writer.RecoverySettingsPlan(result.Value);
        }
        else
        {
            var plan = reader.RecoverySettingsPlan() ?? throw new InvalidDataException("Missing recovery settings plan."); bool reverse = reader.Boolean(); reader.End();
            writer.XsrError((await settings.ApplyRecoveryAsync(plan, reverse, token).ConfigureAwait(false)).Error);
        }
        return writer.Finish();
    }
}
