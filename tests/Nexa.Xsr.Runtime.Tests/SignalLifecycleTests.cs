using Nexa.Sidecar.Protocol;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Runtime.Tests;

internal static partial class Program
{
    private static async ValueTask SignalDeactivationWaitsForInFlightFlushAndPreservesControlOrder()
    {
        var runtime = SignalRuntime();
        var point = runtime.Resolve(SignalIntent);
        SignalFlushBarrier? barrier = null;
        var (session, peer) = await SignalSession(runtime,
            [SignalItem("signal.catch", SidecarRegistrationKind.IntentCatch, SignalIntent, true)],
            inner => barrier = new(inner));
        using (session) using (peer)
        {
            await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            barrier!.Block = true;
            try
            {
                AssertTrue(runtime.Emit(point, "first"));
                var first = await ReceiveSignal(peer);
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                AssertTrue(runtime.Emit(point, "queued"));
                Task deactivation = session.DeactivateAsync().AsTask();
                AssertFalse(runtime.Emit(point, "retired"));
                AssertFalse(deactivation.IsCompleted);
                AssertFalse(barrier.Cancelled.Task.IsCompleted);
                barrier.Block = false;
                barrier.Release.TrySetResult();
                await deactivation.WaitAsync(TimeSpan.FromSeconds(5));
                AssertEqual(SidecarMessageType.Deactivate, (await DataPlaneReceiveAsync(peer)).MessageType);
                AssertEqual(SidecarSessionState.Ready, session.State);

                await session.ActivateAsync();
                AssertEqual(SidecarMessageType.Activate, (await DataPlaneReceiveAsync(peer)).MessageType);
                AssertTrue(runtime.Emit(point, "next"));
                var next = await ReceiveSignal(peer);
                AssertEqual("next", next.Value);
                AssertEqual(1UL, next.Sequence);
                AssertTrue(first.ActivationId != next.ActivationId);
            }
            finally { barrier.Release.TrySetResult(); }
        }
    }

    private static async ValueTask SignalDeactivationDeadlineCancelsInFlightFlush()
    {
        var runtime = SignalRuntime();
        var point = runtime.Resolve(SignalIntent);
        SignalFlushBarrier? barrier = null;
        var (session, peer) = await SignalSession(runtime,
            [SignalItem("signal.catch", SidecarRegistrationKind.IntentCatch, SignalIntent, true)],
            inner => barrier = new(inner));
        using (session) using (peer)
        {
            await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            barrier!.Block = true;
            try
            {
                AssertTrue(runtime.Emit(point, "first")); await ReceiveSignal(peer);
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                using var deadline = new CancellationTokenSource();
                Task deactivation = session.DeactivateAsync(deadline.Token).AsTask();
                AssertFalse(runtime.Emit(point, "retired"));
                AssertFalse(deactivation.IsCompleted);
                deadline.Cancel();
                await AssertThrowsAsync<SidecarProtocolException>(() => deactivation.WaitAsync(TimeSpan.FromSeconds(5)));
                await barrier.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                AssertEqual(SidecarSessionState.Failed, session.State);
                AssertFalse(runtime.Emit(point, "failed"));
            }
            finally { barrier.Release.TrySetResult(); }
        }
    }

    private static async ValueTask SignalShutdownDrainAbortsImmediatelyOnDisposal()
    {
        var runtime = SignalRuntime();
        var point = runtime.Resolve(SignalIntent);
        var (store, captions, state) = CaptionRuntime();
        int captionIndex = captions.Resolve(CaptionTarget);
        SignalFlushBarrier? barrier = null;
        var (session, peer) = await PatchSession(new(CaptionPoint),
            [SignalItem("signal.catch", SidecarRegistrationKind.IntentCatch, SignalIntent, true),
             CaptionItem("caption.active", "Active")],
            signals: new(runtime, SignalIntent), uiPatches: new(captions, CaptionTarget),
            wrap: inner => barrier = new(inner), limits: new() { ShutdownTimeout = TimeSpan.FromSeconds(30) });
        using (session) using (peer)
        {
            await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            AssertEqual("Active", store.Read<XsrUiPatchSnapshot>(state).Value!.CaptionAt(captionIndex));
            barrier!.Block = true;
            try
            {
                AssertTrue(runtime.Emit(point, "first")); await ReceiveSignal(peer);
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Task shutdown = session.ShutdownAsync().AsTask();
                // Caption retirement follows signal detachment and gives a passive observation
                // that shutdown has entered its drain, without adding signals to that queue.
                await WaitUntil(() => store.Read<XsrUiPatchSnapshot>(state).Value!.CaptionAt(captionIndex) is null);
                AssertFalse(runtime.Emit(point, "retired"));
                AssertFalse(shutdown.IsCompleted);
                AssertFalse(barrier.Cancelled.Task.IsCompleted);
                session.Dispose();
                await barrier.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
                AssertEqual(SidecarSessionState.Closed, session.State);
            }
            finally { barrier.Release.TrySetResult(); }
        }
    }

    private sealed class SignalFlushBarrier(Stream inner) : Stream
    {
        internal bool Block;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => inner.CanRead;
        public override bool CanWrite => inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await inner.FlushAsync(cancellationToken);
            if (!Block) return;
            Entered.TrySetResult();
            try { await Release.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
