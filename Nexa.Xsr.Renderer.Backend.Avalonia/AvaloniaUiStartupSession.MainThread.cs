using System.Runtime.InteropServices;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiStartupSession
{
    private static MainThreadApplication? _mainThreadApplication;
    internal static bool IsOnProcessMainThread => !OperatingSystem.IsMacOS() || PthreadMainThread() == 1;

    /// <summary>
    /// Synchronous executable entry for async GUI bootstrap. Cocoa's native lifetime runs
    /// on the process main thread; bootstrap and readiness work run outside its dispatcher.
    /// Windows and Linux retain their dedicated native dispatcher startup behavior.
    /// </summary>
    public static int RunApplicationOnMainThread(Func<Task<int>> application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!OperatingSystem.IsMacOS()) return application().GetAwaiter().GetResult();
        if (!IsOnProcessMainThread)
            throw new InvalidOperationException("Cocoa application entry must run on the process main thread.");
        var entry = new MainThreadApplication();
        if (Interlocked.CompareExchange(ref _mainThreadApplication, entry, null) is not null)
            throw new InvalidOperationException("A main-thread application entry is already running.");
        try
        {
            Task<int> bootstrap = Task.Run(application);
            Task shutdown = bootstrap.ContinueWith(static (_, state) => ((MainThreadApplication)state!).Shutdown(),
                entry, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _ = Task.WhenAny(entry.Request.Task, bootstrap).GetAwaiter().GetResult();
            if (entry.TakeRequest() is { } request)
            {
                request.Session.Run(request.Args);
            }
            try { return bootstrap.GetAwaiter().GetResult(); }
            finally { shutdown.GetAwaiter().GetResult(); }
        }
        finally
        {
            try { entry.Shutdown(); }
            finally { Interlocked.CompareExchange(ref _mainThreadApplication, null, entry); }
        }
    }

    private sealed record MainThreadRequest(AvaloniaUiStartupSession Session, string[] Args);
    private sealed class MainThreadApplication
    {
        private readonly object _gate = new();
        private bool _closed;
        internal TaskCompletionSource<MainThreadRequest> Request { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool IsAccepting { get { lock (_gate) return !_closed && !Request.Task.IsCompleted; } }
        internal void Submit(AvaloniaUiStartupSession session, string[] args)
        {
            lock (_gate)
            {
                if (_closed || !Request.TrySetResult(new(session, args)))
                    throw new InvalidOperationException("The main-thread application entry no longer admits native startup.");
            }
        }
        internal MainThreadRequest? TakeRequest()
        {
            lock (_gate)
            {
                _closed = true;
                return Request.Task.IsCompletedSuccessfully ? Request.Task.GetAwaiter().GetResult() : null;
            }
        }
        internal void Shutdown()
        {
            MainThreadRequest? request;
            lock (_gate)
            {
                _closed = true;
                request = Request.Task.IsCompletedSuccessfully ? Request.Task.GetAwaiter().GetResult() : null;
            }
            request?.Session.RequestShutdown();
        }
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "pthread_main_np")]
    private static partial int PthreadMainThread();
}
