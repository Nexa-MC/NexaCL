using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Java;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    public async ValueTask<JavaRuntimeCandidate?> InspectJavaAsync(string executable, CancellationToken token = default)
    {
        await _javaConfiguration.WaitAsync(token).ConfigureAwait(false);
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.JavaInspect, writer => writer.String(executable), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var result = reader.JavaRuntimeCandidate(); reader.End(); return result;
    }
    public ValueTask<XsrResult<JavaRuntimeDiagnosticsSnapshot>> ReadJavaPropertiesAsync(JavaRuntimePropertiesQuery query, CancellationToken token = default) =>
        ReadJavaDiagnosticsAsync(MinecraftServiceProtocol.JavaProperties, query.Executable, query.ExpectedRegistryRevision, token);
    public ValueTask<XsrResult<JavaRuntimeDiagnosticsSnapshot>> ReadJavaModulesAsync(JavaRuntimeModulesQuery query, CancellationToken token = default) =>
        ReadJavaDiagnosticsAsync(MinecraftServiceProtocol.JavaModules, query.Executable, query.ExpectedRegistryRevision, token);
    private async ValueTask<XsrResult<JavaRuntimeDiagnosticsSnapshot>> ReadJavaDiagnosticsAsync(int operation, string executable, long revision, CancellationToken token)
    {
        await _javaConfiguration.WaitAsync(token).ConfigureAwait(false);
        byte[] bytes = await InvokeAsync(operation, writer => { writer.String(executable); writer.Int64(revision); }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.JavaRuntimeDiagnosticsSnapshot() ?? throw new InvalidDataException("Missing Java diagnostics."))
            : XsrResult.Failure<JavaRuntimeDiagnosticsSnapshot>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<JavaManualPreview>> PreviewJavaAsync(JavaManualPreviewQuery query, CancellationToken token = default)
    {
        await _javaConfiguration.WaitAsync(token).ConfigureAwait(false);
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.JavaManualPreview, writer => writer.JavaManualPreviewQuery(query), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.JavaManualPreview() ?? throw new InvalidDataException("Missing Java preview.")) : XsrResult.Failure<JavaManualPreview>(error);
        reader.End(); return result;
    }
    public async ValueTask<XsrResult> InstallManualJavaAsync(JavaManualInstallCommand command, CancellationToken token = default)
    {
        await _javaConfiguration.WaitAsync(token).ConfigureAwait(false);
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.JavaManualInstall, writer => writer.JavaManualInstallCommand(command), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> CancelManualJavaAsync(JavaManualCancelCommand command, CancellationToken token = default)
    {
        await _javaConfiguration.WaitAsync(token).ConfigureAwait(false);
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.JavaManualCancel, writer => writer.JavaManualCancelCommand(command), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult<JavaManualReceipt>> JavaManualStatusAsync(JavaManualStatusQuery query, CancellationToken token = default)
    {
        await _javaConfiguration.WaitAsync(token).ConfigureAwait(false);
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.JavaManualStatus, writer => writer.JavaManualStatusQuery(query), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.JavaManualReceipt() ?? throw new InvalidDataException("Missing Java receipt.")) : XsrResult.Failure<JavaManualReceipt>(error);
        reader.End(); return result;
    }
    public async ValueTask<XsrResult> ManageJavaAsync(JavaRuntimeManageCommand command, CancellationToken token = default)
    {
        await _javaConfiguration.WaitAsync(token).ConfigureAwait(false);
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.JavaManage, writer => writer.JavaRuntimeManageCommand(command), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
}
