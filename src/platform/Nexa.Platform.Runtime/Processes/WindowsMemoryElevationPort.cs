using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Nexa.Services.Memory;
using Nexa.Xsr.State;

namespace Nexa.Platform;

/// <summary>One cleanup per UAC-authorized helper. The pipe accepts bounded PID exclusions only.</summary>
public sealed class WindowsMemoryElevationPort : IElevatedMemoryCleanupPort
{
    public const string WorkerArgument = "--memory-clean-worker";
    private readonly WindowsProcessMemoryPort _ordinary = new();
    public bool IsSupported => _ordinary.IsSupported;
    public SystemMemorySample Sample() => _ordinary.Sample();
    public IReadOnlyList<MemoryProcess> CaptureProcesses() => _ordinary.CaptureProcesses();
    public bool TryTrim(MemoryProcess process, out long workingSetReductionBytes) => _ordinary.TryTrim(process, out workingSetReductionBytes);

    public async Task<MemoryCleanupResult> CleanElevatedAsync(IReadOnlySet<int> protectedIds, CancellationToken token)
    {
        try { return await CleanElevatedCoreAsync(protectedIds, token).ConfigureAwait(false); }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
        {
            token.ThrowIfCancellationRequested();
            throw new IOException("Elevated memory cleanup timed out.", error);
        }
    }

