using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using PluginSidecar.WasmRuntime;

namespace Nexa.Services.Composition.Plugins;

/// <summary>Captures one authenticated session. Never redirects an interrupted guest to a replacement process.</summary>
public sealed class PluginSidecarExchange(SidecarHostSession session) : IPluginBinaryExchange
{
    public async ValueTask<byte[]> ExchangeAsync(ReadOnlyMemory<byte> request, CancellationToken cancellation)
    {
        var result = await session.SendBinaryCommandAsync(XsrSemanticId.Parse(RemoteWasmPluginMachine.ExecutionContract),
            new(SidecarWireCodecs.GeneratedDto, request.Span), TimeSpan.FromSeconds(30), cancellation).ConfigureAwait(false);
        if (!result.IsSuccess) throw new IOException("plugin.execution.unavailable");
        return result.Value!.ToArray();
    }
}
