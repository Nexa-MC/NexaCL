using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Process;

namespace Nexa.Native.Minecraft;

internal sealed class RemoteMinecraftWindow(ServiceEndpoint endpoint, ServiceStatePublisher publications) : IMinecraftWindowProbe
{
    public async ValueTask<MinecraftWindowProbeResult> ProbeAsync(int processId, CancellationToken cancellationToken = default)
    {
        await publications.FlushAsync(cancellationToken).ConfigureAwait(false);
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.WindowProbe); request.Int32(processId);
        byte[] bytes = await endpoint.InvokeAsync(request.Finish(), cancellationToken).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); int value = reader.Int32(); reader.End();
        if (!Enum.IsDefined(typeof(MinecraftWindowProbeResult), value)) throw new InvalidDataException("Invalid native game window observation.");
        return (MinecraftWindowProbeResult)value;
    }
}