    private async Task<MemoryCleanupResult> CleanElevatedCoreAsync(IReadOnlySet<int> protectedIds, CancellationToken token)
    {
        if (!IsSupported) throw new PlatformNotSupportedException();
        if (protectedIds.Count > 4096) throw new IOException("Game protection list exceeds its budget.");
        token.ThrowIfCancellationRequested();
        string pipeName = "nexa-memory-" + Guid.NewGuid().ToString("N");
        using var pipe = CreateServerPipe(pipeName);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var start = CreateHelperStart(pipeName);
        // Canceling/closing the parent pipe also cancels the helper's running cleanup.
        using var closeOnCancellation = deadline.Token.Register(pipe.Dispose);
        using var helper = await StartHelperAsync(start, deadline.Token).ConfigureAwait(false);
        await pipe.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint client) || client != helper.Id)
            throw new IOException("The elevated memory helper identity does not match.");
        byte[] request = new byte[12 + protectedIds.Count * 4];
        BinaryPrimitives.WriteInt32LittleEndian(request, 1);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(4), Environment.ProcessId);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(8), protectedIds.Count);
        int offset = 12;
        foreach (int id in protectedIds) { BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(offset), id); offset += 4; }
        await pipe.WriteAsync(request, deadline.Token).ConfigureAwait(false);
        byte[] response = new byte[36];
        await pipe.ReadExactlyAsync(response, deadline.Token).ConfigureAwait(false);
        if (BinaryPrimitives.ReadInt32LittleEndian(response) != 1 || BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(4)) != 0)
            throw new IOException("Elevated memory cleanup was not completed.");
        var result = new MemoryCleanupResult(BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(8)),
            BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(12)), BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(16)),
            BinaryPrimitives.ReadInt64LittleEndian(response.AsSpan(20)), BinaryPrimitives.ReadInt64LittleEndian(response.AsSpan(28)));
        if (result.Cleaned < 0 || result.Skipped < 0 || result.Failed < 0
            || (long)result.Cleaned + result.Skipped + result.Failed > 4096 || result.WorkingSetReductionBytes < 0)
            throw new IOException("Invalid elevated cleanup result.");
        await helper.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        if (helper.ExitCode != 0) throw new IOException("The elevated memory helper failed.");
        return result;
    }

    private static async Task<Process> StartHelperAsync(ProcessStartInfo start, CancellationToken token)
    {
        // ShellExecute's UAC dialog has no cancellation API. Do not block launcher shutdown on it.
        var launch = Task.Run(() => Process.Start(start), token);
        try { return await launch.WaitAsync(token).ConfigureAwait(false) ?? throw new IOException("The memory cleanup helper did not start."); }
        catch
        {
            _ = launch.ContinueWith(completed =>
            {
                if (completed.IsCompletedSuccessfully) completed.Result?.Dispose();
                else _ = completed.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            // A helper accepted after cancellation finds no parent pipe and cannot perform cleanup.
            throw;
        }
    }

    internal static NamedPipeServerStream CreateServerPipe(string pipeName)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new IOException("Current Windows identity is unavailable.");
        PipeSecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        // A standard account can supply a different administrator's credentials at UAC.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security, HandleInheritability.None, 0);
    }

    private static ProcessStartInfo CreateHelperStart(string pipeName)
    {
        string executable = Environment.ProcessPath ?? throw new IOException("Launcher executable is unavailable.");
        ProcessStartInfo start = new(executable) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
        // Managed development can run under dotnet; packaged NativeAOT uses the Bootstrap EXE.
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,
                (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? throw new IOException("Managed entry is unavailable.")) + ".dll"));
        start.ArgumentList.Add(WorkerArgument); start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        return start;
    }

    public static async Task<int> RunWorkerAsync(string pipeName, string parentArgument)
    {
        if (!OperatingSystem.IsWindows() || !IsUserAnAdmin() || !pipeName.StartsWith("nexa-memory-", StringComparison.Ordinal)
            || !Guid.TryParseExact(pipeName[12..], "N", out _) || !int.TryParse(parentArgument, NumberStyles.None,
                CultureInfo.InvariantCulture, out int parent) || parent <= 0) return 2;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(30000, deadline.Token).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server) || server != parent) return 2;
            byte[] header = new byte[12];
            await pipe.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
            int count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
            if (BinaryPrimitives.ReadInt32LittleEndian(header) != 1 || BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4)) != parent
                || count is < 0 or > 4096) return 2;
            byte[] payload = new byte[count * 4];
            await pipe.ReadExactlyAsync(payload, deadline.Token).ConfigureAwait(false);
            HashSet<int> exclusions = [parent];
            for (int offset = 0; offset < payload.Length; offset += 4)
            {
                int id = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
                if (id <= 0) return 2;
                exclusions.Add(id);
            }
            // Parent disconnect must stop further process operations, including parent exit.
            var disconnect = WatchDisconnectAsync(pipe, deadline);
            try
            {
                XsrStateStoreBuilder builder = new(); MemoryOptimizationContract.DeclareState(builder);
                var store = builder.Build();
                await using var service = new MemoryOptimizationService(new WindowsProcessMemoryPort(),
                    () => new(false, 80), () => exclusions, store);
                var outcome = await service.CleanAsync(deadline.Token).ConfigureAwait(false);
                var result = store.Read<MemoryOptimizationSnapshot>(store.Resolve(MemoryOptimizationContract.Snapshot)).Value?.LastResult;
                byte[] response = new byte[36];
                BinaryPrimitives.WriteInt32LittleEndian(response, 1);
                BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(4), outcome.IsSuccess && result is not null ? 0 : 1);
                if (result is not null)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(8), result.Cleaned);
                    BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(12), result.Skipped);
                    BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(16), result.Failed);
                    BinaryPrimitives.WriteInt64LittleEndian(response.AsSpan(20), result.WorkingSetReductionBytes);
                    BinaryPrimitives.WriteInt64LittleEndian(response.AsSpan(28), result.AvailableChangeBytes);
                }
                await pipe.WriteAsync(response, deadline.Token).ConfigureAwait(false);
                return outcome.IsSuccess ? 0 : 1;
            }
            finally
            {
                await deadline.CancelAsync().ConfigureAwait(false);
                await disconnect.ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        { return 1; }
    }

    private static async Task WatchDisconnectAsync(Stream pipe, CancellationTokenSource deadline)
    {
        try { byte[] unexpected = new byte[1]; _ = await pipe.ReadAsync(unexpected, deadline.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or OperationCanceledException) { }
        finally { await deadline.CancelAsync().ConfigureAwait(false); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsUserAnAdmin();
}
