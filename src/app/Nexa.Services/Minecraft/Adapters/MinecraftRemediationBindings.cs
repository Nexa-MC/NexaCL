#if !NEXA_NATIVE_SERVICES
using Nexa.Services.Caching;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Settings;
using Nexa.Xsr.State;

namespace Nexa.Services.Capabilities;

/// <summary>Composition-only, single-assignment bindings to product remediation owners.</summary>
public sealed class MinecraftRemediationBindings(SettingsPolicyService settings, XsrStateStore store)
{
    private readonly MinecraftRemediationService _service = new(settings, store);
    private IMinecraftRemediationPort? _native;
    public ISharedStateCache? SharedStateCache { get => _service.SharedStateCache; init => _service.SharedStateCache = value; }
    public void BindJava(IJavaRuntimeLocator locator, IJavaRuntimeInstaller installer, string runtimeRoot, MinecraftLaunchFileCompletion files)
        => _service.BindJava(locator, installer, runtimeRoot, files);
    public void BindInstall(MinecraftInstallService installer) => _service.BindInstall(installer);
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
        public bool Available => Volatile.Read(ref owner._native) is not null || owner._service.IsAvailable(id);
        public async ValueTask<RemediationResult> ExecuteAsync(RemediationRequest request, CancellationToken cancellationToken)
        {
            try
            {
                return await Task.Run(() => Volatile.Read(ref owner._native) is { } native
                    ? native.RemediateAsync(request, cancellationToken) : owner._service.ExecuteAsync(request, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { return new(Id, false, "repair_failed", error is IOException or InvalidDataException ? error.Message : "修复未完成，请检查任务详情。"); }
        }
    }
}

#endif
