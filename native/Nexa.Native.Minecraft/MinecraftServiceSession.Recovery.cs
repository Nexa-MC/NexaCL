using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private async Task<byte[]> ExecuteRecoveryAsync(int operation, ServiceWireReader reader, CancellationToken token)
    {
        var recovery = new InstanceRecoveryService(_settings, _store) { WorkScheduler = _work };
        using var writer = new ServiceWireWriter();
        switch (operation)
        {
            case MinecraftServiceProtocol.RecoveryRead:
                var query = reader.InstanceRecoveryQuery() ?? throw new InvalidDataException("Missing recovery comparison query."); reader.End();
                var result = await recovery.ReadAsync(query, token).ConfigureAwait(false);
                writer.XsrError(result.Error); if (result.IsSuccess) writer.InstanceRecoveryReport(result.Value); break;
            case MinecraftServiceProtocol.RecoveryRestore:
                var restore = reader.InstanceRecoveryRestoreCommand() ?? throw new InvalidDataException("Missing recovery restore command."); reader.End();
                writer.XsrError((await recovery.RestoreAsync(restore, token).ConfigureAwait(false)).Error); break;
            case MinecraftServiceProtocol.RecoveryResume:
                var resume = reader.InstanceRecoveryResumeCommand() ?? throw new InvalidDataException("Missing recovery replay command."); reader.End();
                writer.XsrError((await recovery.RecoverAsync(resume, token).ConfigureAwait(false)).Error); break;
            default: throw new InvalidDataException("Unknown Minecraft recovery operation.");
        }
        return writer.Finish();
    }
}
