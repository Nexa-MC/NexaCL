using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Wire;
using Nexa.Xsr.State;

namespace Nexa.Native.Ui.Tests;

internal static partial class Program
{
    private static unsafe void IndependentWindow(string path)
    {
        nint library = NativeLibrary.Load(Path.GetFullPath(path));
        var open = (delegate* unmanaged[Cdecl]<NativeUiCallTable*, NativeUiCallTable*, NativeUiCallTable*, NativeUiCallTable*, int>)NativeLibrary.GetExport(library, "nexa_window_open_v1");
        var run = (delegate* unmanaged[Cdecl]<long, int>)NativeLibrary.GetExport(library, "nexa_window_run_v1");
        var close = (delegate* unmanaged[Cdecl]<long, int>)NativeLibrary.GetExport(library, "nexa_window_close_v1");
        var owner = new WindowOwner();
        var hostControl = owner.Control.Table; var hostUi = owner.Ui.Table;
        NativeUiCallTable control = default, ui = default;
        Equal(0, open(&hostControl, &hostUi, &control, &ui));
        owner.Connect(control, ui);
        int probe = EnableWindowProbe(library);
        Equal(0, run(control.Context)); // macOS must enter on the original process thread.
        var hits = (delegate* unmanaged[Cdecl]<int, long>)NativeLibrary.GetExport(library, "nexa_instrumentation_hits_v1");
        True(hits(probe) > 0);
        owner.Finished.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            int state = close(control.Context); True(state >= 0);
            if (state == 1) break;
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Native window retirement timed out.");
            Thread.Sleep(10);
        }
        owner.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine("PASS: independent native window painted Host scenes, measured text and drained both runtimes without CoreCLR");
    }

    private static unsafe int EnableWindowProbe(nint library)
    {
        var target = (delegate* unmanaged[Cdecl]<int, byte*, int, int>)NativeLibrary.GetExport(library, "nexa_instrumentation_target_v1");
        var configure = (delegate* unmanaged[Cdecl]<int, int, int>)NativeLibrary.GetExport(library, "nexa_instrumentation_probe_v1");
        int count = target(-1, null, 0); True(count > 0);
        for (int i = 0; i < count; i++)
        {
            int length = target(i, null, 0); True(length > 0);
            byte[] bytes = new byte[length];
            fixed (byte* output = bytes) Equal(length, target(i, output, length));
            string signature = System.Text.Encoding.UTF8.GetString(bytes);
            if (!signature.Contains("AvaloniaUiStartupSession::ReportStage``0(", StringComparison.Ordinal)) continue;
            Equal(0, configure(i, 1)); return i;
        }
        throw new InvalidOperationException("The window probe catalog does not cover the actual backend.");
    }

    private sealed class WindowOwner : IAsyncDisposable
    {
        internal readonly NativeUiCallRegistration Control, Ui;
        internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private NativeUiCallClient _control = null!, _ui = null!;
        private NativeUiAsyncClient _operations = null!;
        private NativeUiSynchronizationContext _context = null!;
        private XsrUiShell _shell = null!;
        private XsrUiShellEndpoint _shellEndpoint = null!;
        private XsrUiRendererEndpoint _input = null!;
        private int _begun;
        internal WindowOwner() { Control = new(ControlCall); Ui = new(UiCall); }
        public async ValueTask DisposeAsync()
        { await Ui.DisposeAsync(); await Control.DisposeAsync(); }
        internal void Connect(NativeUiCallTable control, NativeUiCallTable ui)
        {
            _control = new(control); _ui = new(ui);
            _operations = new(message => _control.Exchange(WindowControlProtocol.Prefix(0, message)));
            _context = new(id =>
            {
                byte[] message = new byte[9]; message[0] = (byte)WindowUiCommand.Continuation;
                BinaryPrimitives.WriteInt64LittleEndian(message.AsSpan(1), id); _ui.Exchange(message);
            }, error => Finished.TrySetException(error));
            _context.OperationStarted();
        }
        private byte[] ControlCall(ReadOnlyMemory<byte> message)
        {
            using var reader = WindowControlProtocol.Read(message, out int kind, callback: true);
            switch ((WindowControlCallback)kind)
            {
                case WindowControlCallback.BeginMain:
                    reader.End();
                    if (Interlocked.Exchange(ref _begun, 1) != 0) throw new InvalidOperationException("Duplicate main callback.");
                    _ = Task.Run(Bootstrap); return [];
                case WindowControlCallback.AsyncCompletion:
                    byte[] completion = reader.Buffer(); reader.End(); return _operations.Complete(completion);
                case WindowControlCallback.UiStopped:
                    reader.End(); _ = RetireUi(); return [];
                case WindowControlCallback.Localize:
                    string? text = reader.String(); reader.End();
                    using (var writer = WindowControlProtocol.Write(kind, callback: true))
                    { writer.String(text); return writer.Finish(); }
                case WindowControlCallback.Diagnostic:
                    string? type = reader.String(); int code = reader.Int32(); reader.End();
                    Console.Error.WriteLine($"Native window: {type} 0x{code:X8}"); return [];
                default: throw new InvalidDataException("Unknown window callback.");
            }
        }
        private async Task RetireUi()
        {
            try
            {
                await Ui.DisposeAsync();
                _context.UseCleanupDispatcher(id => ThreadPool.QueueUserWorkItem(_ => _context.Execute(id)));
                _stopped.TrySetResult();
            }
            catch (Exception error) { _stopped.TrySetException(error); }
        }
        private byte[] UiCall(ReadOnlyMemory<byte> message)
        {
            if (message.IsEmpty) throw new InvalidDataException();
            if (message.Span[0] == (byte)WindowUiCallback.Continuation)
            { if (message.Length != 9) throw new InvalidDataException(); _context.Execute(BinaryPrimitives.ReadInt64LittleEndian(message.Span[1..])); return []; }
            return _context.ExecuteInline(() =>
            {
                switch ((WindowUiCallback)message.Span[0])
                {
                    case WindowUiCallback.Shell: return _shellEndpoint.Exchange(message[1..]);
                    case WindowUiCallback.Input: return _input.Exchange(message[1..]);
                    case WindowUiCallback.InstallText:
                        if (message.Length != 2 || message.Span[1] > 1) throw new InvalidDataException();
                        _shell.Renderer.TextLayoutProvider = message.Span[1] == 0 ? null
                            : new XsrUiRemoteTextLayoutProvider(new InputTransport(_ui, (byte)WindowUiCommand.Text));
                        return [];
                    default: throw new InvalidDataException();
                }
            });
        }
        private Task<byte[]> Invoke(WindowCommand command)
        { using var writer = WindowControlProtocol.Write((int)command); return _operations.InvokeAsync(writer.Finish()); }
        private byte[] Call(WindowCommand command, int? value = null)
        {
            using var writer = WindowControlProtocol.Write((int)command);
            if (value.HasValue) writer.Int32(value.Value);
            return _control.Exchange(WindowControlProtocol.Prefix(1, writer.Finish()));
        }
        private Task<byte[]> Platform(WindowPlatformCommand command, Action<Nexa.Native.Window.Wire.UiWireWriter>? arguments = null)
        {
            using var writer = WindowControlProtocol.Write((int)WindowCommand.Platform);
            writer.Int32((int)command); arguments?.Invoke(writer);
            return _operations.InvokeAsync(writer.Finish());
        }
        private static Nexa.Native.Window.Wire.UiWireReader PlatformResult(byte[] bytes, WindowPlatformCommand expected)
        {
            var reader = WindowControlProtocol.Read(bytes, out int kind);
            try
            {
                Equal((int)WindowCommand.Platform, kind); Equal((int)expected, reader.Int32()); return reader;
            }
            catch { reader.Dispose(); throw; }
        }
        private async Task VerifyPlatform()
        {
            await Platform(WindowPlatformCommand.Resize, writer => writer.Boolean(false));
            await Platform(WindowPlatformCommand.Resize, writer => writer.Boolean(true));
            await _context.RunAsync(() =>
            {
                using var request = WindowControlProtocol.Write((int)WindowCommand.Platform);
                request.Int32((int)WindowPlatformCommand.FrameRate); request.Int32(60);
                using var response = PlatformResult(_ui.Exchange(WindowControlProtocol.Prefix((byte)WindowUiCommand.Platform, request.Finish())), WindowPlatformCommand.FrameRate);
                response.End(); return Task.CompletedTask;
            });
            await Platform(WindowPlatformCommand.WindowAppearance, writer => { writer.Int32(100); writer.Boolean(false); });
            await Platform(WindowPlatformCommand.CopyText, writer => writer.String("native clipboard before malformed request"));
            try
            {
                await Platform(WindowPlatformCommand.CopyText, writer => { writer.String("must not replace clipboard"); writer.Byte(1); });
                throw new InvalidDataException("Malformed platform command unexpectedly succeeded.");
            }
            catch (InvalidOperationException) { }
            await _context.RunAsync(() =>
            {
                using var request = WindowControlProtocol.Write((int)WindowCommand.Platform);
                request.Int32((int)WindowPlatformCommand.CopyText); request.String("async operation must not enter synchronous channel");
                Throws<InvalidOperationException>(() => _ui.Exchange(WindowControlProtocol.Prefix((byte)WindowUiCommand.Platform, request.Finish())));
                return Task.CompletedTask;
            });
            using (var clipboard = PlatformResult(await Platform(WindowPlatformCommand.ReadClipboard), WindowPlatformCommand.ReadClipboard))
            { Equal("native clipboard before malformed request", clipboard.String()); clipboard.End(); }
            using (var progress = PlatformResult(await Platform(WindowPlatformCommand.TaskbarProgress,
                writer => { writer.Boolean(false); writer.Boolean(false); }), WindowPlatformCommand.TaskbarProgress))
            { _ = progress.Boolean(); progress.End(); }
            using (var captions = PlatformResult(await Platform(WindowPlatformCommand.LocalizeCaptions,
                writer => { writer.Count(2); writer.String("First"); writer.String("Second"); }), WindowPlatformCommand.LocalizeCaptions))
            { Equal(2, captions.Count()); Equal("First", captions.String()); Equal("Second", captions.String()); captions.End(); }
            using (var facts = PlatformResult(await Platform(WindowPlatformCommand.Facts), WindowPlatformCommand.Facts))
            {
                _ = facts.Boolean(); True(facts.Byte() <= 2); True(facts.Int64() > 0); _ = facts.Int64();
                if (facts.Boolean()) _ = WindowValueChannel.DecodeMaterial(facts.Buffer());
                facts.End();
            }
            try
            {
                await Platform(WindowPlatformCommand.SaveDocument,
                    writer => { writer.Buffer(new byte[] { 255 }); writer.Int32(1024); writer.String(null); });
                throw new InvalidDataException("Invalid UTF-8 unexpectedly opened a document picker.");
            }
            catch (InvalidOperationException) { }
            Console.WriteLine("PASS: native clipboard survives malformed effects; platform facts, preferences and indeterminate progress cross the value boundary");
        }
        private async Task Bootstrap()
        {
            Exception? failure = null;
            try
            {
                using (var writer = WindowControlProtocol.Write((int)WindowCommand.Start))
                {
                    writer.Int32(0); writer.Boolean(true);
                    writer.Buffer(WindowValueChannel.EncodeStartup(new AvaloniaUiStartupAppearance(ReducedMotion: true)));
                    await _operations.InvokeAsync(writer.Finish());
                }
                var initial = WindowStartupFacts.Decode(await Invoke(WindowCommand.StartupFacts));
                Equal(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), initial.HardwareAccelerationDisabled);
                True(initial.Appearance.ReducedMotion);
                var updated = initial.Appearance with { ThemeMode = XsrUiThemeMode.Dark, ProductVersion = "2.0.0.alpha.18" };
                using (var appearance = WindowControlProtocol.Write((int)WindowCommand.Appearance))
                {
                    appearance.Buffer(WindowValueChannel.EncodeStartup(updated));
                    await _operations.InvokeAsync(appearance.Finish());
                }
                Equal(updated, WindowStartupFacts.Decode(await Invoke(WindowCommand.StartupFacts)).Appearance);
                await Invoke(WindowCommand.Relocalize);
                using (var cancellation = new CancellationTokenSource())
                using (var retry = WindowControlProtocol.Write((int)WindowCommand.WaitForRetry))
                {
                    retry.String("Native startup retry");
                    Task<byte[]> pendingRetry = _operations.InvokeAsync(retry.Finish(), cancellation.Token);
                    DateTime retryDeadline = DateTime.UtcNow.AddSeconds(10);
                    while (WindowStartupFacts.Decode(await Invoke(WindowCommand.StartupFacts)).Stage != "Native startup retry")
                    {
                        if (DateTime.UtcNow >= retryDeadline) throw new TimeoutException("Retry UI did not enter.");
                        await Task.Delay(10);
                    }
                    cancellation.Cancel();
                    try { await pendingRetry; throw new InvalidOperationException("Retry was not canceled."); }
                    catch (OperationCanceledException) { }
                }
                await Invoke(WindowCommand.DiscardShell);
                using (var stage = WindowControlProtocol.Write((int)WindowCommand.ReportStage))
                { stage.String("Native window contract test"); _control.Exchange(WindowControlProtocol.Prefix(1, stage.Finish())); }
                await _context.RunAsync(() =>
                {
                    var builder = new XsrStateStoreBuilder(); XsrUiShellWindowState.Declare(builder);
                    _shell = new(builder.Build());
                    _shell.Tree.SetComponent(_shell.Content, new XsrUiText("Independent native window"));
                    _shellEndpoint = new(_shell); _input = new(_shell.Renderer);
                    _shellEndpoint.Invalidated += kind => _ui.Exchange(new byte[] { (byte)WindowUiCommand.Invalidate, (byte)kind });
                    return Task.CompletedTask;
                });
                using (var prepare = WindowControlProtocol.Write((int)WindowCommand.PrepareShell))
                {
                    prepare.Buffer(WindowValueChannel.EncodeDesktopPolicy(new(TrayEnabled: false)));
                    await _operations.InvokeAsync(prepare.Finish());
                }
                await VerifyPlatform();
                await Invoke(WindowCommand.WarmShell);
                Task<byte[]> presented = Invoke(WindowCommand.Present);
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                while (true)
                {
                    var capture = WindowValueChannel.DecodeRuntimeCapture(Call(WindowCommand.Metrics));
                    if (capture.CommittedScenes > 0 && capture.NodePaintCallbacks > 0) break;
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("Native window did not paint.");
                    await Task.Delay(50);
                }
                Call(WindowCommand.CloseWindow);
                Equal(0, BitConverter.ToInt32(await presented.WaitAsync(TimeSpan.FromSeconds(30))));
            }
            catch (Exception error) { failure = error; Console.Error.WriteLine(error); }
            finally
            {
                try
                {
                    await Invoke(WindowCommand.QuiesceUi).WaitAsync(TimeSpan.FromSeconds(30));
                    await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    await _context.RunCleanupAsync(() => { _shellEndpoint?.Dispose(); return Task.CompletedTask; });
                    _context.OperationCompleted(); await _context.DisposeAsync(); _operations.Dispose();
                }
                catch (Exception error) { failure ??= error; Console.Error.WriteLine(error); }
                Call(WindowCommand.MainCompleted, failure is null ? 0 : 1);
                if (failure is null) Finished.TrySetResult(); else Finished.TrySetException(failure);
            }
        }
    }
}
