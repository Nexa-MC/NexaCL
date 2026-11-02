using Nexa.Native.Window.Wire;

namespace Nexa.Native.Ui.Tests;

internal static partial class Program
{
    private sealed partial class WindowOwner
    {
        private readonly Dictionary<long, Func<WindowInputOperation, byte[]>> _inputCallbacks = [];
        private int _shutdownPolls;

        private byte[] ReceiveInputLease(ReadOnlyMemory<byte> message)
        {
            True(ReferenceEquals(SynchronizationContext.Current, _context));
            using var reader = WindowControlProtocol.Read(message, out int kind, callback: true);
            Equal((int)WindowControlCallback.InputLease, kind);
            var operation = (WindowInputOperation)reader.Int32(); long callback = reader.Int64(); reader.End();
            return _inputCallbacks[callback](operation);
        }

        private static byte[] PollReply(long ticks, bool trailing = false)
        {
            using var writer = WindowControlProtocol.Write((int)WindowControlCallback.InputLease, callback: true);
            writer.Int64(ticks); if (trailing) writer.Byte(1); return writer.Finish();
        }

        private Task<byte[]> ReleaseInputLease(long handle)
        {
            using var writer = WindowControlProtocol.Write((int)WindowCommand.ReleaseInputLease);
            writer.Int64(handle); return _operations.InvokeAsync(writer.Finish());
        }

        private async Task<long> CreateInputLease(WindowInputOperation operation, long callback, long ticks = 0,
            bool trailing = false, Action<long>? created = null)
        {
            long handle = 0;
            await _context.RunAsync(() =>
            {
                using var writer = WindowControlProtocol.Write((int)WindowCommand.InputLease);
                writer.Int32((int)operation);
                if (operation != WindowInputOperation.CancelDoubleClick) writer.Int64(callback);
                if (operation == WindowInputOperation.Poll) writer.Int64(ticks);
                if (trailing) writer.Byte(1);
                using var response = WindowControlProtocol.Read(_ui.Exchange(WindowControlProtocol.Prefix(
                    (byte)WindowUiCommand.InputLease, writer.Finish())), out int kind);
                Equal((int)WindowCommand.InputLease, kind);
                if (operation != WindowInputOperation.CancelDoubleClick) { handle = response.Int64(); True(handle > 0); }
                response.End(); created?.Invoke(handle); return Task.CompletedTask;
            });
            return handle;
        }

        private async Task VerifyInputLeases()
        {
            var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int count = 0;
            _inputCallbacks[1] = operation =>
            {
                Equal(WindowInputOperation.Poll, operation);
                // Reenter the native UI from a Host callback without transferring its delegate.
                using var command = WindowControlProtocol.Write((int)WindowCommand.Platform);
                command.Int32((int)WindowPlatformCommand.ControllerInput);
                _ui.Exchange(WindowControlProtocol.Prefix((byte)WindowUiCommand.Platform, command.Finish()));
                if (Interlocked.Increment(ref count) >= 3) polled.TrySetResult();
                return PollReply(TimeSpan.FromMilliseconds(10).Ticks);
            };
            long handle = await CreateInputLease(WindowInputOperation.Poll, 1, TimeSpan.FromMilliseconds(5).Ticks);
            await polled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await ReleaseInputLease(handle).WaitAsync(TimeSpan.FromSeconds(10));
            int stoppedCount = Volatile.Read(ref count);
            await Task.Delay(60); Equal(stoppedCount, Volatile.Read(ref count));
            await ReleaseInputLease(handle); // Repeated and unknown releases are idempotent.
            await ReleaseInputLease(long.MaxValue);

            var selfReleased = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            long selfHandle = 0; int selfCount = 0;
            _inputCallbacks[2] = _ =>
            {
                Interlocked.Increment(ref selfCount);
                Task release = ReleaseInputLease(selfHandle);
                True(!release.IsCompleted); // Native disposal must wait for this callback to return.
                selfReleased.TrySetResult(release);
                return PollReply(TimeSpan.FromMilliseconds(20).Ticks);
            };
            // Assign the handle on the owner thread before the dispatcher can run its first tick.
            await CreateInputLease(WindowInputOperation.Poll, 2, TimeSpan.FromMilliseconds(20).Ticks,
                created: value => selfHandle = value);
            await (await selfReleased.Task.WaitAsync(TimeSpan.FromSeconds(10))).WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(60); Equal(1, Volatile.Read(ref selfCount));

            foreach (long invalid in new[] { 0L, -1L, TimeSpan.TicksPerDay + 1 })
                await RejectInput(() => CreateInputLease(WindowInputOperation.Poll, 1, invalid));
            await RejectInput(() => CreateInputLease(WindowInputOperation.Poll, 0, 1));
            await RejectInput(() => CreateInputLease(WindowInputOperation.Poll, 1, 1, trailing: true));

            // Bad callback replies and Host exceptions retire the native timer after its first tick.
            foreach (int fault in new[] { 0, 1, 2 })
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                int failedPolls = 0;
                _inputCallbacks[3] = _ =>
                {
                    Interlocked.Increment(ref failedPolls); entered.TrySetResult();
                    return fault switch
                    {
                        0 => PollReply(0),
                        1 => PollReply(TimeSpan.TicksPerMillisecond, trailing: true),
                        _ => throw new IOException("Host callback failed"),
                    };
                };
                long failed = await CreateInputLease(WindowInputOperation.Poll, 3, TimeSpan.FromMilliseconds(5).Ticks);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                // Queue a barrier on the same dispatcher so native rejection has completed first.
                await _context.RunAsync(() => Task.CompletedTask);
                await Task.Delay(60); Equal(1, Volatile.Read(ref failedPolls));
                await ReleaseInputLease(failed);
            }

            long firstClick = await CreateInputLease(WindowInputOperation.DoubleClick, 4);
            long nextClick = await CreateInputLease(WindowInputOperation.DoubleClick, 5);
            True(nextClick > firstClick); await ReleaseInputLease(firstClick);
            await CreateInputLease(WindowInputOperation.CancelDoubleClick, 0);
            await ReleaseInputLease(nextClick);

            List<long> leases = [];
            for (int i = 0; i < 32; i++) leases.Add(await CreateInputLease(WindowInputOperation.Poll, 1, TimeSpan.TicksPerDay));
            await RejectInput(() => CreateInputLease(WindowInputOperation.Poll, 1, TimeSpan.TicksPerDay));
            foreach (long lease in leases) await ReleaseInputLease(lease);

            var shutdownPolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _inputCallbacks[6] = _ =>
            {
                Interlocked.Increment(ref _shutdownPolls);
                shutdownPolled.TrySetResult();
                return PollReply(TimeSpan.FromMilliseconds(5).Ticks);
            };
            await CreateInputLease(WindowInputOperation.Poll, 6, TimeSpan.FromMilliseconds(5).Ticks);
            await shutdownPolled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Console.WriteLine("PASS: native input leases preserve Host context, reentry, self-release, callback fault isolation and bounded admission");
        }

        private static async Task RejectInput(Func<Task<long>> action)
        {
            try { await action(); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("Malformed or over-budget native input lease was accepted.");
        }

        private async Task VerifyInputLeasesStopped()
        {
            int count = Volatile.Read(ref _shutdownPolls); True(count > 0);
            _inputCallbacks.Clear();
            await Task.Delay(60); Equal(count, Volatile.Read(ref _shutdownPolls));
            Console.WriteLine("PASS: UI shutdown drains native input subscriptions before Host callback retirement");
        }
    }
}
