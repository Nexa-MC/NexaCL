using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Wire;

namespace Nexa.Xsr.Renderer.Backend.Native;

/// <summary>Owns the Host side of one native window runtime. No native image is unloaded.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The main-thread entry owns deterministic native retirement and local cleanup; callers cannot dispose a running application session.")]
public sealed partial class NativeWindowSession
{
    private static readonly object Admission = new();
    private static NativeWindowSession? _active;
    private readonly Func<Task<int>> _application;
    private readonly NativeUiCallRegistration _hostControl, _hostUi;
    private readonly NativeUiCallClient _control, _ui;
    private readonly NativeUiAsyncClient _operations;
    private readonly NativeUiSynchronizationContext _context;
    private readonly CancellationTokenSource _closed = new();
    private readonly TaskCompletionSource<int> _applicationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _uiStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly nint _run, _close;
    private readonly long _nativeContext;
    private IXsrUiBackendShell? _shell;
    private XsrUiShellEndpoint? _shellEndpoint;
    private XsrUiRendererEndpoint? _input;
    private int _begun, _disposed, _uiRetired, _changingShell;
    private ExceptionDispatchInfo? _applicationFailure;

    public static NativeWindowSession Current => Volatile.Read(ref _active)
        ?? throw new InvalidOperationException("The native application has not entered its main-thread boundary.");
    public CancellationToken CancellationToken => _closed.Token;
    public Task Completion => _uiStopped.Task;
    public Func<string, string>? Localize { get; set; }
    public Func<string, string>? LocalizePicker { get; set; }
    public event Action<string, int>? Diagnostic;

    private unsafe NativeWindowSession(Func<Task<int>> application, string libraryPath)
    {
        _application = application;
        // Resolve all exports before admitting callbacks. Images stay rooted by the OS loader.
        nint library = NativeLibrary.Load(Path.GetFullPath(libraryPath));
        var open = (delegate* unmanaged[Cdecl]<NativeUiCallTable*, NativeUiCallTable*, NativeUiCallTable*, NativeUiCallTable*, int>)
            NativeLibrary.GetExport(library, "nexa_window_open_v1");
        _run = NativeLibrary.GetExport(library, "nexa_window_run_v1");
        _close = NativeLibrary.GetExport(library, "nexa_window_close_v1");
        var identity = (delegate* unmanaged[Cdecl]<byte*, int, int>)NativeLibrary.GetExport(library, "nexa_window_identity_v1");
        byte[] expected = System.Text.Encoding.UTF8.GetBytes(WindowControlProtocol.Identity);
        if (identity(null, 0) != expected.Length) throw new InvalidDataException("Incompatible native window identity.");
        byte[] actual = new byte[expected.Length];
        fixed (byte* target = actual)
            if (identity(target, actual.Length) != actual.Length || !actual.AsSpan().SequenceEqual(expected))
                throw new InvalidDataException("Incompatible native window wire schema.");
        _hostControl = new(ReceiveControl, Report);
        _hostUi = new(ReceiveUi, Report);
        var control = _hostControl.Table; var ui = _hostUi.Table;
        NativeUiCallTable nativeControl = default, nativeUi = default;
        if (open(&control, &ui, &nativeControl, &nativeUi) != 0)
        {
            _hostUi.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _hostControl.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _closed.Dispose();
            throw new InvalidOperationException("The native window rejected application admission.");
        }
        _control = new(nativeControl); _ui = new(nativeUi); _nativeContext = nativeControl.Context;
        _operations = new(message => _control.Exchange(WindowControlProtocol.Prefix(0, message)));
        _context = new(id =>
        {
            byte[] message = new byte[9]; message[0] = (byte)WindowUiCommand.Continuation;
            BinaryPrimitives.WriteInt64LittleEndian(message.AsSpan(1), id); _ui.Exchange(message);
        }, Report);
        _context.OperationStarted();
    }

