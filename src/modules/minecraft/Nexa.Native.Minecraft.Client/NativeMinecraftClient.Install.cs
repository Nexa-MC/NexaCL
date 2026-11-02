using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private Task? _installationConfiguration;
    private string? _installationCountry, _installationApiKey;
    public Task EnsureInstallationConfiguredAsync(string countryCode, string? curseForgeApiKey = null, CancellationToken token = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_installationConfiguration is null)
            {
                _installationCountry = countryCode; _installationApiKey = curseForgeApiKey;
                _installationConfiguration = ConfigureInstallationAsync(countryCode, curseForgeApiKey, CancellationToken.None);
                _ = _installationConfiguration.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else if (_installationCountry != countryCode || _installationApiKey != curseForgeApiKey)
                throw new InvalidOperationException("Minecraft installation configuration changed after admission.");
            return _installationConfiguration.WaitAsync(token);
        }
    }
    public async Task<MinecraftInstallEditSnapshot> ReadInstallationAsync(MinecraftInstallEditQuery query, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.InstallEditRead, writer =>
        { writer.String(query.RootDirectory); writer.String(query.InstanceId); }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var result = reader.MinecraftInstallEditSnapshot() ?? throw new InvalidDataException("Missing Minecraft edit snapshot.");
        reader.End(); return result;
    }
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
