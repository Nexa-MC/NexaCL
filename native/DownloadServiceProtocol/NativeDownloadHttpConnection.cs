using System.Net;
using System.Net.Http.Headers;
using Nexa.Services.Downloads;

namespace Nexa.Native.Network.Services;

internal sealed class NativeDownloadHttpConnection(NativeDownloadSource source,
    Func<HttpRequestMessage, NativeDownloadSource, bool, CancellationToken, Task<HttpResponseMessage>> send)
    : IDownloadConnection, IAsyncDisposable
{
    private HttpResponseMessage? _response;
    private Stream? _stream;
    private long _received;

    public async ValueTask<DownloadConnectionInfo> StartAsync(long beginOffset, CancellationToken token = default)
    {
        Uri current = new(source.Address);
        if (source.MaximumLength < 0 || source.ExactLength < 0) throw new InvalidDataException("Invalid source length admission.");
        for (int redirect = 0; ; redirect++)
        {
            if (source.ResourceHostsOnly && !ResourceAllowed(current, source.MainlandChina))
                throw new InvalidDataException("Resource download address is outside its admitted hosts.");
            if (source.ModpackHostsOnly && !ModpackAllowed(current)) throw new InvalidDataException("Modpack source is outside its admitted hosts.");
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            foreach (var header in source.Headers ?? [])
            {
                if (source.UserAgent is not null && header.Name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
                    || beginOffset > 0 && header.Name.Equals("Range", StringComparison.OrdinalIgnoreCase)) continue;
                if (!request.Headers.TryAddWithoutValidation(header.Name, header.Values)) throw new InvalidDataException("Invalid download request header.");
            }
            if (beginOffset > 0) request.Headers.Range = new RangeHeaderValue(beginOffset, null);
            if (source.UserAgent is { Length: > 0 } userAgent) request.Headers.UserAgent.ParseAdd(userAgent);
            using var headers = CancellationTokenSource.CreateLinkedTokenSource(token);
            var timeout = source.HeaderTimeout ?? TimeSpan.FromSeconds(100);
            if (timeout != Timeout.InfiniteTimeSpan) headers.CancelAfter(timeout);
            _response = await send(request, source, source.AllowAutoRedirect && !source.ResourceHostsOnly && !source.ModpackHostsOnly, headers.Token).ConfigureAwait(false);
            if ((source.ResourceHostsOnly || source.ModpackHostsOnly) && (int)_response.StatusCode is >= 300 and < 400 && _response.Headers.Location is { } location)
            {
                if (redirect >= source.MaximumRedirects) throw new InvalidDataException("Download redirect limit exceeded.");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                _response.Dispose(); _response = null; continue;
            }
            _response.EnsureSuccessStatusCode();
            var final = _response.RequestMessage?.RequestUri ?? current;
            if (source.AuthoritativeOrigin is { } origin && !SameOrigin(new Uri(origin), final))
                throw new InvalidDataException("Authoritative download redirected to another origin.");
            if (source.ResourceHostsOnly && !ResourceAllowed(final, source.MainlandChina))
                throw new InvalidDataException("Resource download redirected outside its admitted hosts.");
            var info = DownloadHttpRanges.Read(_response, beginOffset);
            if (source.MaximumLength is { } maximum && info.Length > maximum
                || source.ExactLength is { } expected && info.Length >= 0 && info.Length != expected)
                throw new InvalidDataException("Download length does not match its admitted artifact.");
            _received = info.BeginOffset;
            _stream = await _response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return source.ExactLength is { } exact ? info with { Length = exact, EndOffset = exact - 1 } : info;
        }
    }
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        if (buffer.IsEmpty) return 0;
        var stream = _stream ?? throw new InvalidOperationException("Download connection has not started.");
        long? limit = source.ExactLength ?? source.MaximumLength;
        if (limit is { } maximum && _received >= maximum)
        {
            byte[] extra = new byte[1];
            if (await stream.ReadAsync(extra, token).ConfigureAwait(false) != 0)
                throw new InvalidDataException("Download exceeded its admitted length.");
            return 0;
        }
        if (limit is { } bound) buffer = buffer[..(int)Math.Min(buffer.Length, bound - _received)];
        int count = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
        _received = checked(_received + count);
        if (count == 0 && source.ExactLength is { } expected && _received != expected)
            throw new InvalidDataException("Download ended before its admitted length.");
        return count;
    }
    public async ValueTask StopAsync(CancellationToken token = default)
    {
        try { if (_stream is not null) await _stream.DisposeAsync().ConfigureAwait(false); }
        finally { _stream = null; _response?.Dispose(); _response = null; }
    }
    public ValueTask DisposeAsync() => StopAsync();

    private static bool SameOrigin(Uri first, Uri second) => first.Scheme == second.Scheme
        && first.Port == second.Port && string.Equals(first.IdnHost, second.IdnHost, StringComparison.OrdinalIgnoreCase);
    private static bool ModpackAllowed(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.IdnHost.ToLowerInvariant() is "cdn.modrinth.com" or "github.com" or "raw.githubusercontent.com" or "gitlab.com"
            or "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "edge.forgecdn.net" or "mediafilez.forgecdn.net" or "media.forgecdn.net";
    private static bool ResourceAllowed(Uri uri, bool mainland) => uri.Scheme == "https" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && (uri.IdnHost.ToLowerInvariant() is "cdn.modrinth.com" or "edge.forgecdn.net" or "mediafilez.forgecdn.net" or "media.forgecdn.net"
            || mainland && uri.IdnHost.ToLowerInvariant() is "mod.mcimirror.top" or "mcim-files.pysio.online");
}
