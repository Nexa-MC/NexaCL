using Avalonia;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void StartupMainThreadEntryCompletesWithoutNativeInitialization()
    {
        AssertTrue(Application.Current is null);
        int entryThread = Environment.CurrentManagedThreadId, bootstrapThread = 0;
        AssertEqual(7, AvaloniaUiStartupSession.RunApplicationOnMainThread(() =>
        {
            bootstrapThread = Environment.CurrentManagedThreadId;
            return Task.FromResult(7);
        }));
        AssertEqual(OperatingSystem.IsMacOS(), bootstrapThread != entryThread);
        var failure = new InvalidOperationException("bootstrap failed before native request");
        bool failed = false;
        try { AvaloniaUiStartupSession.RunApplicationOnMainThread(() => Task.FromException<int>(failure)); }
        catch (InvalidOperationException error) { failed = ReferenceEquals(failure, error); }
        AssertTrue(failed);
        bool cancelled = false;
        try { AvaloniaUiStartupSession.RunApplicationOnMainThread(() => Task.FromCanceled<int>(new CancellationToken(true))); }
        catch (OperationCanceledException) { cancelled = true; }
        AssertTrue(cancelled);
        AssertTrue(Application.Current is null && AvaloniaUiStartupSession.Active is null);
        if (OperatingSystem.IsMacOS())
        {
            bool wrongThread = Task.Run(() =>
            {
                try { AvaloniaUiStartupSession.RunApplicationOnMainThread(() => Task.FromResult(0)); }
                catch (InvalidOperationException) { return true; }
                return false;
            }).GetAwaiter().GetResult();
            AssertTrue(wrongThread);
            var releaseLateRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? lateRequest = null;
            AssertEqual(0, AvaloniaUiStartupSession.RunApplicationOnMainThread(() =>
            {
                lateRequest = Task.Run(async () =>
                {
                    await releaseLateRequest.Task;
                    await AvaloniaUiStartupSession.StartAsync();
                });
                return Task.FromResult(0);
            }));
            releaseLateRequest.SetResult();
            bool lateRejected = false;
            try { AssertNotNull(lateRequest).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { lateRejected = true; }
            AssertTrue(lateRejected);
            AssertTrue(Application.Current is null && AvaloniaUiStartupSession.Active is null);
        }
    }
}
