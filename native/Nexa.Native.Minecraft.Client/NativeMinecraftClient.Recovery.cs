using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    public async ValueTask<XsrResult<InstanceRecoveryReport>> ReadRecoveryAsync(InstanceRecoveryQuery query, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.RecoveryRead, writer => writer.InstanceRecoveryQuery(query), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.InstanceRecoveryReport() ?? throw new InvalidDataException("Missing recovery comparison."))
            : XsrResult.Failure<InstanceRecoveryReport>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult> RestoreRecoveryAsync(InstanceRecoveryRestoreCommand command, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.RecoveryRestore, writer => writer.InstanceRecoveryRestoreCommand(command), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> ResumeRecoveryAsync(InstanceRecoveryResumeCommand command, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.RecoveryResume, writer => writer.InstanceRecoveryResumeCommand(command), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
}
