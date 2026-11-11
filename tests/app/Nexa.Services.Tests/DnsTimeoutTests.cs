using System.Net;
using Nexa.Services.Network;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask DnsTimeoutCoversResponseBody()
    {
        foreach (bool cancelCaller in new[] { false, true })
        {
            int fallbacks = 0;
            using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var client = new HttpClient(new NetworkDnsTestHandler(_ => new(HttpStatusCode.OK)
            { Content = new StreamContent(new StalledDnsBody(entered)) }))
            { Timeout = cancelCaller ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(100) };
            using var resolver = new DnsOverHttpsResolver(client, false,
                (_, token) => { token.ThrowIfCancellationRequested(); fallbacks++; return Task.FromResult(new[] { IPAddress.Loopback }); });
            var pending = resolver.ResolveAsync("stalled.test", caller.Token);
            await entered.Task.WaitAsync(caller.Token);
            if (cancelCaller)
            {
                caller.Cancel();
                bool canceled = false;
                try { await pending; } catch (OperationCanceledException) { canceled = true; }
                AssertTrue(canceled); AssertEqual(0, fallbacks);
            }
            else
            {
                AssertEqual(IPAddress.Loopback, (await pending)[0]);
                AssertFalse(caller.IsCancellationRequested); AssertEqual(1, fallbacks);
            }
        }
    }

    private sealed class StalledDnsBody(TaskCompletionSource entered) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
