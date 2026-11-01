#if NEXA_NATIVE_SERVICES
using Nexa.Services.Caching;
using Nexa.Services.Minecraft.Launch;

namespace Nexa.Services.Capabilities;

public sealed class MinecraftRemediationBindings
{
    public ISharedStateCache? SharedStateCache { get; init; }
    private IMinecraftRemediationPort? _native;
    public void BindNative(IMinecraftRemediationPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (Interlocked.CompareExchange(ref _native, port, null) is not null) throw new InvalidOperationException("Native remediation is already bound.");
    }
    internal IEnumerable<IRemediationHandler> CreateHandlers()
    {
        foreach (string id in new[] { "remediation.java.select", "remediation.java.download", "remediation.java.switch_recommended",
            "remediation.game.repair_files", "remediation.game.repair_version", "remediation.loader.repair" }) yield return new Handler(this, id);
    }
    private sealed class Handler(MinecraftRemediationBindings owner, string id) : IRemediationHandler, IRemediationAvailability
    {
        public string Id => id;
        public bool Available => Volatile.Read(ref owner._native) is not null;
        public async ValueTask<RemediationResult> ExecuteAsync(RemediationRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var target = Volatile.Read(ref owner._native) ?? throw new InvalidOperationException("Native remediation is not bound.");
                return await target.RemediateAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { return new(Id, false, "repair_failed", error is IOException or InvalidDataException ? error.Message : "修复未完成，请检查任务详情。"); }
        }
    }
}
#endif
