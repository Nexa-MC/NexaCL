using Nexa.Services.Minecraft.Java;

namespace Nexa.Native.Minecraft.Client;

/// <summary>Caller-local Java discovery adapter; all probes execute in the Minecraft owner.</summary>
public sealed class NativeJavaRuntimeLocator(Func<NativeMinecraftClient> client) : IJavaRuntimeLocator
{
    private int _refresh = 1;
    public void Invalidate() => Interlocked.Exchange(ref _refresh, 1);
    public async ValueTask<IReadOnlyList<JavaRuntimeCandidate>> FindAllAsync(CancellationToken cancellationToken = default)
    {
        bool refresh = Interlocked.Exchange(ref _refresh, 0) != 0;
        try { return (await client().JavaInventoryAsync(refresh, cancellationToken).ConfigureAwait(false)).Runtimes; }
        catch { if (refresh) Invalidate(); throw; }
    }
    public ValueTask<JavaRuntimeCandidate?> InspectAsync(string javaExecutablePath, CancellationToken cancellationToken = default) =>
        client().InspectJavaAsync(javaExecutablePath, cancellationToken);
}
