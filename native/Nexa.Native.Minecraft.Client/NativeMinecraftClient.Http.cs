using Nexa.Native.Minecraft.Http;
using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private NetworkHttpStreamServer? _hostHttp;
    public void AttachHttp(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_hostHttp is not null) throw new InvalidOperationException("Minecraft HTTP is already attached.");
            _hostHttp = new((request, configuration, token) =>
            {
                if (!configuration.IsEmpty) throw new InvalidDataException("Unexpected Minecraft HTTP configuration.");
                return http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
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
