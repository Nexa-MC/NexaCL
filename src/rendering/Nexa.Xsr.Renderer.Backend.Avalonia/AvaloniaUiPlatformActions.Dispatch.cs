using Avalonia.Threading;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiPlatformActions
{
    /// <summary>Queues work on the native UI thread, including before window attachment.</summary>
    public static void PostToUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Dispatcher.UIThread.Post(action);
    }

    /// <summary>Polls input on the UI thread; each callback returns the next polling interval.</summary>
    public static IDisposable StartInputPolling(TimeSpan interval, Func<TimeSpan> poll)
    {
        ArgumentNullException.ThrowIfNull(poll);
        return new InputPollingLease(interval, poll);
    }

    private sealed class InputPollingLease : IDisposable
    {
        private readonly object _gate = new();
        private readonly DispatcherTimer _timer;
        private Func<TimeSpan>? _poll;

        public InputPollingLease(TimeSpan interval, Func<TimeSpan> poll)
        {
            _poll = poll;
            _timer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher.UIThread) { Interval = interval };
            _timer.Tick += OnTick;
            Dispatcher.UIThread.Post(() =>
            {
                lock (_gate) { if (_poll is not null) _timer.Start(); }
            });
        }

        private void OnTick(object? sender, EventArgs args)
        {
            lock (_gate)
            {
                if (_poll is not { } poll) return;
                TimeSpan next = poll();
                if (_poll is not null) _timer.Interval = next;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_poll is null) return;
                _poll = null;
            }
            void Stop() { _timer.Stop(); _timer.Tick -= OnTick; }
            if (Dispatcher.UIThread.CheckAccess()) Stop();
            else Dispatcher.UIThread.Post(Stop);
        }
    }
}
