using Avalonia.Threading;
using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window;

internal sealed partial class WindowSession
{
    private readonly NativeUiResources<InputLease> _inputLeases = new(32, 0);
    private long _doubleClickLease;

    private byte[] ExchangeInputLease(ReadOnlyMemory<byte> request)
    {
        using var reader = WindowControlProtocol.Read(request, out int command);
        if (command != (int)WindowCommand.InputLease) throw new InvalidDataException("Invalid native input request.");
        var operation = (WindowInputOperation)reader.Int32();
        using var writer = WindowControlProtocol.Write((int)WindowCommand.InputLease);
        if (operation == WindowInputOperation.CancelDoubleClick)
        {
            reader.End(); _lifetime.Token.ThrowIfCancellationRequested();
            CancelDoubleClickLease(); return writer.Finish();
        }
        long callback = reader.Int64();
        TimeSpan interval = operation == WindowInputOperation.Poll ? WindowInputProtocol.Interval(reader.Int64()) : default;
        reader.End();
        if (callback <= 0 || operation is not (WindowInputOperation.Poll or WindowInputOperation.DoubleClick))
            throw new InvalidDataException("Invalid native input callback.");
        _lifetime.Token.ThrowIfCancellationRequested();
        if (operation == WindowInputOperation.DoubleClick) CancelDoubleClickLease();
        var lease = new InputLease();
        long handle = _inputLeases.Register(lease, 0);
        try
        {
            if (!_inputLeases.Claim(handle)) throw new InvalidOperationException("Native input is retiring.");
            if (operation == WindowInputOperation.Poll)
                lease.Attach(AvaloniaUiPlatformActions.StartInputPolling(interval, () => PollInput(handle, callback)));
            else
            {
                _doubleClickLease = handle;
                lease.Attach(new DoubleClickLease(this, handle));
                _actions.ArmPostNavigationDoubleClick(() => InvokeDoubleClick(handle, callback));
            }
            writer.Int64(handle); return writer.Finish();
        }
        catch { _inputLeases.Release(handle); throw; }
    }

    private void CancelDoubleClickLease()
    {
        long previous = Interlocked.Exchange(ref _doubleClickLease, 0);
        _actions.CancelPostNavigationDoubleClick();
        if (previous != 0) _inputLeases.Release(previous);
    }

    private TimeSpan PollInput(long handle, long callback)
    {
        try
        {
            using var borrow = _inputLeases.Acquire(handle);
            return NativeContext.ExecuteInline(() =>
            {
                using var response = InputCallback(WindowInputOperation.Poll, callback);
                var interval = WindowInputProtocol.Interval(response.Int64()); response.End(); return interval;
            });
        }
        catch (Exception error) { _inputLeases.Release(handle); Report(error); return TimeSpan.FromMilliseconds(20); }
    }

    private void InvokeDoubleClick(long handle, long callback)
    {
        try
        {
            using var borrow = _inputLeases.Acquire(handle);
            NativeContext.ExecuteInline(() =>
            {
                using var response = InputCallback(WindowInputOperation.DoubleClick, callback);
                response.End(); return true;
            });
        }
        catch (Exception error) { Report(error); }
        finally { _inputLeases.Release(handle); }
    }

    private UiWireReader InputCallback(WindowInputOperation operation, long callback)
    {
        using var writer = WindowControlProtocol.Write((int)WindowControlCallback.InputLease, callback: true);
        writer.Int32((int)operation); writer.Int64(callback);
        var response = WindowControlProtocol.Read(_hostUi.Exchange(WindowControlProtocol.Prefix(
            (byte)WindowUiCallback.InputLease, writer.Finish())), out int kind, callback: true);
        if (kind == (int)WindowControlCallback.InputLease) return response;
        response.Dispose(); throw new InvalidDataException("Invalid native input callback response.");
    }

    private sealed class InputLease : IDisposable
    {
        private readonly object _gate = new();
        private IDisposable? _resource;
        private bool _retired;

        internal void Attach(IDisposable resource)
        {
            lock (_gate) { if (!_retired) { _resource = resource; return; } }
            resource.Dispose();
        }
        public void Dispose()
        {
            IDisposable? resource;
            lock (_gate) { _retired = true; resource = _resource; _resource = null; }
            resource?.Dispose();
        }
    }

    private sealed class DoubleClickLease(WindowSession owner, long handle) : IDisposable
    {
        public void Dispose()
        {
            void Cancel()
            {
                if (Interlocked.CompareExchange(ref owner._doubleClickLease, 0, handle) == handle)
                    owner._actions.CancelPostNavigationDoubleClick();
            }
            if (Dispatcher.UIThread.CheckAccess()) Cancel(); else Dispatcher.UIThread.Post(Cancel);
        }
    }
}
