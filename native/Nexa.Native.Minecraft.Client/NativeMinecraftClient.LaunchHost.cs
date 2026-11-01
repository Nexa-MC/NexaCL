using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private IMinecraftWindowProbe? _windowProbe;
    private Func<string, string, MinecraftLaunchPlan, CancellationToken, Task<CapabilityPreflightReport>>? _preflight;
    private Action<int>? _gameWindowAppeared;
    private IResourceObservationSink? _observation;
    public void AttachObservations(IResourceObservationSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_observation is not null) throw new InvalidOperationException("Minecraft observation sink is already attached.");
            _observation = sink;
        }
    }
    public void AttachLaunchHost(IMinecraftWindowProbe windowProbe,
        Func<string, string, MinecraftLaunchPlan, CancellationToken, Task<CapabilityPreflightReport>> preflight)
        => AttachLaunchHost(windowProbe, preflight, null);
    public void AttachLaunchHost(IMinecraftWindowProbe windowProbe,
        Func<string, string, MinecraftLaunchPlan, CancellationToken, Task<CapabilityPreflightReport>> preflight,
        Action<int>? gameWindowAppeared)
    {
        ArgumentNullException.ThrowIfNull(windowProbe); ArgumentNullException.ThrowIfNull(preflight);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_windowProbe is not null) throw new InvalidOperationException("Minecraft launch host is already attached.");
            _windowProbe = windowProbe; _preflight = preflight; _gameWindowAppeared = gameWindowAppeared;
        }
    }
    private async Task<byte[]> ExecuteLaunchHostAsync(int operation, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out _); using var writer = new ServiceWireWriter();
        if (operation == MinecraftHostOperations.RecordObservation)
        {
            var sample = reader.ResourceObservationSample() ?? throw new InvalidDataException("Missing JVM observation sample."); reader.End();
            var sink = Volatile.Read(ref _observation) ?? throw new InvalidOperationException("JVM observation sink is not attached.");
            await sink.RecordAsync(sample, token).ConfigureAwait(false);
        }
        else if (operation == MinecraftHostOperations.WindowProbe)
        {
            int pid = reader.Int32(); reader.End();
            var probe = Volatile.Read(ref _windowProbe) ?? throw new InvalidOperationException("Game window probe is not attached.");
            var result = await probe.ProbeAsync(pid, token).ConfigureAwait(false);
            if (result == MinecraftWindowProbeResult.Visible)
            {
                try { _gameWindowAppeared?.Invoke(pid); }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { Report(error); }
            }
            writer.Int32((int)result);
        }
        else
        {
            string root = reader.String() ?? "", instance = reader.String() ?? "";
            var plan = reader.MinecraftLaunchPlan() ?? throw new InvalidDataException("Missing preflight launch plan."); reader.End();
            var evaluate = Volatile.Read(ref _preflight) ?? throw new InvalidOperationException("Launch preflight is not attached.");
            writer.CapabilityPreflightReport(await evaluate(root, instance, plan, token).ConfigureAwait(false));
        }
        return writer.Finish();
    }
}
