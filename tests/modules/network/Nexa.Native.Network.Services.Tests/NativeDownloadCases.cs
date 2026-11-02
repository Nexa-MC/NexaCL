using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Nexa.Native.Network.Client;
using Nexa.Native.Network.Services;
using Nexa.Services.Downloads;
using Nexa.Services.Logging;
using Nexa.Services.Network;
using Nexa.Services.Scheduling;
using Nexa.Xsr.State;

internal static class NativeDownloadCases
{
    private static readonly NetworkPreferences Policy = new() { ProxyMode = 0, DnsOverHttps = false, IpStack = "ipv4" };
    internal static async Task RunAsync(NativeNetworkClient network, CancellationToken token)
    {
        string directory = Path.Combine(Path.GetTempPath(), "nexa-native-下载-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var work = new CountingWork();
        XsrStateStoreBuilder builder = new(); NativeNetworkClient.DeclareDownloadState(builder); var state = builder.Build();
        network.AttachDownloads(work, state, logging: work, operation: (_, _, _) => work.OperationObserved.TrySetResult());
        await using var server = new FilesServer(token);
        NativeDownloadSource Source(string path) => new(server.Address(path), Policy);
        NativeDownloadRequest Request(string path, params string[] sources) => new(sources.Select(Source).ToArray(),
            Path.Combine(directory, path), ExpectedSize: FilesServer.Size, Priority: WorkPriority.Critical);
        try
        {
            var resumed = Request("resumed.bin", "/fail", "/data");
            await File.WriteAllBytesAsync(resumed.DestinationPath + resumed.TemporaryExtension, FilesServer.Payload[..65536], token);
            var resumeResult = await network.DownloadAsync(resumed, token: token);
            Check(resumeResult.Success && resumeResult.Errors.Count == 1 && resumeResult.Errors[0].Failure is HttpRequestException { StatusCode: HttpStatusCode.NotFound }, "Source failure did not retain its HTTP category.");
            Check(server.DataRanges.Contains(65536) && File.ReadAllBytes(resumed.DestinationPath).AsSpan().SequenceEqual(FilesServer.Payload), "Native resume/failover changed bytes.");

            var restart = Request("restart.bin", "/data", "/data");
            await File.WriteAllBytesAsync(restart.DestinationPath + restart.TemporaryExtension, new byte[FilesServer.Size + 1], token);
            Check((await network.DownloadAsync(restart, token: token)).Success, "Native 416 did not restart.");
            Check(File.ReadAllBytes(restart.DestinationPath).AsSpan().SequenceEqual(FilesServer.Payload), "Native 416 restart retained invalid prefix.");

            var first = Request("shared.bin", "/hold"); using var firstStop = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task<DownloadTransferResult> canceled = network.DownloadAsync(first, token: firstStop.Token);
            await server.HoldStarted.Task.WaitAsync(token);
            TaskCompletionSource joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<DownloadTransferResult> kept = network.DownloadAsync(first, _ => joined.TrySetResult(), token);
            await joined.Task.WaitAsync(token); firstStop.Cancel();
            try { await canceled; throw new InvalidDataException("A canceled waiter completed successfully."); } catch (OperationCanceledException) when (firstStop.IsCancellationRequested) { }
            server.ReleaseHold.TrySetResult(); Check((await kept).Success && server.HoldRequests == 1, "Independent waiter cancellation broke destination coalescing.");

            string digest = Convert.ToHexString(SHA256.HashData(FilesServer.Payload));
            int beforeCache = server.CacheRequests;
            var cached = Request("cache-one.bin", "/cache") with { Digest = digest };
            Check((await network.DownloadAsync(cached, token: token)).Success, "Native verified cache fill failed.");
            Check((await network.DownloadAsync(cached with { DestinationPath = Path.Combine(directory, "cache-two.bin") }, token: token)).Success
                && server.CacheRequests == beforeCache + 1, "Native cache did not reuse a verified artifact.");
            Check(File.ReadAllBytes(Path.Combine(directory, "cache-two.bin")).AsSpan().SequenceEqual(FilesServer.Payload), "Native cache materialization changed bytes.");

            var bad = Request("length-rejected.bin", "/oversized");
            bad = bad with { Sources = [bad.Sources[0] with { ExactLength = FilesServer.Size }] };
            Check(!(await network.DownloadAsync(bad, token: token)).Success && !File.Exists(bad.DestinationPath), "An oversized source was committed.");
            var restricted = Request("host-rejected.bin", "/data");
            restricted = restricted with { Sources = [restricted.Sources[0] with { ResourceHostsOnly = true }] };
            Check(!(await network.DownloadAsync(restricted, token: token)).Success && !File.Exists(restricted.DestinationPath), "An unadmitted resource host was committed.");

            var transfers = state.ReadCollection<DownloadTransferView>(state.Resolve(DownloadStateContract.TransfersKey), token);
            Check(transfers.Items.Count == 0 && transfers.Revision > 0, "Terminal native transfer state did not drain before completion.");
            Check(work.Active == 0 && work.Maximum <= 2 && work.FeedbackBytes > 0 && work.Priorities.Contains(WorkPriority.Critical),
                "Native downloads bypassed canonical host work/feedback or retained a lease.");
            await work.OperationObserved.Task.WaitAsync(token);
            Check(work.LogCount > 0, "Native downloads lost their log mirrors.");
            Console.WriteLine("Native downloads: resume/failover, 416, independent cancellation/deduplication, verified cache, length/host admission, state replicas and canonical work/feedback passed.");
        }
        finally { server.ReleaseHold.TrySetResult(); Directory.Delete(directory, recursive: true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }

    private sealed class CountingWork : IWorkScheduler, IAsyncWorkResourceFeedback, ILogWriter, IDisposable
    {
        private readonly SemaphoreSlim _budget = new(2, 2);
        internal ConcurrentBag<WorkPriority> Priorities { get; } = [];
        internal int Active, Maximum;
        internal long FeedbackBytes;
        internal int LogCount;
        internal TaskCompletionSource OperationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Write(LogLevel level, string subsystem, string message, string? exceptionText = null) => Interlocked.Increment(ref LogCount);
        public WorkPriority CurrentPriority => WorkPriority.Interactive;
        public IDisposable UsePriority(WorkPriority priority) => new Lease(() => { });
        public IWorkQuietLease EnterQuiet() => throw new NotSupportedException();
        public async ValueTask<IDisposable> AcquireAsync(WorkPriority priority, WorkResource resource, CancellationToken token = default)
        {
            await _budget.WaitAsync(token); Priorities.Add(priority);
            int active = Interlocked.Increment(ref Active); Maximum = Math.Max(Maximum, active);
            return new Lease(() => { Interlocked.Decrement(ref Active); _budget.Release(); });
        }
        public async ValueTask ObserveDiskWriteAsync(int bytes, TimeSpan elapsed, CancellationToken token = default)
        {
            Check(Active > 0 && elapsed >= TimeSpan.Zero, "Disk feedback escaped its host lease.");
            await Task.Yield(); token.ThrowIfCancellationRequested(); Interlocked.Add(ref FeedbackBytes, bytes);
        }
        public void Dispose() { Check(Active == 0, "Canonical host work leaked."); _budget.Dispose(); }
        private sealed class Lease(Action release) : IDisposable
        { private Action? _release = release; public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke(); }
    }

    private sealed class FilesServer : IAsyncDisposable
    {
        internal const int Size = 1024 * 1024;
        internal static readonly byte[] Payload = Enumerable.Range(0, Size).Select(index => (byte)(index % 251)).ToArray();
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop;
        private readonly Task _accepting;
        private readonly ConcurrentBag<Task> _requests = [];
        internal readonly ConcurrentBag<long> Ranges = [];
        internal readonly ConcurrentBag<long> DataRanges = [];
        internal TaskCompletionSource HoldStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseHold { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int HoldRequests, CacheRequests;
        internal FilesServer(CancellationToken token)
        { _stop = CancellationTokenSource.CreateLinkedTokenSource(token); _listener.Start(); _accepting = AcceptAsync(); }
        internal string Address(string path) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}" + path;
        private async Task AcceptAsync()
        {
            try { while (true) _requests.Add(ServeAsync(await _listener.AcceptTcpClientAsync(_stop.Token))); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }
        private async Task ServeAsync(TcpClient peer)
        {
            using (peer)
            {
                try
                {
                    await using var stream = peer.GetStream(); using var collected = new MemoryStream(); byte[] bytes = new byte[16384];
                    while (collected.GetBuffer().AsSpan(0, (int)collected.Length).IndexOf("\r\n\r\n"u8) < 0)
                    {
                        int count = await stream.ReadAsync(bytes, _stop.Token); if (count == 0) return;
                        collected.Write(bytes, 0, count); Check(collected.Length <= 32768, "Fixture header budget exceeded.");
                    }
                    string[] lines = Encoding.ASCII.GetString(collected.GetBuffer(), 0, (int)collected.Length).Split("\r\n");
                    string path = lines[0].Split(' ')[1];
                    string? range = lines.FirstOrDefault(line => line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase));
                    int offset = range is null ? 0 : int.Parse(range[13..].Split('-')[0], System.Globalization.CultureInfo.InvariantCulture); Ranges.Add(offset);
                    if (path == "/data") DataRanges.Add(offset);
                    if (path == "/cache") Interlocked.Increment(ref CacheRequests);
                    if (path == "/fail" || offset >= Size)
                    {
                        string error = path == "/fail" ? "404 Not Found" : "416 Range Not Satisfiable";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 " + error + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), _stop.Token); return;
                    }
                    string status = offset == 0 ? "200 OK" : "206 Partial Content";
                    int length = Size - offset + (path == "/oversized" ? 1 : 0);
                    string headers = "HTTP/1.1 " + status + "\r\nContent-Length: " + length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n";
                    if (offset > 0) headers += $"Content-Range: bytes {offset}-{Size - 1}/{Size}\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"), _stop.Token);
                    if (path == "/hold") { Interlocked.Increment(ref HoldRequests); HoldStarted.TrySetResult(); await ReleaseHold.Task.WaitAsync(_stop.Token); }
                    await stream.WriteAsync(Payload.AsMemory(offset), _stop.Token);
                    if (path == "/oversized") await stream.WriteAsync(new byte[1], _stop.Token);
                }
                catch (IOException) { }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            }
        }
        public async ValueTask DisposeAsync()
        { _stop.Cancel(); await _accepting; _listener.Stop(); try { await Task.WhenAll(_requests); } finally { _stop.Dispose(); } }
    }
}
