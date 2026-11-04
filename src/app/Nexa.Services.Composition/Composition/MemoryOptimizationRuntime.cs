using System.Globalization;
using Nexa.Platform;
using Nexa.Services.Foundation;
using Nexa.Services.Memory;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

/// <summary>Composition owns settings and game identity; the service sees explicit delegates.</summary>
public sealed class MemoryOptimizationRuntime : IAsyncDisposable
{
    private readonly MemoryOptimizationService _service;
    private readonly FoundationHost _host;
    public MemoryOptimizationRuntime(FoundationHost host, IProcessMemoryPort? port = null, TimeProvider? clock = null)
    {
        _host = host;
        _service = new(port ?? new WindowsMemoryElevationPort(), () => new(
            host.Settings.GetRawValue(MemoryOptimizationContract.AutomaticSetting).Value == "true",
            int.TryParse(host.Settings.GetRawValue(MemoryOptimizationContract.ThresholdSetting).Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int threshold) ? Math.Clamp(threshold, 50, 95) : 80,
            host.Settings.GetRawValue(MemoryOptimizationContract.DeepSetting).Value == "true"),
            () => host.StateStore.ReadCollection<MinecraftProcessSnapshot>(
                host.StateStore.Resolve(MinecraftProcessStateComposition.SessionsKey)).Items
                .Where(item => item.State is MinecraftProcessState.Created or MinecraftProcessState.Running)
                .Select(item => item.ProcessId).Where(id => id > 0).ToHashSet(), host.StateStore, clock);
    }
    public void Register(XsrCommandRouterBuilder commands) => commands.Register<MemoryCleanupCommand>(
        MemoryOptimizationContract.Clean, (_, token) => _service.CleanAsync(token));
    public async ValueTask<XsrResult> BeforeLaunchAsync(CancellationToken token)
    {
        if (!_service.IsSupported || _host.Settings.GetRawValue(MemoryOptimizationContract.BeforeLaunchSetting).Value != "true") return XsrResult.Success();
        var result = await _service.CleanBeforeLaunchAsync(token).ConfigureAwait(false);
        if (!result.IsSuccess) _host.Logging.Warn("Memory", "Pre-launch cleanup was skipped: " + result.Error?.Code.Value);
        // Cleanup is optional; denied UAC does not prevent playing. Launch cancellation
        // still propagates through the token and prevents JVM creation.
        return XsrResult.Success();
    }
    public void Start() => _service.Start();
    public ValueTask DisposeAsync() => _service.DisposeAsync();
}
