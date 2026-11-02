using Avalonia.Threading;
using Nexa.Desktop.Ui;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static int RunNativeGamepadPollingSmoke() =>
        AvaloniaUiStartupSession.RunApplicationOnMainThread(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
                new(ReducedMotion: true), cancellationToken: timeout.Token);
            await startup.InvokeAsync(async () =>
            {
                using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
                fixture.Shell.Renderer.ReducedMotion = true;
                var input = new PollingGamepadInput();
                // Match Program's background composition, rather than manually calling Step.
                using var entry = await Task.Run(() =>
                {
                    AssertFalse(Dispatcher.UIThread.CheckAccess());
                    return new GamepadEntrySession(fixture.Shell, fixture.Intents, fixture.Store,
                        new(), input, foreground: () => true, immersive: _ => { });
                });
                long deadline = Environment.TickCount64 + 2000;
                while (input.Reads < 3 && Environment.TickCount64 < deadline)
                    await Task.Delay(20, timeout.Token);
                AssertTrue(input.Reads >= 3);
                AssertEqual(0, input.OffThreadReads);
                AssertTrue(entry.IsPromptOpen);
                AssertFalse(entry.IsEntered);
                AssertFalse(input.Disposed);

                entry.Dispose();
                AssertFalse(entry.IsPromptOpen);
                AssertTrue(input.Disposed);
                int reads = input.Reads;
                await Task.Delay(100, timeout.Token);
                AssertEqual(reads, input.Reads);
                Console.WriteLine($"PASS: background-constructed gamepad session polled {reads} times on UI thread; activity prompted; disposal stopped polling");
            }, timeout.Token);
            return 0;
        });

    private sealed class PollingGamepadInput : IGamepadInput
    {
        internal int Reads, OffThreadReads;
        internal bool Disposed;

        public IReadOnlyList<GamepadSample> Read()
        {
            Interlocked.Increment(ref Reads);
            if (!Dispatcher.UIThread.CheckAccess()) Interlocked.Increment(ref OffThreadReads);
            return [new("polling-test", GamepadLayout.OffsetLeftHigh, Activity: true)];
        }

        public void Dispose() => Disposed = true;
    }
}
