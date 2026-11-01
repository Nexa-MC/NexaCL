using System.Runtime.InteropServices;
using System.Text;
using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Crash;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Minecraft.Client;

/// <summary>Local typed adapters over asynchronous Minecraft service ports.</summary>
public sealed partial class NativeMinecraftClient : IAsyncDisposable
{
    private readonly ServiceEndpoint _endpoint;
    private readonly long _context;
    private readonly nint _close;
    private readonly Task _initialization;
    private readonly object _gate = new();
    private Task? _retirement;
    private bool _closed;
    private readonly XsrStateStore? _replicas;
    private readonly Action<Exception>? _failure;

    public static void DeclareProcessState(XsrStateStoreBuilder builder)
    {
        MinecraftProcessStateComposition.DeclareState(builder);
        builder.Replica(MinecraftProcessStateComposition.SessionsKey);
        builder.Replica(MinecraftProcessStateComposition.FailuresKey);
    }

    public NativeMinecraftClient(string javaRuntimeRoot, string? cacheDirectory = null, string? jvmHostExecutable = null,
        XsrStateStore? replicas = null, Action<Exception>? failure = null)
    {
        _replicas = replicas;
        _failure = failure;
        _endpoint = new(ExecuteReverseAsync, ApplyPublication, Report, RejectReverse);
        try { (_context, _close) = Open(_endpoint); }
        catch { _ = _endpoint.DisposeAsync().AsTask(); throw; }
        _initialization = InitializeAsync(javaRuntimeRoot, cacheDirectory, jvmHostExecutable);
        _ = _initialization.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private void Report(Exception error) { try { _failure?.Invoke(error); } catch { } }
    private byte[] ApplyPublication(ReadOnlyMemory<byte> bytes)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out int operation);
        if (operation == MinecraftServiceProtocol.JavaProgress) return ReceiveJavaProgress(reader);
        long revision = reader.Int64(); var availability = (XsrStateAvailability)reader.Int32(); int count = reader.Count();
        if (revision <= 0 || count < 0 || availability is not (XsrStateAvailability.Available or XsrStateAvailability.Unavailable or XsrStateAvailability.Stale))
            throw new InvalidDataException("Invalid native Minecraft state.");
        if (operation == MinecraftServiceProtocol.TaskState) return ApplyTaskState(reader, revision, count);
        if (operation == MinecraftServiceProtocol.ProcessState)
        {
            MinecraftProcessSnapshot[] items = new MinecraftProcessSnapshot[count];
            for (int index = 0; index < count; index++) items[index] = reader.MinecraftProcessSnapshot() ?? throw new InvalidDataException("Missing process state.");
            reader.End();
            if (_replicas is { } store) store.ReplicateCollection<MinecraftProcessSnapshot, Guid>(store.Resolve(MinecraftProcessStateComposition.SessionsKey), revision, availability, items);
        }
        else if (operation == MinecraftServiceProtocol.FailureState)
        {
            MinecraftProcessFailure[] items = new MinecraftProcessFailure[count];
            for (int index = 0; index < count; index++) items[index] = reader.MinecraftProcessFailure() ?? throw new InvalidDataException("Missing process failure.");
            reader.End();
            if (_replicas is { } store) store.ReplicateCollection<MinecraftProcessFailure, Guid>(store.Resolve(MinecraftProcessStateComposition.FailuresKey), revision, availability, items);
        }
        else throw new InvalidDataException("Unknown native Minecraft state.");
        return [];
    }
    private async Task InitializeAsync(string java, string? cache, string? jvmHost)
    {
        using var writer = MinecraftServiceProtocol.Request(MinecraftServiceProtocol.Initialize);
        writer.String(AppContext.BaseDirectory); writer.String(Path.GetFullPath(java));
        writer.String(cache is null ? null : Path.GetFullPath(cache)); writer.String(jvmHost);
        byte[] bytes = await InvokeFrameAsync(writer.Finish(), CancellationToken.None).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    private async Task<byte[]> InvokeAsync(int operation, Action<ServiceWireWriter> encode, CancellationToken token)
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_closed, this);
        await _initialization.WaitAsync(token).ConfigureAwait(false);
        using var writer = MinecraftServiceProtocol.Request(operation); encode(writer);
        return await InvokeFrameAsync(writer.Finish(), token).ConfigureAwait(false);
    }
    private async Task<byte[]> InvokeFrameAsync(byte[] request, CancellationToken token)
    {
        byte[] bytes = await _endpoint.InvokeAsync(request, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); int status = reader.Int32();
        if (status == 0) { byte[] result = reader.Buffer(); reader.End(); return result; }
        string message = reader.String() ?? "Native Minecraft operation failed."; string? parameter = reader.String(); reader.End();
        throw status switch
        {
            1 => new ArgumentNullException(parameter, message),
            2 => new ArgumentOutOfRangeException(parameter, message),
            3 => new ArgumentException(message, parameter),
            4 => new InvalidDataException(message),
            5 => new UnauthorizedAccessException(message),
            6 => new IOException(message),
            7 => new InvalidOperationException(message),
            8 => new NotSupportedException(message),
            _ => new InvalidDataException("Invalid native Minecraft failure category.")
        };
    }
    public async Task<IReadOnlyList<MinecraftVersionDescriptor>> VersionsAsync(string root, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.Versions, writer => writer.String(root), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); int count = reader.Count();
        if (count < 0) throw new InvalidDataException("Missing Minecraft versions.");
        MinecraftVersionDescriptor[] values = new MinecraftVersionDescriptor[count];
        for (int index = 0; index < count; index++) values[index] = reader.MinecraftVersionDescriptor() ?? throw new InvalidDataException("Missing Minecraft version.");
        reader.End(); return Array.AsReadOnly(values);
    }
    public async Task<IReadOnlyList<MinecraftInstanceDescriptor>> InstancesAsync(string root, bool refresh = false, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(refresh ? MinecraftServiceProtocol.RefreshInstances : MinecraftServiceProtocol.Instances,
            writer => writer.String(root), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); int count = reader.Count();
        if (count < 0) throw new InvalidDataException("Missing Minecraft instances.");
        MinecraftInstanceDescriptor[] values = new MinecraftInstanceDescriptor[count];
        for (int index = 0; index < count; index++) values[index] = reader.MinecraftInstanceDescriptor() ?? throw new InvalidDataException("Missing Minecraft instance.");
        reader.End(); return Array.AsReadOnly(values);
    }
    public async Task<IReadOnlyList<MinecraftProcessSnapshot>> ProcessesAsync(CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.Processes, _ => { }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); int count = reader.Count();
        if (count < 0) throw new InvalidDataException("Missing Minecraft processes.");
        MinecraftProcessSnapshot[] values = new MinecraftProcessSnapshot[count];
        for (int index = 0; index < count; index++) values[index] = reader.MinecraftProcessSnapshot() ?? throw new InvalidDataException("Missing Minecraft process.");
        reader.End(); return Array.AsReadOnly(values);
    }
    public async Task<JavaRuntimeInventorySnapshot> JavaInventoryAsync(bool refresh = false, CancellationToken token = default)
    {
        var result = await ReadJavaInventoryAsync(new(refresh), token).ConfigureAwait(false);
        return result.IsSuccess ? result.Value! : throw new InvalidOperationException(result.Error!.Message);
    }
    public async ValueTask<XsrResult<JavaRuntimeInventorySnapshot>> ReadJavaInventoryAsync(JavaRuntimeInventoryQuery query, CancellationToken token = default)
    {
        await _javaConfiguration.WaitAsync(token).ConfigureAwait(false);
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.JavaInventory, writer => writer.Boolean(query.Refresh), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.JavaRuntimeInventorySnapshot() ?? throw new InvalidDataException("Missing Java inventory."))
            : XsrResult.Failure<JavaRuntimeInventorySnapshot>(error); reader.End(); return result;
    }
    public async Task<MinecraftProcessSnapshot> StartProcessAsync(MinecraftLaunchPlan plan, string instanceId, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.StartProcess,
            writer => { writer.MinecraftLaunchPlan(plan); writer.String(instanceId); }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.MinecraftProcessSnapshot() ?? throw new InvalidDataException("Missing started process."); reader.End(); return result;
    }
    public async Task<XsrResult<MinecraftProcessOutputSnapshot>> ReadOutputAsync(Guid sessionId, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ProcessOutput, writer => writer.Guid(sessionId), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.MinecraftProcessOutputSnapshot() ?? throw new InvalidDataException("Missing process output."))
            : XsrResult.Failure<MinecraftProcessOutputSnapshot>(error); reader.End(); return result;
    }
    public async Task<XsrResult> CancelProcessAsync(Guid sessionId, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.CancelProcess, writer => writer.Guid(sessionId), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async Task<MinecraftLaunchFaultReport> AnalyzeCrashAsync(IReadOnlyList<string> evidence, string? stage = null,
        string? lastClass = null, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.CrashAnalyze, writer =>
        { writer.Count(evidence.Count); foreach (var line in evidence) writer.String(line); writer.String(stage); writer.String(lastClass); }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.MinecraftLaunchFaultReport() ?? throw new InvalidDataException("Missing crash report."); reader.End(); return result;
    }
    private static unsafe (long Context, nint Close) Open(ServiceEndpoint endpoint)
    {
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        nint image = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nexa-minecraft" + suffix));
        var identity = (delegate* unmanaged[Cdecl]<byte*, int, int>)NativeLibrary.GetExport(image, "nexa_minecraft_service_identity_v1");
        var open = (delegate* unmanaged[Cdecl]<NativeUiCallTable*, NativeUiCallTable*, int>)NativeLibrary.GetExport(image, "nexa_minecraft_service_open_v1");
        nint close = NativeLibrary.GetExport(image, "nexa_minecraft_service_close_v1");
        byte[] expected = Encoding.UTF8.GetBytes(MinecraftServiceProtocol.Identity), actual = new byte[expected.Length];
        if (identity(null, 0) != actual.Length) throw new InvalidDataException("Incompatible native Minecraft service.");
        fixed (byte* output = actual)
            if (identity(output, actual.Length) != actual.Length || !actual.AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Incompatible native Minecraft service.");
        var host = endpoint.Table; NativeUiCallTable peer = default;
        if (open(&host, &peer) != 0) throw new InvalidOperationException("Native Minecraft admission failed.");
        endpoint.Connect(peer); return (peer.Context, close);
    }
    private unsafe int Close() => ((delegate* unmanaged[Cdecl]<long, int>)_close)(_context);
    public ValueTask DisposeAsync()
    { lock (_gate) { _closed = true; return new(_retirement ??= RetireAsync()); } }
    private async Task RetireAsync()
    {
        int delay = 1;
        while (true)
        {
            int status = Close(); if (status == 1) break;
            if (status != 0) throw new IOException("Native Minecraft shutdown failed; callbacks remain retained.");
            await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32);
        }
        await DrainJavaAsync().ConfigureAwait(false);
        if (_hostHttp is { } http) await http.DisposeAsync().ConfigureAwait(false);
        if (_hostPublications is { } publications) await publications.DisposeAsync().ConfigureAwait(false);
        await _endpoint.DisposeAsync().ConfigureAwait(false);
        _downloadAdmission.Dispose();
    }
}
