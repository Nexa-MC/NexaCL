using System.Buffers.Binary;
using Avalonia.Threading;
using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Wire;

namespace Nexa.Native.Window;

/// <summary>Owns the actual native application; Host state remains behind the UI call table.</summary>
internal sealed partial class WindowSession : IAsyncDisposable
{
    private readonly NativeUiCallClient _hostControl, _hostUi;
    private readonly NativeUiCallRegistration _control, _ui;
    private readonly NativeUiAsyncServer _operations;
    private readonly AvaloniaUiPlatformActions _actions = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<int> _main = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<AvaloniaUiStartupSession> _created = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _startupGate = new();
    private AvaloniaUiStartupSession? _startup;
    private XsrUiRemoteShell? _shell;
    private NativeUiSynchronizationContext? _nativeContext;
    private XsrUiTextLayoutEndpoint? _text;
    private Task? _uiStopped;
    private Task? _quiesced;
    private bool _startRequested, _quiescing;
    private int _runEntered, _invalidations, _invalidationQueued;

    internal WindowSession(NativeUiCallClient hostControl, NativeUiCallClient hostUi)
    {
        _hostControl = hostControl; _hostUi = hostUi;
        _operations = new(ExecuteAsync, message =>
        {
            using var writer = WindowControlProtocol.Write((int)WindowControlCallback.AsyncCompletion, callback: true);
            writer.Buffer(message); return _hostControl.Exchange(writer.Finish());
        }, Report);
        _control = new(ExchangeControl, Report);
        _ui = new(ExchangeUi, Report);
    }

    internal bool HasNativeStartup => Volatile.Read(ref _startup) is not null;
    internal NativeUiCallTable ControlTable => _control.Table;
    internal NativeUiCallTable UiTable => _ui.Table;

    internal int RunMain()
    {
        if (Interlocked.CompareExchange(ref _runEntered, 1, 0) != 0)
            throw new InvalidOperationException("The native main entry runs once.");
        return AvaloniaUiStartupSession.RunApplicationOnMainThread(() =>
        {
            using var writer = WindowControlProtocol.Write((int)WindowControlCallback.BeginMain, callback: true);
            _hostControl.Exchange(writer.Finish());
            return _main.Task;
        });
    }

    private byte[] ExchangeControl(ReadOnlyMemory<byte> request)
    {
        if (request.IsEmpty) throw new InvalidDataException("Missing native window channel.");
        if (request.Span[0] == 0) return _operations.Exchange(request[1..]);
        if (request.Span[0] != 1) throw new InvalidDataException("Unknown native window channel.");
        using var reader = WindowControlProtocol.Read(request[1..], out int command);
        switch ((WindowCommand)command)
        {
            case WindowCommand.MainCompleted:
                int exitCode = reader.Int32(); reader.End(); _main.TrySetResult(exitCode); return [];
            case WindowCommand.ReportStage:
                string stage = Required(reader.String()); reader.End(); Startup.ReportStage(stage); return [];
            case WindowCommand.ReportFailure:
                string failure = Required(reader.String()); reader.End(); Startup.ReportFailure(failure); return [];
            case WindowCommand.FailureLog:
                string? log = reader.String(); reader.End(); Startup.SetFailureLog(log); return [];
            case WindowCommand.CloseWindow:
                reader.End(); _actions.RequestClose(); return [];
            case WindowCommand.Metrics:
                reader.End(); return WindowValueChannel.EncodeRuntimeCapture(AvaloniaUiRuntimeDiagnostics.Capture());
            default: throw new InvalidDataException("Unknown synchronous window command.");
        }
    }

    private byte[] ExchangeUi(ReadOnlyMemory<byte> request)
    {
        if (request.IsEmpty) throw new InvalidDataException("Missing window UI command.");
        switch ((WindowUiCommand)request.Span[0])
        {
            case WindowUiCommand.Continuation:
                if (request.Length != 9) throw new InvalidDataException("Invalid window continuation.");
                long id = BinaryPrimitives.ReadInt64LittleEndian(request.Span[1..]);
                if (id <= 0 || Startup.Completion.IsCompleted) throw new InvalidOperationException("Window dispatcher is stopped.");
                Dispatcher.UIThread.Post(() =>
                {
                    byte[] message = new byte[9]; message[0] = (byte)WindowUiCallback.Continuation;
                    BinaryPrimitives.WriteInt64LittleEndian(message.AsSpan(1), id);
                    try { _hostUi.Exchange(message); } catch (Exception error) { Report(error); }
                });
                return [];
            case WindowUiCommand.Invalidate:
                if (request.Length != 2 || (request.Span[1] & ~3) != 0)
                    throw new InvalidDataException("Invalid window invalidation.");
                Interlocked.Or(ref _invalidations, request.Span[1]);
                if (Interlocked.Exchange(ref _invalidationQueued, 1) == 0) Dispatcher.UIThread.Post(Invalidate);
                return [];
            case WindowUiCommand.Platform:
                if (!Dispatcher.UIThread.CheckAccess()) throw new InvalidOperationException("Synchronous platform effects belong to the native UI thread.");
                return NativeContext.ExecuteInline(() =>
                {
                    using var reader = WindowControlProtocol.Read(request[1..], out int command);
                    if (command != (int)WindowCommand.Platform) throw new InvalidDataException("Invalid synchronous platform request.");
                    return ExecutePlatformSynchronous((WindowPlatformCommand)reader.Int32(), reader, _lifetime.Token);
                });
            case WindowUiCommand.Text:
                if (!Dispatcher.UIThread.CheckAccess()) throw new InvalidOperationException("Text measurement belongs to the native UI thread.");
                return (_text ?? throw new InvalidOperationException("Native text layout is not ready.")).Exchange(request[1..]);
            default: throw new InvalidDataException("Unknown window UI command.");
        }
    }

