using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>Owns a responsive native dispatcher before slow product composition begins.</summary>
public sealed partial class AvaloniaUiStartupSession : IAsyncDisposable
{
    private static AvaloniaUiStartupSession? _active;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long _started = Stopwatch.GetTimestamp();
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private StartupWindow? _window;
    private bool _presented;
    private bool _nativeShutdownRequested;
    private readonly Queue<string> _pendingProtocols = new(16);
    private Func<string, string> _localize = static text => text;
    private AvaloniaUiRenderingConfiguration _rendering = new(false);
    private AvaloniaUiStartupAppearance _appearance = new();
    public AvaloniaUiStartupAppearance Appearance => Volatile.Read(ref _appearance);
    public bool HardwareAccelerationDisabled => _rendering.HardwareAccelerationDisabled;
    internal AvaloniaUiRenderingConfiguration RenderingConfiguration => _rendering;
    public CancellationToken CancellationToken => _cancellation.Token;
    public Task<int> Completion => _completion.Task;
    public TimeSpan? FirstRenderElapsed { get; private set; }
    public TimeSpan? ShellReadyElapsed { get; private set; }
    public string Stage { get; private set; } = "启动中";
    internal static AvaloniaUiStartupSession? Active => Volatile.Read(ref _active);

    public static Task<AvaloniaUiStartupSession> StartAsync(string[]? args = null,
        Func<string, string>? localize = null, CancellationToken cancellationToken = default)
        => StartAsync(args, disableHardwareAcceleration: false, localize: localize, cancellationToken: cancellationToken);

    public static async Task<AvaloniaUiStartupSession> StartAsync(string[]? args, bool disableHardwareAcceleration,
        Func<string, string>? localize = null, CancellationToken cancellationToken = default)
        => await StartWithAppearanceAsync(args, disableHardwareAcceleration, new(), localize, cancellationToken).ConfigureAwait(false);

