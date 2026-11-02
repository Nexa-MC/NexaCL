using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nexa.Core.Media;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static Task WaitForNativeStartupStageAsync(AvaloniaUiStartupSession startup,
        Window window, string expected, CancellationToken token) => startup.InvokeAsync(async () =>
    {
        // ReportStage/ReportFailure post work; an unrelated normal-priority Invoke is not
        // a completion barrier. Observe the real status control, subscribing on its thread
        // before yielding so either an already-applied value or its next change is captured.
        TextBlock status = window.GetVisualDescendants().OfType<ScrollViewer>()
            .Select(viewer => viewer.Content).OfType<TextBlock>().Single();
        if (status.Text == expected) return;
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStatusChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property == TextBlock.TextProperty && status.Text == expected) applied.TrySetResult();
        }
        status.PropertyChanged += OnStatusChanged;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(startup.CancellationToken, token);
        try { await applied.Task.WaitAsync(linked.Token); }
        finally { status.PropertyChanged -= OnStatusChanged; }
    }, token);

    private static async Task VerifyNativeStartupAppearanceAsync(AvaloniaUiStartupSession startup,
        Window window, CancellationToken token)
    {
        object unrelatedOwner = new();
        double unrelatedProgress = 0;
        await startup.InvokeAsync(() =>
        {
            if (OperatingSystem.IsMacOS()) AssertTrue(AvaloniaUiStartupSession.IsOnProcessMainThread);
            AssertEqual("NexaCL", Application.Current!.Name);
            AssertEqual(ThemeVariant.Dark, window.ActualThemeVariant);
            ThemeVariant? originalTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                startup.SetAppearance(startup.Appearance with { ThemeMode = XsrUiThemeMode.Light });
                AssertEqual(ThemeVariant.Light, window.ActualThemeVariant);
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                startup.SetAppearance(startup.Appearance with { ThemeMode = XsrUiThemeMode.System });
                AssertEqual(ThemeVariant.Default, window.RequestedThemeVariant);
                AssertEqual(ThemeVariant.Dark, window.ActualThemeVariant);
                Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                AssertEqual(ThemeVariant.Light, window.ActualThemeVariant);
            }
            finally { Application.Current.RequestedThemeVariant = originalTheme; }

            // The scheduler belongs to the process. Keep another owner's track alive so
            // retiring splash motion cannot accidentally pass by canceling the entire clock.
            AvaloniaUiMotion.Animate(unrelatedOwner, "sentinel", () => unrelatedProgress,
                value => unrelatedProgress = value, 1, 20000);
        }, token);
        try
        {
            startup.ReportStage("loading-past-two-seconds");
            await Task.Delay(2250, token);
            await startup.InvokeAsync(() =>
            {
                var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
                AssertTrue(ReferenceEquals(window, desktop.MainWindow) && window.Topmost && window.IsVisible);
                AssertFalse(startup.Completion.IsCompleted);
                AssertEqual(1d, ((AvaloniaUiStartupCard)window.Content!).Opacity);
                AssertEqual(startup.Appearance.ReducedMotion ? 1 : 2, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
                AssertTrue(unrelatedProgress > 0 && unrelatedProgress < 1);
                window.UpdateLayout();
                var mark = window.GetVisualDescendants().OfType<Image>().Single();
                var name = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "NexaCL");
                var ring = window.GetVisualDescendants().OfType<AvaloniaUiProgressRing>().Single();
                var hint = window.GetVisualDescendants().OfType<ScrollViewer>().Single();
                Point markOrigin = mark.TranslatePoint(default, window)!.Value;
                Point nameOrigin = name.TranslatePoint(default, window)!.Value;
                Point ringOrigin = ring.TranslatePoint(default, window)!.Value;
                Point hintOrigin = hint.TranslatePoint(default, window)!.Value;
                AssertTrue(markOrigin.X + mark.Bounds.Width < nameOrigin.X);
                AssertTrue(ringOrigin.X >= nameOrigin.X && ringOrigin.X + ring.Bounds.Width < hintOrigin.X);
                AssertTrue(ringOrigin.Y < hintOrigin.Y + hint.Bounds.Height && hintOrigin.Y < ringOrigin.Y + ring.Bounds.Height);
                foreach (Button button in window.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible))
                {
                    Point origin = button.TranslatePoint(default, window)!.Value;
                    AssertTrue(origin.X > 0 && origin.Y + button.Bounds.Height < window.ClientSize.Height);
                }

                startup.SetAppearance(startup.Appearance with { ReducedMotion = true });
                AssertTrue(startup.Appearance.ReducedMotion);
                AssertEqual(1d, ((AvaloniaUiStartupCard)window.Content!).Opacity);
                AssertEqual(1, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
                startup.SetAppearance(startup.Appearance with { ReducedMotion = false });
                AssertFalse(startup.Appearance.ReducedMotion);
                AssertEqual(2, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
                startup.SetAppearance(startup.Appearance with { ReducedMotion = true });
                AssertEqual(1, AvaloniaUiMotion.CaptureDiagnostics().ActiveTracks);
            }, token);
        }
        finally { AvaloniaUiMotion.CancelAll(unrelatedOwner); }

        await startup.InvokeAsync(async () =>
        {
            while (AvaloniaUiMotion.CaptureDiagnostics() is not { ActiveTracks: 0, TimerRunning: false })
                await Task.Delay(10, token);
        }, token);
    }

    private static int RunNativeStartupCancelSmoke() => AvaloniaUiStartupSession.RunApplicationOnMainThread(RunNativeStartupCancelSmokeAsync);

    private static async Task<int> RunNativeStartupCancelSmokeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], disableHardwareAcceleration: true,
            new AvaloniaUiStartupAppearance(XsrUiThemeMode.Light, ReducedMotion: true),
            cancellationToken: timeout.Token);
        IClassicDesktopStyleApplicationLifetime desktop = await Dispatcher.UIThread.InvokeAsync(() =>
            (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!);
        Window startupWindow = desktop.MainWindow!;
        await startup.InvokeAsync(() =>
        {
            AssertTrue(startupWindow.Topmost && startupWindow.IsVisible);
            AssertEqual(1d, ((AvaloniaUiStartupCard)startupWindow.Content!).Opacity);
            AssertTrue(AvaloniaUiMotion.CaptureDiagnostics() is { ActiveTracks: 0, TimerRunning: false });
        }, timeout.Token);
        await Task.Delay(250, timeout.Token);
        await startup.InvokeAsync(() =>
            AssertTrue(AvaloniaUiMotion.CaptureDiagnostics() is { ActiveTracks: 0, TimerRunning: false }), timeout.Token);

        int rasterLeaseBaseline = AvaloniaUiRuntimeDiagnostics.Capture().RasterLeases;
        var shell = XsrUiShellComposer.Compose(new XsrStateStoreBuilder().Build());
        shell.Renderer.ReducedMotion = true;
        var page = shell.Tree.Create("startup-cancel-page");
        shell.Tree.SetComponent(page, new XsrUiElement { Weight = 1 });
        var imageEntity = shell.Tree.Create("startup-cancel-raster");
        PngImage png = PngImage.TryCreate(Convert.FromBase64String(TinyPngBase64))!;
        shell.Tree.SetComponent(imageEntity, new XsrUiElement { Width = 32, Height = 32 });
        shell.Tree.SetComponent(imageEntity, new XsrUiImage("startup/cancel-fixture")
        {
            Raster = new(png, [new(new(0, 0, png.Width, png.Height), new(0, 0, 1, 1))]) { FitToBounds = true },
        });
        shell.Tree.Attach(imageEntity, page);
        shell.Stage.Navigation.Replace(page);
        var actions = new AvaloniaUiPlatformActions();
        await startup.PrepareShellAsync(shell, actions, timeout.Token);
        await startup.WarmUpShellAsync(shell, timeout.Token);
        AvaloniaUiShellWindow? preparedWindow = null;
        AvaloniaUiSceneNodeControl? imageControl = null;
        await startup.InvokeAsync(() =>
        {
            preparedWindow = AssertNotNull(startup.PreparedWindow);
            AssertFalse(preparedWindow.IsVisible);
            imageControl = preparedWindow.Surface.GetVisualDescendants().OfType<AvaloniaUiSceneNodeControl>()
                .Single(control => control.Node.Entity == imageEntity);
            AssertTrue(imageControl.HasDecodedRaster);
            AssertTrue(AvaloniaUiRuntimeDiagnostics.Capture().RasterLeases > rasterLeaseBaseline);
            actions.RestoreWindow();
            AssertFalse(preparedWindow.IsVisible);
            AssertTrue(ReferenceEquals(startupWindow, desktop.MainWindow));
            AssertTrue(AvaloniaUiMotion.CaptureDiagnostics() is { ActiveTracks: 0, TimerRunning: false });
        }, timeout.Token);

        // Exercise the real startup-window Escape route, without linking this dispatch to
        // the startup token that closing the window intentionally cancels synchronously.
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            startupWindow.RaiseEvent(escape);
            AssertTrue(escape.Handled);
            AssertTrue(startup.CancellationToken.IsCancellationRequested);
            AssertTrue(startup.PreparedWindow is null && !preparedWindow!.IsVisible);
            AssertFalse(imageControl!.HasDecodedRaster);
            AssertEqual(rasterLeaseBaseline, AvaloniaUiRuntimeDiagnostics.Capture().RasterLeases);
        }, DispatcherPriority.Normal, timeout.Token);
        AssertEqual(0, await startup.Completion.WaitAsync(timeout.Token));
        AssertTrue(AvaloniaUiStartupSession.Active is null && !startup.ShellReadyElapsed.HasValue);
        AssertEqual(0, desktop.Windows.Count);
        AssertTrue(AvaloniaUiMotion.CaptureDiagnostics() is { ActiveTracks: 0, TimerRunning: false });
        bool cancelledPresentation = false;
        try { await Task.Run(() => AvaloniaUiShellHost.Run(startup, shell, actions)); }
        catch (OperationCanceledException) { cancelledPresentation = true; }
        AssertTrue(cancelledPresentation);
        AssertTrue(AvaloniaUiStartupSession.Active is null);
        AssertEqual(0, desktop.Windows.Count);
        Console.WriteLine("PASS: native startup suppresses optional motion initially and Escape discards the warmed hidden shell and its raster leases without presenting a main window.");
        return 0;
    }
}
