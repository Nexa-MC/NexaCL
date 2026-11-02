using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Java;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private LocalJavaRuntimeLocator? _javaLocator;
    private JavaRuntimeManagementService? _javaManagement;
    private JavaManualDownloadService? _manualJava;
    private JavaRuntimeDiagnosticsService? _javaDiagnostics;
    private string? _javaRuntimeRoot;
    private bool _javaConfigured;

    private async Task<byte[]> ExecuteJavaManagementAsync(int operation, ServiceWireReader reader, CancellationToken token)
    {
        using var writer = new ServiceWireWriter();
        if (operation == MinecraftServiceProtocol.JavaConfigure)
        {
            int count = reader.Count(); if (count is < 1 or > 16) throw new InvalidDataException("Invalid Java managed roots.");
            string[] roots = new string[count];
            for (int index = 0; index < count; index++)
            {
                roots[index] = reader.String() ?? throw new InvalidDataException("Missing Java managed root.");
                if (!Path.IsPathFullyQualified(roots[index]) || roots[index].Length > 4096) throw new InvalidDataException("Invalid Java managed root.");
            }
            reader.End(); await _initialization.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing), this);
                if (_javaConfigured) throw new InvalidOperationException("Java services are already configured.");
                var registry = new RemoteJavaRegistry(_endpoint);
                _javaLocator = new(_javaRuntimeRoot)
                { RegisteredRuntimesAsync = async cancellation => (await registry.ReadAsync(cancellation).ConfigureAwait(false)).Registrations };
                _java = new(_javaLocator, managedRuntimeRoots: roots, registry: null) { RegistrationPort = registry };
                _javaManagement = new(_javaLocator, UnavailableRegistry.Instance, roots) { RegistrationPort = registry };
                _manualJava = new(new HttpJavaRuntimeMetadataProvider(_metadataHttp), _javaInstaller!, _javaLocator,
                    UnavailableRegistry.Instance, _javaRuntimeRoot!, JavaRuntimeInstaller.DetectPlatform())
                { RegistrationPort = registry, Tasks = _tasks };
                _javaInstaller!.RuntimesChanged += _javaLocator.Invalidate;
                _javaDiagnostics = new(_java, new Nexa.Platform.PlatformJavaDiagnostics());
                _javaConfigured = true;
            }
            finally { _initialization.Release(); }
            return writer.Finish();
        }
        if (!_javaConfigured) throw new InvalidOperationException("Canonical Java services are not configured.");
        switch (operation)
        {
            case MinecraftServiceProtocol.JavaInspect:
                string executable = reader.String() ?? throw new InvalidDataException("Missing Java executable."); reader.End();
                writer.JavaRuntimeCandidate(await _javaLocator!.InspectAsync(executable, token).ConfigureAwait(false)); break;
            case MinecraftServiceProtocol.JavaProperties:
            case MinecraftServiceProtocol.JavaModules:
                string diagnosticExecutable = reader.String() ?? throw new InvalidDataException("Missing Java executable.");
                long revision = reader.Int64(); reader.End();
                var diagnostic = operation == MinecraftServiceProtocol.JavaProperties
                    ? await _javaDiagnostics!.ReadPropertiesAsync(new(diagnosticExecutable, revision), token).ConfigureAwait(false)
                    : await _javaDiagnostics!.ReadModulesAsync(new(diagnosticExecutable, revision), token).ConfigureAwait(false);
                writer.XsrError(diagnostic.Error); if (diagnostic.IsSuccess) writer.JavaRuntimeDiagnosticsSnapshot(diagnostic.Value); break;
            case MinecraftServiceProtocol.JavaManage:
                var manage = reader.JavaRuntimeManageCommand() ?? throw new InvalidDataException("Missing Java management command."); reader.End();
                writer.XsrError((await _javaManagement!.ManageAsync(manage, token).ConfigureAwait(false)).Error); break;
            case MinecraftServiceProtocol.JavaManualPreview:
                var preview = reader.JavaManualPreviewQuery() ?? throw new InvalidDataException("Missing Java preview query."); reader.End();
                var previewResult = await _manualJava!.PreviewAsync(preview, token).ConfigureAwait(false);
                writer.XsrError(previewResult.Error); if (previewResult.IsSuccess) writer.JavaManualPreview(previewResult.Value); break;
            case MinecraftServiceProtocol.JavaManualInstall:
                var install = reader.JavaManualInstallCommand() ?? throw new InvalidDataException("Missing Java install command."); reader.End();
                writer.XsrError((await _manualJava!.InstallAsync(install, token).ConfigureAwait(false)).Error); break;
            case MinecraftServiceProtocol.JavaManualCancel:
                var cancel = reader.JavaManualCancelCommand() ?? throw new InvalidDataException("Missing Java cancel command."); reader.End();
                writer.XsrError((await _manualJava!.CancelAsync(cancel, token).ConfigureAwait(false)).Error); break;
            case MinecraftServiceProtocol.JavaManualStatus:
                var status = reader.JavaManualStatusQuery() ?? throw new InvalidDataException("Missing Java status query."); reader.End();
                var statusResult = await _manualJava!.StatusAsync(status, token).ConfigureAwait(false);
                writer.XsrError(statusResult.Error); if (statusResult.IsSuccess) writer.JavaManualReceipt(statusResult.Value); break;
            default: throw new InvalidDataException("Unknown Java management operation.");
        }
        return writer.Finish();
    }
    private sealed class UnavailableRegistry : IJavaRuntimeRegistrationStore
    {
        internal static readonly UnavailableRegistry Instance = new();
        public JavaRuntimeRegistrySnapshot Read() => throw new InvalidOperationException("Native Java requires asynchronous canonical registry access.");
        public XsrResult Write(long expectedRevision, IReadOnlyList<JavaRuntimeRegistration> registrations) =>
            throw new InvalidOperationException("Native Java requires asynchronous canonical registry access.");
    }
}
