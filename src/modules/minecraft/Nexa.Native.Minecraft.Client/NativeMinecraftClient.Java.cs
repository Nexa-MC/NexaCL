using System.Collections.Concurrent;
using System.Threading.Channels;
using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Java;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient : IConfirmedJavaRuntimeInstaller
{
    public Task<XsrResult> InstallJavaArchiveAsync(JavaArchiveInstallCommand command, CancellationToken token = default)
        => LaunchResultAsync(MinecraftServiceProtocol.JavaArchiveInstall, writer => writer.JavaArchiveInstallCommand(command), token);
    public event Action? RuntimesChanged;
    private readonly ConcurrentDictionary<long, JavaProgressSink> _javaProgress = new();
    private long _nextJava;
    private int _activeJava;
    private readonly TaskCompletionSource _javaDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<string> InstallAsync(string requestedComponent, string runtimeRootDirectory,
        IProgress<JavaRuntimeInstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
        InstallJavaAsync(requestedComponent, runtimeRootDirectory, null, progress, cancellationToken);
    public Task<string> InstallConfirmedAsync(string component, string runtimeRootDirectory, string expectedPlanFingerprint,
        IProgress<JavaRuntimeInstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
        InstallJavaAsync(component, runtimeRootDirectory, expectedPlanFingerprint, progress, cancellationToken);
    private async Task<string> InstallJavaAsync(string component, string root, string? fingerprint,
        IProgress<JavaRuntimeInstallProgress>? progress, CancellationToken token)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_activeJava >= 64) throw new InvalidOperationException("Java install caller budget exceeded.");
            _activeJava++;
        }
        long call = Interlocked.Increment(ref _nextJava); JavaProgressSink? sink = null;
        try
        {
            sink = progress is null ? null : new(progress);
            if (sink is not null) _javaProgress[call] = sink;
            byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.JavaInstall, writer =>
            { writer.Int64(call); writer.String(component); writer.String(root); writer.String(fingerprint); }, token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); string result = reader.String() ?? throw new InvalidDataException("Missing installed Java path."); reader.End();
            RuntimesChanged?.Invoke(); return result;
        }
        finally
        {
            _javaProgress.TryRemove(call, out _);
            try { if (sink is not null) await sink.DisposeAsync().ConfigureAwait(false); }
            finally { lock (_gate) { _activeJava--; if (_closed && _activeJava == 0) _javaDrained.TrySetResult(); } }
        }
    }
    private Task DrainJavaAsync()
    {
        lock (_gate) { if (_activeJava == 0) _javaDrained.TrySetResult(); return _javaDrained.Task; }
    }
    private byte[] ReceiveJavaProgress(ServiceWireReader reader)
    {
        long call = reader.Int64(); var progress = reader.JavaRuntimeInstallProgress() ?? throw new InvalidDataException("Missing Java progress."); reader.End();
        if (_javaProgress.TryGetValue(call, out var sink)) sink.Post(progress); return [];
    }
    public async Task<XsrResult> StopJavaAsync(bool pause, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.JavaStop, writer => writer.Boolean(pause), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async Task<XsrResult> RecoverJavaAsync(string root, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.JavaRecover, writer => writer.String(root), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    private sealed class JavaProgressSink : IAsyncDisposable
    {
        private readonly Channel<JavaRuntimeInstallProgress> _values = Channel.CreateBounded<JavaRuntimeInstallProgress>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        private readonly Task _worker;
        internal JavaProgressSink(IProgress<JavaRuntimeInstallProgress> progress) => _worker = Task.Run(async () =>
        {
            await foreach (var value in _values.Reader.ReadAllAsync().ConfigureAwait(false))
                try { progress.Report(value); } catch (Exception) { }
        });
        internal void Post(JavaRuntimeInstallProgress value) => _values.Writer.TryWrite(value);
        public async ValueTask DisposeAsync() { _values.Writer.TryComplete(); await _worker.ConfigureAwait(false); }
    }
}
