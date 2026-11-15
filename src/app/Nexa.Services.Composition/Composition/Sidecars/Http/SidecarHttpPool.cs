using System.Security.Cryptography;
using System.Text;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>Bounded native HTTP exchanges; guest policy never enters this transport.</summary>
public sealed class SidecarHttpPool : IAsyncDisposable
{
    public const int ChunkLimit = 60 * 1024, BodyLimit = 16 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<(Guid, long), Entry> _entries = [];
    private readonly HttpClient _client;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Timer _expiry;
    private Task? _disposal;
    private long _reserved;
    private Exception? _cleanupFailure;
    private sealed class Entry(Guid owner, HostHttpBegin begin, Func<bool> active, DateTimeOffset now) : IDisposable
    {
        internal readonly Guid Owner = owner;
        internal readonly HostHttpBegin Begin = begin;
        internal readonly Func<bool> Active = active;
        internal readonly byte[] Body = new byte[Math.Max(0, begin.BodyLength)];
        internal readonly SemaphoreSlim Serial = new(1, 1);
        internal readonly CancellationTokenSource Stop = new();
        internal DateTimeOffset Used = now;
        internal int Uploaded;
        internal bool Sent;
        internal long Read;
        internal HttpRequestMessage? Request;
        internal HttpResponseMessage? Response;
        internal Stream? Stream;
        internal Task? Retirement;
        public void Dispose() { Stop.Dispose(); Serial.Dispose(); }
    }
    /// <param name="client">An owned client using committed Host policy, with redirects and cookies disabled.</param>
    public SidecarHttpPool(HttpClient client, Func<DateTimeOffset>? clock = null)
    {
        _client = client; _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _expiry = new(_ => Sweep(), null, Timeout.Infinite, Timeout.Infinite);
    }
    public void Begin(Guid owner, HostHttpBegin input, Func<bool> active)
    {
        if (owner == Guid.Empty || input.Id <= 0 || input.BodyLength is < -1 or > BodyLimit
            || input.Method is not ("GET" or "HEAD" or "POST" or "PUT" or "PATCH" or "DELETE" or "OPTIONS")
            || input.Uri is null || input.Uri.Length > 8192 || input.Uri.Any(char.IsControl) || !Uri.TryCreate(input.Uri, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Invalid HTTP request.");
        var headers = CopyHeaders(input.Headers);
        // Validate HTTP header classification before reserving memory or starting transport work.
        using (var request = Request(input with { Headers = headers }, [])) { }
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            if (!active()) throw new InvalidOperationException("HTTP session retired.");
            long length = Math.Max(0, input.BodyLength);
            if (_entries.ContainsKey((owner, input.Id)) || _entries.Count >= 64 || _reserved + length > 128L * 1024 * 1024
                || _entries.Values.Count(e => e.Owner == owner) >= 32
                || _entries.Values.Where(e => e.Owner == owner).Sum(e => (long)e.Body.Length) + length > 64L * 1024 * 1024)
                throw new InvalidOperationException("HTTP resource budget exceeded.");
            _entries.Add((owner, input.Id), new(owner, input with { Headers = headers }, active, _clock()));
            _reserved += length; _expiry.Change(1000, 1000);
        }
    }
    public async ValueTask AppendAsync(Guid owner, HostHttpAppend input, CancellationToken token)
    {
        if (input.Bytes is null || input.Bytes.Length is < 1 or > ChunkLimit) throw new InvalidDataException("Invalid HTTP chunk.");
        byte[] copy = input.Bytes.ToArray();
        try
        {
            await Use(owner, input.Id, async (entry, _) =>
            {
                if (entry.Sent || input.Offset != entry.Uploaded || copy.Length > entry.Body.Length - entry.Uploaded)
                    throw new InvalidDataException("Invalid HTTP upload sequence.");
                copy.CopyTo(entry.Body, entry.Uploaded); entry.Uploaded += copy.Length;
                await ValueTask.CompletedTask; return true;
            }, token).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(copy); }
    }
    public ValueTask<HostHttpResponse> SendAsync(Guid owner, HostHttpRef input, CancellationToken token)
        => Use<HostHttpResponse>(owner, input.Id, async (entry, stop) =>
        {
            if (entry.Sent || entry.Uploaded != entry.Body.Length) throw new InvalidDataException("HTTP upload is incomplete or already sent.");
            entry.Sent = true; // Sending is an effect, including when its reply is lost.
            entry.Request = Request(entry.Begin, entry.Body);
            entry.Response = await _client.SendAsync(entry.Request, HttpCompletionOption.ResponseHeadersRead, stop).ConfigureAwait(false);
            var headers = CopyHeaders(entry.Response.Headers.Concat(entry.Response.Content.Headers)
                .Select(pair => new HostHttpHeader(pair.Key, pair.Value.ToArray())).ToArray());
            entry.Stream = await entry.Response.Content.ReadAsStreamAsync(stop).ConfigureAwait(false);
            return new((int)entry.Response.StatusCode, headers);
        }, token, retireOnFailure: true);
    public ValueTask<HostHttpChunk> ReadAsync(Guid owner, HostHttpRead input, CancellationToken token)
        => Use<HostHttpChunk>(owner, input.Id, async (entry, stop) =>
        {
            if (input.Count is < 1 or > ChunkLimit || input.Offset != entry.Read || entry.Stream is null)
                throw new InvalidDataException("Invalid HTTP response sequence.");
            byte[] bytes = new byte[input.Count];
            try
            {
                int count = await entry.Stream.ReadAsync(bytes, stop).ConfigureAwait(false);
                entry.Read = checked(entry.Read + count);
                return new(input.Offset, bytes.AsSpan(0, count).ToArray());
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }, token, retireOnFailure: true);
    public async ValueTask ReleaseAsync(Guid owner, HostHttpRef input, CancellationToken token)
    {
        if (input.Id <= 0) throw new InvalidDataException("Invalid HTTP resource.");
        Task retirement;
        lock (_gate) retirement = _entries.TryGetValue((owner, input.Id), out var entry) ? Retire(entry) : Task.CompletedTask;
        await retirement.WaitAsync(token).ConfigureAwait(false);
    }
    private async ValueTask<T> Use<T>(Guid owner, long id, Func<Entry, CancellationToken, ValueTask<T>> operation, CancellationToken token, bool retireOnFailure = false)
    {
        Entry entry; CancellationTokenSource stop;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            if (!_entries.TryGetValue((owner, id), out entry!) || entry.Retirement is not null || !entry.Active())
                throw new InvalidOperationException("HTTP resource unavailable.");
            stop = CancellationTokenSource.CreateLinkedTokenSource(token, entry.Stop.Token);
        }
        using (stop)
        {
            await entry.Serial.WaitAsync(stop.Token).ConfigureAwait(false);
            try
            {
                stop.Token.ThrowIfCancellationRequested();
                if (!entry.Active()) throw new InvalidOperationException("HTTP session retired.");
                var result = await operation(entry, stop.Token).ConfigureAwait(false);
                stop.Token.ThrowIfCancellationRequested();
                lock (_gate) entry.Used = _clock();
                return result;
            }
            catch
            {
                if (retireOnFailure) { lock (_gate) _ = Retire(entry); }
                throw;
            }
            finally { entry.Serial.Release(); }
        }
    }
    private static HttpRequestMessage Request(HostHttpBegin begin, byte[] body)
    {
        var request = new HttpRequestMessage(new HttpMethod(begin.Method), begin.Uri);
        try
        {
            if (begin.BodyLength >= 0) request.Content = new ByteArrayContent(body);
            foreach (var header in begin.Headers)
            {
                if (header.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                    || header.Name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("HTTP body framing is transport-owned.");
                if (!request.Headers.TryAddWithoutValidation(header.Name, header.Values)
                    && (request.Content is null || !request.Content.Headers.TryAddWithoutValidation(header.Name, header.Values)))
                    throw new InvalidDataException("Invalid HTTP header classification.");
            }
            return request;
        }
        catch { request.Dispose(); throw; }
    }
    private static HostHttpHeader[] CopyHeaders(HostHttpHeader[] headers)
    {
        if (headers is null || headers.Length > 128) throw new InvalidDataException("HTTP header budget exceeded.");
        List<HostHttpHeader> copy = []; long bytes = 0; HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (header is null || header.Name is null || header.Name.Length is < 1 or > 256
                || !header.Name.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c))
                || !names.Add(header.Name) || header.Values is null || header.Values.Length is < 1 or > 64)
                throw new InvalidDataException("Invalid HTTP headers.");
            var values = header.Values.ToArray(); bytes += header.Name.Length;
            foreach (string value in values)
            {
                if (value is null || value.Length > 8192 || value.Any(c => char.IsControl(c) && c != '\t'))
                    throw new InvalidDataException("Invalid HTTP header value.");
                bytes += Encoding.UTF8.GetByteCount(value);
            }
            if (bytes > 24 * 1024) throw new InvalidDataException("HTTP header budget exceeded.");
            copy.Add(new(header.Name, values));
        }
        return copy.ToArray();
    }
    /// <summary>Also callable by deterministic lifetime tests; ordinary expiry runs without UI frames.</summary>
    public void Sweep()
    {
        lock (_gate)
            foreach (var entry in _entries.Values.ToArray())
                if (!entry.Active() || _clock() - entry.Used > TimeSpan.FromMinutes(2)) _ = Retire(entry);
    }
    private Task Retire(Entry entry) => entry.Retirement ??= Task.Run(async () =>
    {
        try { await entry.Stop.CancelAsync().ConfigureAwait(false); }
        catch (AggregateException) { /* Cancellation is signalled; resource disposal must still drain. */ }
        await entry.Serial.WaitAsync().ConfigureAwait(false);
        try
        {
            try { entry.Stream?.Dispose(); }
            finally { try { entry.Response?.Dispose(); } finally { entry.Request?.Dispose(); } }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { Interlocked.CompareExchange(ref _cleanupFailure, error, null); }
        finally
        {
            CryptographicOperations.ZeroMemory(entry.Body); entry.Serial.Release(); entry.Dispose();
            lock (_gate)
            {
                _entries.Remove((entry.Owner, entry.Begin.Id)); _reserved -= entry.Body.Length;
                if (_entries.Count == 0 && _disposal is null) _expiry.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    });
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is null)
            {
                _expiry.Dispose(); var pending = _entries.Values.Select(Retire).ToArray();
                _disposal = DisposeCore(pending);
            }
            return new(_disposal);
        }
    }
    private async Task DisposeCore(Task[] pending)
    {
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        finally { _client.Dispose(); }
        if (_cleanupFailure is { } failure) throw new AggregateException("HTTP cleanup failed.", failure);
    }
}
