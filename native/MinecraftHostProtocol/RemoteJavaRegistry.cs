using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Java;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed class RemoteJavaRegistry(ServiceEndpoint endpoint) : IAsyncJavaRuntimeRegistrationStore
{
    public async ValueTask<JavaRuntimeRegistrySnapshot> ReadAsync(CancellationToken token = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.JavaRegistryRead);
        byte[] bytes = await endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.JavaRuntimeRegistrySnapshot() ?? throw new InvalidDataException("Missing Java registrations."); reader.End();
        Validate(result.Registrations); return result;
    }
    public async ValueTask<XsrResult> WriteAsync(long expectedRevision, IReadOnlyList<JavaRuntimeRegistration> registrations, CancellationToken token = default)
    {
        Validate(registrations);
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.JavaRegistryWrite);
        request.Int64(expectedRevision); request.Count(registrations.Count);
        foreach (var entry in registrations) request.JavaRuntimeRegistration(entry);
        byte[] bytes = await endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    private static void Validate(IReadOnlyList<JavaRuntimeRegistration> entries)
    {
        if (entries.Count > 64 || entries.Any(entry => !Path.IsPathFullyQualified(entry.Executable)
            || entry.Executable.Length > 4096 || entry.Executable.Any(char.IsControl)))
            throw new InvalidDataException("Invalid Java registrations.");
    }
}
