using System.Globalization;
using Nexa.Platform;
using Nexa.Services.Foundation;
using Nexa.Services.Memory;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

/// <summary>Composition owns settings and game identity; the service sees explicit delegates.</summary>
public sealed class MemoryOptimizationRuntime : IAsyncDisposable
{
    private readonly MemoryOptimizationService _service;
    public MemoryOptimizationRuntime(FoundationHost host, IProcessMemoryPort? port = null, TimeProvider? clock = null)
    {
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
    public void Start() => _service.Start();
    public ValueTask DisposeAsync() => _service.DisposeAsync();
}
