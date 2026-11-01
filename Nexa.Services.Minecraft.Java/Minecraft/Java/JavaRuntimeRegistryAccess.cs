using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Java;

internal static class JavaRuntimeRegistryAccess
{
    internal static ValueTask<JavaRuntimeRegistrySnapshot> ReadAsync(IAsyncJavaRuntimeRegistrationStore? port,
        IJavaRuntimeRegistrationStore? local, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return port is not null ? port.ReadAsync(token) : ValueTask.FromResult(local?.Read() ?? new JavaRuntimeRegistrySnapshot(0, []));
    }
    internal static ValueTask<XsrResult> WriteAsync(IAsyncJavaRuntimeRegistrationStore? port,
        IJavaRuntimeRegistrationStore local, long revision, IReadOnlyList<JavaRuntimeRegistration> entries, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return port is not null ? port.WriteAsync(revision, entries, token) : ValueTask.FromResult(local.Write(revision, entries));
    }
}
