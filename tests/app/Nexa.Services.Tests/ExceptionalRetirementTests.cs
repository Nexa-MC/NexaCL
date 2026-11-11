using System.Net;
using Nexa.Services.Caching;
using Nexa.Services.Network;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask NetworkResponseCleanupFailureReleasesTransport()
    {
        foreach (bool materializeStream in new[] { false, true })
        {
            var transport = new CleanupFailureTransport();
            using var pool = new NetworkHttpClientPool(() => new() { DnsOverHttps = false }, (_, _) => transport);
            using var client = pool.CreateClient();
            var response = await client.GetAsync("https://example.test/body", HttpCompletionOption.ResponseHeadersRead);
            if (materializeStream) _ = await response.Content.ReadAsStreamAsync();
            pool.Dispose();
            AssertEqual(0, transport.Disposals);
            bool failed = false;
            try { response.Dispose(); } catch (IOException) { failed = true; }
            AssertTrue(failed);
            AssertEqual(1, transport.Disposals);
            response.Dispose();
            AssertEqual(1, transport.Disposals);
        }
    }

    private sealed class CleanupFailureTransport : HttpMessageHandler
    {
        internal int Disposals { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CleanupFailureStream()) });
        protected override void Dispose(bool disposing)
        { if (disposing) Disposals++; base.Dispose(disposing); }
    }

    private sealed class CleanupFailureStream : MemoryStream
    {
        protected override void Dispose(bool disposing)
        { base.Dispose(disposing); if (disposing) throw new IOException("response cleanup fixture"); }
    }

    private static async ValueTask SharedCacheCancellationFailureDrainsProducers()
    {
        var cache = new SharedStateCache();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var key = new StateCacheKey("retirement", "callback-failure", "1");
        var policy = new StateCachePolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), 1);
        Task<string> producer = cache.GetOrCreateAsync(key, policy, async token =>
        {
            using var fault = token.Register(static () => throw new InvalidOperationException("cache cancellation fixture"));
            await release.Task;
            token.ThrowIfCancellationRequested();
            return "unreachable";
        }).AsTask();
        Task? retirement = null;
        try
        {
            cache.Invalidate(key);
            retirement = cache.DisposeAsync().AsTask();
            AssertFalse(retirement.IsCompleted);
            Task overlapping = cache.DisposeAsync().AsTask();
            AssertFalse(overlapping.IsCompleted);
            release.TrySetResult();
            await TrustRejectsAsync<OperationCanceledException>(() => producer.WaitAsync(deadline.Token));
            await TrustRejectsAsync<AggregateException>(() => retirement.WaitAsync(deadline.Token));
            await overlapping.WaitAsync(deadline.Token);
            await cache.DisposeAsync();
        }
        finally
        {
            release.TrySetResult();
            try { await producer; } catch (OperationCanceledException) { }
            if (retirement is not null)
                try { await retirement; } catch (AggregateException) { }
            await cache.DisposeAsync();
        }
    }
}