    public static async Task<AvaloniaUiStartupSession> StartWithAppearanceAsync(string[]? args, bool disableHardwareAcceleration,
        AvaloniaUiStartupAppearance appearance, Func<string, string>? localize = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        cancellationToken.ThrowIfCancellationRequested();
        MainThreadApplication? mainThread = null;
        if (OperatingSystem.IsMacOS())
        {
            mainThread = Volatile.Read(ref _mainThreadApplication)
                ?? throw new InvalidOperationException("macOS startup requires RunApplicationOnMainThread from the executable entry.");
            if (!mainThread.IsAccepting)
                throw new InvalidOperationException("A main-thread entry admits one native application lifetime.");
        }
        appearance = appearance.Snapshot();
        var session = new AvaloniaUiStartupSession { _rendering = new(disableHardwareAcceleration), _appearance = appearance };
        if (localize is not null) session._localize = localize;
        if (Interlocked.CompareExchange(ref _active, session, null) is not null)
        {
            session._cancellation.Dispose();
            throw new InvalidOperationException("A native startup session is already running.");
        }
        try
        {
            if (mainThread is not null) mainThread.Submit(session, args ?? []);
            else
            {
                var thread = new Thread(() => session.Run(args ?? [])) { Name = "Nexa native dispatcher" };
                if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
        }
        catch (Exception error) { session.FailBeforeNativeStart(error); throw; }
        try { await session._ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
        return session;
    }

    private void FailBeforeNativeStart(Exception error)
    {
        Interlocked.CompareExchange(ref _active, null, this);
        _ready.TrySetException(error); _completion.TrySetException(error);
        // The rejected session is never returned; both terminal tasks must be observed.
        _ = _ready.Task.Exception; _ = _completion.Task.Exception;
        _cancellation.Dispose();
    }

    private void Run(string[] args)
    {
        int result = 1;
        Exception? failure = null;
        try
        {
            result = _rendering.Apply(AppBuilder.Configure<StartupApplication>().UsePlatformDetect())
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            try { _cancellation.Cancel(); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            Interlocked.CompareExchange(ref _active, null, this);
        }
        // Publish completion after cancellation callbacks and native ownership cleanup; a
        // continuation may dispose the CTS immediately after observing this task.
        if (failure is null) { _completion.TrySetResult(result); _ready.TrySetCanceled(); }
        else { _completion.TrySetException(failure); _ready.TrySetException(failure); }
    }

    public void SetLocalizer(Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(localize);
        if (Completion.IsCompleted) return;
        Dispatcher.UIThread.Post(() => { _localize = localize; _window?.Relocalize(localize); });
    }

    public void SetAppearance(AvaloniaUiStartupAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        appearance = appearance.Snapshot();
        if (Completion.IsCompleted) return;
        void Apply()
        {
            if (_presented || Completion.IsCompleted || _appearance == appearance) return;
            Volatile.Write(ref _appearance, appearance);
            _window?.SetAppearance(appearance);
            if (_preparedWindow is not null) _preparedWindow.StartupMotionSuppressed = appearance.ReducedMotion;
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    public void ReportStage(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        Stage = stage;
        if (Completion.IsCompleted) return;
        Dispatcher.UIThread.Post(() => _window?.SetStage(stage, false));
    }

    public void ReportFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Stage = message;
        if (Completion.IsCompleted) return;
        Dispatcher.UIThread.Post(() => _window?.SetStage(message, true));
    }

    /// <summary>The composition root supplies the already-flushed local session log, or null if unavailable.</summary>
    public void SetFailureLog(string? filePath)
    {
        if (filePath is not null && (!Path.IsPathFullyQualified(filePath) || filePath.Any(char.IsControl)))
            throw new ArgumentException("An absolute local log path is required.", nameof(filePath));
        if (Completion.IsCompleted) return;
        Dispatcher.UIThread.Post(() => _window?.SetFailureLog(filePath));
    }

    internal int Present(XsrUiShell shell, AvaloniaUiPlatformActions? actions, Stream? icon)
    {
        if (Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Shell composition must not block the native startup dispatcher.");
        CancellationToken.ThrowIfCancellationRequested();
        // Standalone shell consumers do not own Desktop readiness participants, but still
        // receive the same hidden native/font/raster preparation before their first Show.
        if (_preparedShell != shell || _preparedWindow is null)
            PrepareShellAsync(shell, actions).GetAwaiter().GetResult();
        if (!_shellWarmed) WarmUpShellAsync(shell).GetAwaiter().GetResult();
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (_presented) throw new InvalidOperationException("The startup shell has already been presented.");
            var desktop = _desktop ?? throw new InvalidOperationException("The native lifetime is not ready.");
            if (_preparedShell != shell || _preparedWindow is null || !_shellWarmed)
                throw new InvalidOperationException("The product shell must finish startup preparation before presentation.");
            icon?.Dispose();
            var window = _preparedWindow;
            actions?.CompleteStartup();
            _presented = true;
            AvaloniaUiShellLifetime.PresentPrepared(desktop, window);
            AvaloniaUiShellHost.BindActivation(Application.Current!, actions);
            while (_pendingProtocols.TryDequeue(out string? uri)) actions?.OnProtocolActivated(uri);
            ShellReadyElapsed = Stopwatch.GetElapsedTime(_started);
            _window?.Close();
            _window = null;
        }).GetAwaiter().GetResult();
        return Completion.GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            try { if (!Completion.IsCompleted) RequestShutdown(); }
            finally { await Completion.ConfigureAwait(false); }
        }
        finally { _cancellation.Dispose(); }
    }

    private void RequestShutdown()
    {
        if (Completion.IsCompleted) return;
        try { _cancellation.Cancel(); }
        finally
        {
            // A cancellation before AppBuilder initializes must not create a dispatcher
            // on the bootstrap worker. OnFrameworkInitializationCompleted observes it.
            if (Volatile.Read(ref _desktop) is not null) Dispatcher.UIThread.Post(ShutdownOnNativeThread);
        }
    }

    private void ShutdownOnNativeThread()
    {
        if (_nativeShutdownRequested || Completion.IsCompleted) return;
        if (_window is { } startupWindow) { startupWindow.Close(); return; }
        _nativeShutdownRequested = true;
        DiscardPreparedShell(); _desktop?.Shutdown(0);
    }

    private sealed class StartupApplication : Application
    {
        public StartupApplication() => Name = "NexaCL";

        public override void OnFrameworkInitializationCompleted()
        {
            AvaloniaUiFileActivation.Initialize(this);
            Styles.Add(new FluentTheme());
            var session = Active ?? throw new InvalidOperationException("Missing native startup session.");
            if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                throw new InvalidOperationException("A desktop lifetime is required.");
            Volatile.Write(ref session._desktop, desktop);
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (session.CancellationToken.IsCancellationRequested)
            {
                session._ready.TrySetCanceled(session.CancellationToken);
                Dispatcher.UIThread.Post(session.ShutdownOnNativeThread);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            var window = new StartupWindow(() =>
            {
                session.FirstRenderElapsed ??= Stopwatch.GetElapsedTime(session._started);
                session._ready.TrySetResult();
            }, session._localize, () => session._retryRequested?.TrySetResult(), session._appearance);
            session._window = window;
            desktop.MainWindow = window;
            if (this.TryGetFeature<IActivatableLifetime>() is { } activation)
                activation.Activated += (_, args) =>
                {
                    if (session._presented) return;
                    if (args is ProtocolActivatedEventArgs protocol && session._pendingProtocols.Count < 16)
                        session._pendingProtocols.Enqueue(protocol.Uri.AbsoluteUri);
                    else if (args.Kind == ActivationKind.Reopen) { window.WindowState = WindowState.Normal; window.Activate(); }
                };
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(session._window, window)) session._window = null;
                if (!session._presented)
                {
                    session._cancellation.Cancel();
                    session.ShutdownOnNativeThread();
                }
            };
            window.Show();
            base.OnFrameworkInitializationCompleted();
        }
    }

}
