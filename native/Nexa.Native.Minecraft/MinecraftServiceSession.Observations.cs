using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private bool PublishObservationState(XsrStateChange change)
    {
        var key = change.SemanticId;
        if (key == JvmHostStateContract.ObservationsKey) PublishCollection<JvmHostObservation>(key, 42, MinecraftServiceProtocol.ObservationState, (writer, value) => writer.JvmHostObservation(value));
        else if (key == JvmHostStateContract.SamplesKey) PublishCollection<JvmRunSample>(key, 43, MinecraftServiceProtocol.SampleState, (writer, value) => writer.JvmRunSample(value));
        else if (key == JvmHostStateContract.ContextsKey) PublishCollection<JvmRunContext>(key, 44, MinecraftServiceProtocol.ContextState, (writer, value) => writer.JvmRunContext(value));
        else return false;
        return true;
    }
    private void PublishCollection<T>(XsrSemanticId key, int slot, int operation, Action<ServiceWireWriter, T> write)
    {
        var captured = _store.ReadCollection<T>(_store.Resolve(key));
        _publications.Post(slot, captured.Revision, () =>
        {
            using var writer = MinecraftServiceProtocol.Request(operation); writer.Int64(captured.Revision);
            writer.Int32((int)captured.Availability); writer.Count(captured.Items.Count);
            foreach (var value in captured.Items) write(writer, value); return writer.Finish();
        });
    }
    private sealed class Observations(MinecraftServiceSession owner) : IResourceObservationSink
    {
        public async ValueTask RecordAsync(ResourceObservationSample sample, CancellationToken cancellationToken = default)
        {
            using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.RecordObservation); request.ResourceObservationSample(sample);
            byte[] bytes = await owner._endpoint.InvokeAsync(request.Finish(), cancellationToken).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); reader.End();
        }
    }
}