    private void Invalidate()
    {
        Interlocked.Exchange(ref _invalidationQueued, 0);
        var kind = (XsrUiShellInvalidation)Interlocked.Exchange(ref _invalidations, 0);
        if (_shell is null || Startup.Completion.IsCompleted) return;
        try { _shell.NotifyInvalidated(kind); } catch (Exception error) { Report(error); }
    }

    private async Task<byte[]> ExecuteAsync(ReadOnlyMemory<byte> request, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        return await ExecuteCoreAsync(request, linked.Token).ConfigureAwait(false);
    }

    private async Task<byte[]> ExecuteCoreAsync(ReadOnlyMemory<byte> request, CancellationToken token)
    {
        using var reader = WindowControlProtocol.Read(request, out int command);
        switch ((WindowCommand)command)
        {
            case WindowCommand.Start:
                int count = reader.Count();
                if (count < 0) throw new InvalidDataException("Missing window arguments.");
                string[] args = new string[count];
                for (int i = 0; i < args.Length; i++) args[i] = Required(reader.String());
                bool disableHardware = reader.Boolean();
                var appearance = WindowValueChannel.DecodeStartup(reader.Buffer()); reader.End();
                lock (_startupGate)
                {
                    if (_startRequested || _quiescing) throw new InvalidOperationException("Native startup is no longer admissible.");
                    _startRequested = true;
                }
                try
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
                    _startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync(args, disableHardware, appearance, Localize, linked.Token).ConfigureAwait(false);
                    _nativeContext = new(id => Dispatcher.UIThread.Post(() => _nativeContext!.Execute(id)), Report);
                    _nativeContext.OperationStarted();
                    _created.TrySetResult(_startup); _ = ObserveUiLifetimeAsync(_startup);
                    return [];
                }
                catch (Exception error) { _created.TrySetException(error); _ = _created.Task.Exception; throw; }
            case WindowCommand.PrepareShell:
                var desktopPolicy = WindowValueChannel.DecodeDesktopPolicy(reader.Buffer()); reader.End();
                await NativeContext.RunAsync(() =>
                {
                    if (_shell is not null) return Task.CompletedTask;
                    var renderer = new XsrUiRemoteRenderer(new UiTransport(_hostUi, WindowUiCallback.Input), provider =>
                    {
                        _text = provider is null ? null : new(provider);
                        _hostUi.Exchange(new byte[] { (byte)WindowUiCallback.InstallText, provider is null ? (byte)0 : (byte)1 });
                    });
                    _shell = new(new UiTransport(_hostUi, WindowUiCallback.Shell), renderer);
                    _actions.SetDesktopPolicy(desktopPolicy);
                    return Task.CompletedTask;
                }, token).ConfigureAwait(false);
                await Startup.PrepareShellAsync(_shell!, _actions, token).ConfigureAwait(false);
                return [];
            case WindowCommand.WarmShell:
                reader.End();
                await Startup.WarmUpShellAsync(Shell, token).ConfigureAwait(false); return [];
            case WindowCommand.Present:
                reader.End();
                int result = await Task.Run(() => AvaloniaUiShellHost.Run(Startup, Shell, _actions), token).ConfigureAwait(false);
                return BitConverter.GetBytes(result);
            case WindowCommand.Platform:
                var platformCommand = (WindowPlatformCommand)reader.Int32();
                byte[] platformResult = [];
                await NativeContext.RunAsync(async () =>
                { platformResult = await ExecutePlatformAsync(platformCommand, reader, token); }, token).ConfigureAwait(false);
                return platformResult;
            case WindowCommand.Appearance:
                var updatedAppearance = WindowValueChannel.DecodeStartup(reader.Buffer()); reader.End();
                await NativeContext.RunAsync(() => { Startup.SetAppearance(updatedAppearance); return Task.CompletedTask; }, token).ConfigureAwait(false);
                return [];
            case WindowCommand.Relocalize:
                reader.End(); Startup.SetLocalizer(Localize); return [];
            case WindowCommand.StartupFacts:
                reader.End();
                byte[] facts = [];
                await NativeContext.RunAsync(() =>
                {
                    facts = WindowStartupFacts.Encode(new(Startup.Appearance, Startup.HardwareAccelerationDisabled,
                        Startup.Stage, Startup.FirstRenderElapsed?.Ticks, Startup.ShellReadyElapsed?.Ticks));
                    return Task.CompletedTask;
                }, token).ConfigureAwait(false);
                return facts;
            case WindowCommand.DiscardShell:
                reader.End(); await Startup.DiscardPreparedShellAsync().ConfigureAwait(false); return [];
            case WindowCommand.WaitForRetry:
                string retryMessage = Required(reader.String()); reader.End();
                bool retry = false;
                await NativeContext.RunAsync(async () => { retry = await Startup.WaitForRetryAsync(retryMessage, token); }, token).ConfigureAwait(false);
                return [retry ? (byte)1 : (byte)0];
            case WindowCommand.QuiesceUi:
                reader.End(); await QuiesceUiAsync().ConfigureAwait(false); return [];
            default: throw new InvalidDataException("Unknown asynchronous window command.");
        }
    }

    private AvaloniaUiStartupSession Startup => Volatile.Read(ref _startup) ?? throw new InvalidOperationException("Native startup is not ready.");
    private NativeUiSynchronizationContext NativeContext => _nativeContext ?? throw new InvalidOperationException("Native UI context is not ready.");
    private XsrUiRemoteShell Shell => _shell ?? throw new InvalidOperationException("Native shell is not prepared.");
    private static string Required(string? value) => value ?? throw new InvalidDataException("Missing window value.");

    private string Localize(string text)
    {
        using var writer = WindowControlProtocol.Write((int)WindowControlCallback.Localize, callback: true);
        writer.String(text);
        using var reader = WindowControlProtocol.Read(_hostControl.Exchange(writer.Finish()), out int kind, callback: true);
        if (kind != (int)WindowControlCallback.Localize) throw new InvalidDataException("Invalid localization response.");
        string result = Required(reader.String()); reader.End(); return result;
    }

    private async Task ObserveUiLifetimeAsync(AvaloniaUiStartupSession startup)
    {
        try { await startup.Completion.ConfigureAwait(false); }
        catch (Exception error) { Report(error); }
        try { await StopUiAsync().ConfigureAwait(false); }
        catch (Exception error) { Report(error); _main.TrySetResult(1); }
    }

    private Task StopUiAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? previous = Interlocked.CompareExchange(ref _uiStopped, completion.Task, null);
        if (previous is null) _ = StopUiCoreAsync(completion);
        return previous ?? completion.Task;
    }
    private async Task StopUiCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            await _ui.DisposeAsync().ConfigureAwait(false);
            _lifetime.Cancel();
            if (_nativeContext is { } context)
                context.UseCleanupDispatcher(id => ThreadPool.QueueUserWorkItem(_ => context.Execute(id)));
            using var writer = WindowControlProtocol.Write((int)WindowControlCallback.UiStopped, callback: true);
            byte[] message = writer.Finish(); int delay = 1;
            while (true)
            {
                try { _hostControl.Exchange(message); break; }
                catch (NativeUiCallBusyException)
                { await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32); }
            }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private Task QuiesceUiAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? previous = Interlocked.CompareExchange(ref _quiesced, completion.Task, null);
        if (previous is null) _ = QuiesceUiCoreAsync(completion);
        return previous ?? completion.Task;
    }
    private async Task QuiesceUiCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            bool started;
            lock (_startupGate) { _quiescing = true; started = _startRequested; }
            _lifetime.Cancel();
            if (started)
            {
                AvaloniaUiStartupSession? startup = null;
                try { startup = await _created.Task.ConfigureAwait(false); }
                catch (Exception error) { Report(error); }
                if (startup is not null) await startup.DisposeAsync().ConfigureAwait(false);
            }
            await StopUiAsync().ConfigureAwait(false); completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    internal void Report(Exception error)
    {
        try
        {
            using var writer = WindowControlProtocol.Write((int)WindowControlCallback.Diagnostic, callback: true);
            writer.String(error.GetType().FullName); writer.Int32(error.HResult);
            _hostControl.Exchange(writer.Finish());
        }
        catch { } // Never unwind native callbacks; exception messages/credentials stay in their owner.
    }

    public async ValueTask DisposeAsync()
    {
        _main.TrySetResult(1);
        Task calls = _control.DisposeAsync().AsTask();
        Task operations = _operations.DisposeAsync().AsTask();
        await QuiesceUiAsync().ConfigureAwait(false);
        await Task.WhenAll(calls, operations).ConfigureAwait(false);
        if (_nativeContext is { } context)
        { context.OperationCompleted(); await context.DisposeAsync().ConfigureAwait(false); }
        _text = null; _shell = null; _lifetime.Dispose();
    }

    private sealed class UiTransport(NativeUiCallClient client, WindowUiCallback callback) : IXsrUiWireTransport
    {
        public byte[] Exchange(ReadOnlyMemory<byte> request) => client.Exchange(WindowControlProtocol.Prefix((byte)callback, request));
    }
}
