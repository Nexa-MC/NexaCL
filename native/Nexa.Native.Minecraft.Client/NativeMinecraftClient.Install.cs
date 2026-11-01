using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    public async Task ConfigureInstallationAsync(string countryCode, string? curseForgeApiKey = null, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.InstallConfigure, writer =>
        { writer.String(countryCode); writer.String(curseForgeApiKey); }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    public async Task<XsrResult<MinecraftInstallResult>> InstallMinecraftAsync(MinecraftInstallCommand command, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.InstallRun, writer => writer.MinecraftInstallCommand(command), token).ConfigureAwait(false);
        return ReadInstallation(bytes);
    }
    public async Task<XsrResult<MinecraftInstallResult>> InstallModpackAsync(MinecraftModpackCommand command, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.InstallModpack, writer => writer.MinecraftModpackCommand(command), token).ConfigureAwait(false);
        return ReadInstallation(bytes);
    }
    private static XsrResult<MinecraftInstallResult> ReadInstallation(byte[] bytes)
    {
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.MinecraftInstallResult() ?? throw new InvalidDataException("Missing installed Minecraft identity."))
            : XsrResult.Failure<MinecraftInstallResult>(error); reader.End(); return result;
    }
    public async Task<XsrResult> StopInstallationAsync(bool pause, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.InstallStop, writer => writer.Boolean(pause), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async Task<XsrResult> RecoverInstallationAsync(MinecraftInstallRecoveryCommand command, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.InstallRecover, writer => writer.MinecraftInstallRecoveryCommand(command), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
}
