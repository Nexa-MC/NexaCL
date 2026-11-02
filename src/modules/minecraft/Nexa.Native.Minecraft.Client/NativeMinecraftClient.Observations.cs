using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    public static void DeclareObservationReplicas(XsrStateStoreBuilder builder)
    {
        builder.Replica(JvmHostStateContract.ObservationsKey);
        builder.Replica(JvmHostStateContract.SamplesKey);
        builder.Replica(JvmHostStateContract.ContextsKey);
    }
    private byte[] ApplyObservationState(int operation, ServiceWireReader reader, long revision, XsrStateAvailability availability, int count)
    {
        if (operation == MinecraftServiceProtocol.ObservationState)
            Apply<JvmHostObservation, Guid>(JvmHostStateContract.ObservationsKey, () => reader.JvmHostObservation() ?? throw new InvalidDataException("Missing JVM observation."));
        else if (operation == MinecraftServiceProtocol.SampleState)
            Apply<JvmRunSample, string>(JvmHostStateContract.SamplesKey, () => reader.JvmRunSample() ?? throw new InvalidDataException("Missing JVM sample."));
        else Apply<JvmRunContext, Guid>(JvmHostStateContract.ContextsKey, () => reader.JvmRunContext() ?? throw new InvalidDataException("Missing JVM context."));
        return [];
        void Apply<T, TKey>(XsrSemanticId key, Func<T> read) where TKey : notnull
        {
            T[] items = new T[count]; for (int i = 0; i < count; i++) items[i] = read(); reader.End();
            if (_replicas is { } store) store.ReplicateCollection<T, TKey>(store.Resolve(key), revision, availability, items);
        }
    }
}
