using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nexa.Native.Ui;

namespace Nexa.Native.Window;

internal static class WindowExports
{
    private static WindowSession? _session;
    private static Task? _retirement;
    private static int _opened;

    [UnmanagedCallersOnly(EntryPoint = "nexa_window_open_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Open(NativeUiCallTable* hostControl, NativeUiCallTable* hostUi,
        NativeUiCallTable* control, NativeUiCallTable* ui)
    {
        if (hostControl == null || hostUi == null || control == null || ui == null) return -1;
        if (Interlocked.CompareExchange(ref _opened, 1, 0) != 0) return -1;
        try
        {
            var session = new WindowSession(new(*hostControl), new(*hostUi));
            _session = session; *control = session.ControlTable; *ui = session.UiTable;
            return 0;
        }
        catch { Volatile.Write(ref _opened, 0); return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_window_run_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Run(long context)
    {
        var session = Volatile.Read(ref _session);
        if (session is null || session.ControlTable.Context != context) return -1;
        try { return session.RunMain(); }
        catch (Exception error) { session.Report(error); return 1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_window_close_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Close(long context)
    {
        var session = Volatile.Read(ref _session);
        if (session is null || session.ControlTable.Context != context) return -1;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? previous = Interlocked.CompareExchange(ref _retirement, completion.Task, null);
        if (previous is null) _ = RetireAsync(session, completion);
        Task retirement = previous ?? completion.Task;
        if (retirement.IsFaulted) return -1;
        if (!retirement.IsCompletedSuccessfully) return 0;
        try { MacPlatformRetirement.Complete(session.HasNativeStartup); return 1; }
        catch (Exception error) { session.Report(error); return -1; }
    }

    private static async Task RetireAsync(WindowSession session, TaskCompletionSource completion)
    {
        try { await session.DisposeAsync().ConfigureAwait(false); completion.TrySetResult(); }
        catch (Exception error) { session.Report(error); completion.TrySetException(error); _ = completion.Task.Exception; }
    }
}