    public static unsafe int RunApplicationOnMainThread(Func<Task<int>> application, string? libraryPath = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        libraryPath ??= Path.Combine(AppContext.BaseDirectory, "nexa-window" +
            (OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so"));
        NativeWindowSession session;
        lock (Admission)
        {
            if (_active is not null) throw new InvalidOperationException("A native window session is already running.");
            session = new(application, libraryPath); Volatile.Write(ref _active, session);
        }
        try
        {
            int nativeResult = ((delegate* unmanaged[Cdecl]<long, int>)session._run)(session._nativeContext);
            session.RetireOnMainThread();
            if (Volatile.Read(ref session._begun) == 0) throw new InvalidOperationException("The native module did not enter the application.");
            int result = session._applicationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            session._applicationFailure?.Throw();
            return nativeResult == 0 ? result : nativeResult;
        }
        finally
        {
            try
            {
                session.RetireOnMainThread();
                session.DisposeLocalAsync().GetAwaiter().GetResult();
            }
            finally { Interlocked.CompareExchange(ref _active, null, session); }
        }
    }

    public async Task StartAsync(string[] args, bool disableHardwareAcceleration,
        AvaloniaUiStartupAppearance appearance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args); ArgumentNullException.ThrowIfNull(appearance);
        using var writer = WindowControlProtocol.Write((int)WindowCommand.Start);
        writer.Count(args.Length); foreach (string argument in args) writer.String(argument);
        writer.Boolean(disableHardwareAcceleration); writer.Buffer(WindowValueChannel.EncodeStartup(appearance));
        await _operations.InvokeAsync(writer.Finish(), cancellationToken).ConfigureAwait(false);
    }

    public async Task PrepareShellAsync(IXsrUiBackendShell shell, AvaloniaUiDesktopPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shell);
        if (Interlocked.CompareExchange(ref _changingShell, 1, 0) != 0)
            throw new InvalidOperationException("A shell ownership change is already running.");
        try
        {
            if (_shell is not null && !ReferenceEquals(_shell, shell))
                throw new InvalidOperationException("Discard the prepared shell before replacing it.");
            if (_shell is null)
            {
                _shell = shell; _shellEndpoint = new(shell); _input = new(shell.Renderer);
                _shellEndpoint.Invalidated += OnInvalidated;
            }
            using var writer = WindowControlProtocol.Write((int)WindowCommand.PrepareShell);
            writer.Buffer(WindowValueChannel.EncodeDesktopPolicy(policy ?? new()));
            await _operations.InvokeAsync(writer.Finish(), cancellationToken).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref _changingShell, 0); }
    }

    public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        => _context.RunAsync(action, cancellationToken);
    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        => InvokeAsync(() => { action(); return Task.CompletedTask; }, cancellationToken);
    public Task WarmUpShellAsync(CancellationToken cancellationToken = default)
        => InvokeOperation(WindowCommand.WarmShell, cancellationToken);
    public async Task<int> PresentAsync(CancellationToken cancellationToken = default)
        => BinaryPrimitives.ReadInt32LittleEndian(await InvokeOperation(WindowCommand.Present, cancellationToken).ConfigureAwait(false));
    public void ReportStage(string stage) => SendControl(WindowCommand.ReportStage, writer => writer.String(stage));
    public void ReportFailure(string message) => SendControl(WindowCommand.ReportFailure, writer => writer.String(message));
    public void SetFailureLog(string? path) => SendControl(WindowCommand.FailureLog, writer => writer.String(path));
    public void RequestClose() => SendControl(WindowCommand.CloseWindow);
    public AvaloniaUiRuntimeCapture Capture() => WindowValueChannel.DecodeRuntimeCapture(SendControl(WindowCommand.Metrics));

    public async Task DiscardPreparedShellAsync()
    {
        if (Interlocked.CompareExchange(ref _changingShell, 1, 0) != 0)
            throw new InvalidOperationException("A shell ownership change is already running.");
        try { await InvokeOperation(WindowCommand.DiscardShell).ConfigureAwait(false); DetachShell(); }
        finally { Volatile.Write(ref _changingShell, 0); }
    }

    private Task<byte[]> InvokeOperation(WindowCommand command, CancellationToken token = default)
    { using var writer = WindowControlProtocol.Write((int)command); return _operations.InvokeAsync(writer.Finish(), token); }
    private byte[] SendControl(WindowCommand command, Action<UiWireWriter>? arguments = null)
    {
        using var writer = WindowControlProtocol.Write((int)command); arguments?.Invoke(writer);
        return _control.Exchange(WindowControlProtocol.Prefix(1, writer.Finish()));
    }

    private byte[] ReceiveControl(ReadOnlyMemory<byte> message)
    {
        using var reader = WindowControlProtocol.Read(message, out int kind, callback: true);
        switch ((WindowControlCallback)kind)
        {
            case WindowControlCallback.BeginMain:
                reader.End();
                if (Interlocked.Exchange(ref _begun, 1) != 0) throw new InvalidOperationException("Duplicate application entry.");
                _ = Task.Run(BootstrapAsync); return [];
            case WindowControlCallback.AsyncCompletion:
                byte[] completion = reader.Buffer(); reader.End(); return _operations.Complete(completion);
            case WindowControlCallback.UiStopped:
                reader.End(); _ = RetireUiAsync(); return [];
            case WindowControlCallback.Localize:
            case WindowControlCallback.LocalizePicker:
                string text = reader.String() ?? throw new InvalidDataException("Missing localization input."); reader.End();
                using (var writer = WindowControlProtocol.Write(kind, callback: true))
                {
                    writer.String(((WindowControlCallback)kind == WindowControlCallback.Localize ? Localize : LocalizePicker)?.Invoke(text) ?? text);
                    return writer.Finish();
                }
            case WindowControlCallback.Diagnostic:
                string type = reader.String() ?? "NativeWindow"; int code = reader.Int32(); reader.End();
                Diagnostic?.Invoke(type, code); return [];
            case WindowControlCallback.PlatformEvent:
                return ReceivePlatformEvent(reader, worker: true);
            case WindowControlCallback.Media:
                return ReceiveMedia(reader);
            case WindowControlCallback.MediaControls:
                return ReceiveMediaControls(reader);
            default: throw new InvalidDataException("Unknown native window control callback.");
        }
    }

    private async Task BootstrapAsync()
    {
        int result = 1;
        try { result = await _application().ConfigureAwait(false); }
        catch (Exception error) { _applicationFailure = ExceptionDispatchInfo.Capture(error); Report(error); }
        finally
        {
            try { SendControl(WindowCommand.MainCompleted, writer => writer.Int32(result)); }
            catch (Exception error) { Report(error); result = 1; }
            _applicationCompleted.TrySetResult(result);
        }
    }

    private byte[] ReceiveUi(ReadOnlyMemory<byte> message)
    {
        if (message.IsEmpty) throw new InvalidDataException("Missing native UI callback.");
        if (message.Span[0] == (byte)WindowUiCallback.Continuation)
        {
            if (message.Length != 9) throw new InvalidDataException("Invalid native continuation.");
            _context.Execute(BinaryPrimitives.ReadInt64LittleEndian(message.Span[1..])); return [];
        }
        return _context.ExecuteInline(() =>
        {
            switch ((WindowUiCallback)message.Span[0])
            {
                case WindowUiCallback.Shell:
                    return (_shellEndpoint ?? throw new InvalidOperationException("The Host shell is not prepared.")).Exchange(message[1..]);
                case WindowUiCallback.Input:
                    return (_input ?? throw new InvalidOperationException("The Host input endpoint is not prepared.")).Exchange(message[1..]);
                case WindowUiCallback.InstallText:
                    if (message.Length != 2 || message.Span[1] > 1) throw new InvalidDataException("Invalid native text provider.");
                    if (_shell is not XsrUiShell shell) throw new InvalidOperationException("The Host shell cannot install text layout.");
                    shell.Renderer.TextLayoutProvider = message.Span[1] == 0 ? null
                        : new XsrUiRemoteTextLayoutProvider(new TextTransport(_ui));
                    return [];
                case WindowUiCallback.PlatformEvent:
                    using (var reader = WindowControlProtocol.Read(message[1..], out int kind, callback: true))
                    {
                        if (kind != (int)WindowControlCallback.PlatformEvent) throw new InvalidDataException("Invalid platform event envelope.");
                        return ReceivePlatformEvent(reader, worker: false);
                    }
                case WindowUiCallback.InputLease:
                    return ReceiveInput(message[1..]);
                case WindowUiCallback.Notification:
                    return ReceiveNotification(message[1..]);
                default: throw new InvalidDataException("Unknown native UI callback.");
            }
        });
    }

    private async Task RetireUiAsync()
    {
        try
        {
            Volatile.Write(ref _uiRetired, 1);
            await _hostUi.DisposeAsync().ConfigureAwait(false);
            _context.UseCleanupDispatcher(id => ThreadPool.QueueUserWorkItem(_ => _context.Execute(id)));
            _closed.Cancel(); _uiStopped.TrySetResult();
        }
        catch (Exception error) { Report(error); _uiStopped.TrySetException(error); }
    }

    private void DetachShell()
    {
        if (_shellEndpoint is { } endpoint) endpoint.Invalidated -= OnInvalidated;
        _shellEndpoint?.Dispose(); _shellEndpoint = null; _input = null; _shell = null;
    }
    private void Report(Exception error)
    { try { Diagnostic?.Invoke(error.GetType().FullName ?? "HostWindow", error.HResult); } catch { } }
    private unsafe int CloseNative() => ((delegate* unmanaged[Cdecl]<long, int>)_close)(_nativeContext);

    private void RetireOnMainThread()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Write(ref _nativeRetiring, 1);
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            int state = CloseNative();
            if (state < 0) throw new InvalidOperationException("Native window retirement failed.");
            if (state == 1) break;
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Native window retirement exceeded its budget.");
            Thread.Sleep(10);
        }
    }

    private async Task DisposeLocalAsync()
    {
        await _uiStopped.Task.ConfigureAwait(false);
        await _inputCallbacks.DisposeAsync().ConfigureAwait(false);
        await _notificationCallbacks.DisposeAsync().ConfigureAwait(false);
        Interlocked.Exchange(ref _fileActivation, null);
        foreach (var controls in _mediaControls.Values) await controls.DisposeAsync().ConfigureAwait(false);
        foreach (var player in _media.Values) await player.DisposeAsync().ConfigureAwait(false);
        foreach (var effect in _effects.Values) await effect.DisposeAsync().ConfigureAwait(false);
        foreach (var theme in _themes.Values) await theme.DisposeAsync().ConfigureAwait(false);
        DetachShell(); _operations.Dispose();
        _context.OperationCompleted(); await _context.DisposeAsync().ConfigureAwait(false);
        await _hostControl.DisposeAsync().ConfigureAwait(false);
        _closed.Dispose();
    }

    private sealed class TextTransport(NativeUiCallClient ui) : IXsrUiWireTransport
    {
        public byte[] Exchange(ReadOnlyMemory<byte> request)
            => ui.Exchange(WindowControlProtocol.Prefix((byte)WindowUiCommand.Text, request));
    }
}
