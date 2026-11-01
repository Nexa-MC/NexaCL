using Nexa.Native.Minecraft.Http;
using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private NetworkHttpStreamServer? _hostHttp;
    public void AttachHttp(HttpClient http)
        => AttachHttpCore(http, null);
    public void AttachHttp(HttpClient http, HttpClient noRedirect)
    {
        ArgumentNullException.ThrowIfNull(noRedirect); AttachHttpCore(http, noRedirect);
    }
    private void AttachHttpCore(HttpClient http, HttpClient? noRedirect)
    {
        ArgumentNullException.ThrowIfNull(http);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_hostHttp is not null) throw new InvalidOperationException("Minecraft HTTP is already attached.");
            _hostHttp = new((request, configuration, token) =>
            {
                bool redirect = true;
                if (!configuration.IsEmpty)
                {
                    using var reader = new ServiceWireReader(configuration); redirect = reader.Boolean(); reader.End();
                }
                var transport = redirect ? http : noRedirect ?? throw new InvalidOperationException("Minecraft no-redirect HTTP is not attached.");
                return transport.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            });
        }
    }
    private Task<byte[]> ExecuteReverseAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out int operation);
        if (operation is >= NetworkHttpWire.Create and <= NetworkHttpWire.Release)
            return ExecuteHttpAsync(operation, bytes, token);
        if (operation is MinecraftHostOperations.DescribeDownload or MinecraftHostOperations.Download)
            return ExecuteDownloadAsync(operation, bytes, token);
        if (operation is MinecraftHostOperations.JavaRegistryRead or MinecraftHostOperations.JavaRegistryWrite)
            return ExecuteJavaRegistryAsync(operation, bytes, token);
        if (operation is MinecraftHostOperations.SettingsSnapshot or MinecraftHostOperations.SettingsPolicyRead or MinecraftHostOperations.SettingsPolicySet
            or MinecraftHostOperations.SettingsRenamePrepare or MinecraftHostOperations.SettingsRenameApply)
            return ExecuteSettingsAsync(operation, bytes, token);
        if (operation is MinecraftHostOperations.SettingsRecoveryCapture or MinecraftHostOperations.SettingsRecoveryPlan or MinecraftHostOperations.SettingsRecoveryApply)
            return ExecuteSettingsRecoveryAsync(operation, bytes, token);
        if (operation is >= DownloadProtocol.AcquireWork and <= DownloadProtocol.ObserveDiskWrite)
            return ExecuteWorkAsync(bytes, token);
        if (operation is >= DownloadProtocol.AcquireQuiet and <= DownloadProtocol.GraceQuiet)
            return ExecuteWorkAsync(bytes, token);
        if (operation is MinecraftHostOperations.AccountProfile or MinecraftHostOperations.AccountIdentity)
            return ExecuteAccountsAsync(operation, bytes, token);
        if (operation is MinecraftHostOperations.WindowProbe or MinecraftHostOperations.PreflightEvaluate or MinecraftHostOperations.RecordObservation)
            return ExecuteLaunchHostAsync(operation, bytes, token);
        throw new InvalidDataException("Unknown Minecraft host operation.");
    }
    private async Task<byte[]> ExecuteHttpAsync(int operation, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out _);
        var http = Volatile.Read(ref _hostHttp) ?? throw new InvalidOperationException("Minecraft host HTTP is not attached.");
        return await http.ExecuteAsync(operation, reader, token).ConfigureAwait(false);
    }
    private void RejectReverse(long id, ReadOnlyMemory<byte> request, ReadOnlyMemory<byte> result)
    {
        _hostWork?.Reject(id, request, result);
        using var reader = MinecraftServiceProtocol.Read(request, out int operation);
        if (operation != NetworkHttpWire.Create) return;
        using var response = new ServiceWireReader(result); long handle = response.Int64(); response.End();
        _ = RejectHttpAsync(handle);
    }
    private async Task RejectHttpAsync(long handle)
    {
        try { if (_hostHttp is { } http) await http.RejectAsync(handle).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { Report(error); }
    }
}
