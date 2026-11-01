using Nexa.Services.Updates;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

public static class NexaUpdateRuntimeComposer
{
    private sealed class Observer : IXsrDispatchObserver
    {
        public void OnCompleted(XsrDispatchObservation observation) { }
    }
#if NEXA_NATIVE_SERVICES
    public static XsrQueryRouter Compose(Nexa.Native.Updates.Client.NativeUpdatesClient service)
#else
    public static XsrQueryRouter Compose(NexaUpdateService service)
#endif
    {
        XsrQueryRouterBuilder queries = new();
        queries.Register<NexaUpdateQuery, NexaUpdateStatus>(NexaUpdateContract.Check,
            (query, token) => new(service.CheckAsync(query, token)));
        return queries.Build(new Observer());
    }
}
