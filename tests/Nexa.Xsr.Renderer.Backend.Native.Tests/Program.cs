using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Native;
using Nexa.Xsr.State;

if (args is not [var library, var mode] || mode is not ("headless" or "window"))
    throw new ArgumentException("Expected a native window library and headless/window mode.");

try
{
    NativeWindowSession.RunApplicationOnMainThread(() => Task.FromResult(0), library + ".missing");
    throw new InvalidOperationException("Missing native libraries must reject admission.");
}
catch (DllNotFoundException) { }

int result = NativeWindowSession.RunApplicationOnMainThread(async () =>
{
    var session = NativeWindowSession.Current;
    List<string> diagnostics = [];
    session.Diagnostic += (type, code) =>
    {
        string message = $"{type}: 0x{code:X8}"; Console.Error.WriteLine(message);
        lock (diagnostics) diagnostics.Add(message);
    };
    if (mode == "headless")
    {
        if (session.Capture().CommittedScenes != 0) throw new InvalidOperationException("Headless entry created a scene.");
        return 23;
    }
    await session.StartAsync([], OperatingSystem.IsLinux() || OperatingSystem.IsWindows(), new());
    var discarded = XsrUiShellComposer.Compose(new XsrStateStoreBuilder().Build());
    await session.PrepareShellAsync(discarded);
    await session.WarmUpShellAsync();
    await session.SetAnimationFrameRateAsync(24);
    if (session.Capture().MotionTargetFramesPerSecond != 24) throw new InvalidOperationException("The native frame-rate effect was not applied.");
    session.Localize = text => "Native " + text;
    var localized = await session.LocalizeDesktopCaptionsAsync(["First", "Second"]);
    if (!localized.SequenceEqual(["Native First", "Native Second"])) throw new InvalidOperationException("Native localization crossed the wrong callback boundary.");
    if ((await session.ReadPlatformAsync()).DoubleClickInterval <= TimeSpan.Zero) throw new InvalidOperationException("Missing platform input facts.");
    SynchronizationContext? inputContext = null;
    await session.InvokeAsync(() => inputContext = SynchronizationContext.Current);
    int polls = 0;
    var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await using var polling = await session.StartInputPollingAsync(TimeSpan.FromMilliseconds(5), () =>
    {
        if (inputContext is null || !ReferenceEquals(SynchronizationContext.Current, inputContext))
            throw new InvalidOperationException("Input polling lost the Host continuation context.");
        if (Interlocked.Increment(ref polls) >= 3) polled.TrySetResult();
        return TimeSpan.FromMilliseconds(10);
    });
    await polled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await polling.DisposeAsync();
    int stopped = Volatile.Read(ref polls);
    await Task.Delay(60);
    if (Volatile.Read(ref polls) != stopped) throw new InvalidOperationException("Retired input polling still called the Host.");
    NativeWindowInputLease? self = null;
    var selfReleased = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
    self = await session.StartInputPollingAsync(TimeSpan.FromMilliseconds(10), () =>
    {
        if (self is not null) selfReleased.TrySetResult(self.DisposeAsync().AsTask());
        return TimeSpan.FromMilliseconds(20);
    });
    await (await selfReleased.Task.WaitAsync(TimeSpan.FromSeconds(5))).WaitAsync(TimeSpan.FromSeconds(5));
    await self.DisposeAsync();
    await session.DiscardPreparedShellAsync();
    var shell = XsrUiShellComposer.Compose(new XsrStateStoreBuilder().Build());
    int closes = 0;
    session.CloseRequested = () => { Interlocked.Increment(ref closes); return true; };
    await session.PrepareShellAsync(shell);
    await using var theme = await session.PrepareThemeAsync(new(1));
    await session.CommitThemeAsync(theme);
    await session.WarmUpShellAsync();
    await session.InvokeAsync(() =>
    {
        if (shell.Renderer.TextLayoutProvider is null) throw new InvalidOperationException("Native text was not installed.");
    });
    Task<int> presented = session.PresentAsync();
    DateTime deadline = DateTime.UtcNow.AddSeconds(15);
    while (session.Capture().NodePaintCallbacks == 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
    if (session.Capture().NodePaintCallbacks == 0) throw new InvalidOperationException("The native backend never painted the Host scene.");
    session.RequestClose();
    int exit = await presented.WaitAsync(TimeSpan.FromSeconds(15));
    await session.Completion.WaitAsync(TimeSpan.FromSeconds(15));
    if (closes != 1) throw new InvalidOperationException("Native close admission was not delivered exactly once.");
    lock (diagnostics)
        if (diagnostics.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics));
    return exit;
}, library);

if (result != (mode == "headless" ? 23 : 0)) throw new InvalidOperationException($"Unexpected native exit code {result}.");
Console.WriteLine($"PASS: production native window client {mode} admission, ownership and retirement");
