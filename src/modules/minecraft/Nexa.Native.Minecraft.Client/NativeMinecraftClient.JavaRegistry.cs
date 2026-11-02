using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Java;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private IJavaRuntimeRegistrationStore? _javaRegistry;
    private Task _javaConfiguration = Task.CompletedTask;
    public void AttachJavaRegistry(IJavaRuntimeRegistrationStore registry, IReadOnlyList<string> managedRoots)
    {
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(managedRoots);
        string[] roots = managedRoots.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
        if (roots.Length is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(managedRoots));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_javaRegistry is not null) throw new InvalidOperationException("Java registry is already attached.");
            _javaRegistry = registry;
            _javaConfiguration = ConfigureJavaAsync(roots);
            _ = _javaConfiguration.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    private async Task ConfigureJavaAsync(string[] roots)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.JavaConfigure, writer =>
        { writer.Count(roots.Length); foreach (string root in roots) writer.String(root); }, CancellationToken.None).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    private Task<byte[]> ExecuteJavaRegistryAsync(int operation, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var registry = Volatile.Read(ref _javaRegistry) ?? throw new InvalidOperationException("Canonical Java registry is not attached.");
        using var reader = MinecraftServiceProtocol.Read(bytes, out _); using var writer = new ServiceWireWriter();
        if (operation == MinecraftHostOperations.JavaRegistryRead)
        {
            reader.End(); var snapshot = registry.Read();
            if (snapshot.Registrations.Count > 64) throw new InvalidDataException("Java registration budget exceeded.");
            writer.JavaRuntimeRegistrySnapshot(snapshot);
        }
        else
        {
            long revision = reader.Int64(); int count = reader.Count();
            if (revision < 0 || count is < 0 or > 64) throw new InvalidDataException("Invalid Java registry commit.");
            JavaRuntimeRegistration[] entries = new JavaRuntimeRegistration[count];
            for (int index = 0; index < count; index++) entries[index] = reader.JavaRuntimeRegistration() ?? throw new InvalidDataException("Missing Java registration.");
            reader.End(); writer.XsrError(registry.Write(revision, Array.AsReadOnly(entries)).Error);
        }
        return Task.FromResult(writer.Finish());
    }
}
