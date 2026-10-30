using Avalonia;
using Avalonia.Threading;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void BackendPortCoalescesInvalidationAndRetiresSubscriptions()
    {
        var shell = new XsrUiShell(new XsrStateStoreBuilder().Build());
        shell.Renderer.ReducedMotion = true;
        var port = new ObservedShellPort(shell);
        using (var surface = new AvaloniaUiSceneSurface(port))
        {
            AssertEqual(1, port.RenderSubscribers);
            AssertEqual(1, port.NavigationSubscribers);
            surface.Measure(new Size(850, 500));
            surface.Arrange(new Rect(0, 0, 850, 500));
            surface.CommitScene();
            Dispatcher.UIThread.RunJobs();
            AssertTrue(surface.Scene is { Count: > 0 });
            int before = port.RenderCalls;
            for (int i = 0; i < 100; i++) port.Invalidate();
            Dispatcher.UIThread.RunJobs();
            AssertEqual(before + 1, port.RenderCalls);
        }
        AssertEqual(0, port.RenderSubscribers);
        AssertEqual(0, port.NavigationSubscribers);
        int retired = port.RenderCalls;
        port.Invalidate();
        Dispatcher.UIThread.RunJobs();
        AssertEqual(retired, port.RenderCalls);
    }

    private static async Task InputPollingRetiresCallbacksAsync()
    {
        int retiredCalls = 0;
        var retired = AvaloniaUiPlatformActions.StartInputPolling(TimeSpan.FromMilliseconds(1), () =>
        {
            retiredCalls++;
            return TimeSpan.FromMilliseconds(1);
        });
        // Dispose before the queued UI-thread start; it must never resurrect the lease.
        Task.Run(retired.Dispose).GetAwaiter().GetResult();
        retired.Dispose();
        int calls = 0;
        IDisposable? active = null;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        active = AvaloniaUiPlatformActions.StartInputPolling(TimeSpan.FromMilliseconds(1), () =>
        {
            AssertTrue(Dispatcher.UIThread.CheckAccess());
            calls++;
            active!.Dispose();
            reached.TrySetResult();
            return TimeSpan.FromMilliseconds(1);
        });
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            await Task.Delay(30).ConfigureAwait(true);
            AssertEqual(0, retiredCalls);
            AssertEqual(1, calls);
        }
        finally { active.Dispose(); }
    }

    // Deliberately not XsrUiShell: native adapters must be consumable without a cast
    // back to the local tree, stage or state bridge.
    private sealed class ObservedShellPort(XsrUiShell shell) : IXsrUiBackendShell
    {
        private readonly IXsrUiBackendShell _inner = shell;
        private event EventHandler? _invalidated;
        public int RenderSubscribers { get; private set; }
        public int NavigationSubscribers { get; private set; }
        public int RenderCalls { get; private set; }
        public string Title => _inner.Title;
        public string Version => _inner.Version;
        public XsrUiEntityId Root => _inner.Root;
        public XsrUiEntityId CurrentPage => _inner.CurrentPage;
        public bool HasContentTransition => _inner.HasContentTransition;
        public XsrSemanticId SelectedNavigationId => _inner.SelectedNavigationId;
        public XsrUiShellPalette Palette => _inner.Palette;
        public bool IsNavigationExpanded => _inner.IsNavigationExpanded;
        public double RailPresentationProgress => _inner.RailPresentationProgress;
        public IXsrUiBackendRenderer Renderer => _inner.Renderer;
        public event EventHandler? RenderRequested
        {
            add { _invalidated += value; RenderSubscribers++; }
            remove { _invalidated -= value; RenderSubscribers--; }
        }
        public event EventHandler? NavigationExpandedChanged
        {
            add { _inner.NavigationExpandedChanged += value; NavigationSubscribers++; }
            remove { _inner.NavigationExpandedChanged -= value; NavigationSubscribers--; }
        }
        public void Invalidate() => _invalidated?.Invoke(this, EventArgs.Empty);
        public XsrUiScene Render(XsrUiSize viewport) { RenderCalls++; return _inner.Render(viewport); }
        public void SetRailPresentationProgress(double value) => _inner.SetRailPresentationProgress(value);
        public void PublishWindowActivity(bool active, bool minimized) => _inner.PublishWindowActivity(active, minimized);
        public void PublishWindowMetrics(double trafficLightPadding, bool fullscreen, double contentInset = 0) =>
            _inner.PublishWindowMetrics(trafficLightPadding, fullscreen, contentInset);
    }
}
