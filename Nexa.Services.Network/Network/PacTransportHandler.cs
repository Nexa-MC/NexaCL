using System.Net;

namespace Nexa.Services.Network;

/// <summary>Evaluates PAC again for every redirect and keeps route selection separate from credentials.</summary>
internal sealed class PacTransportHandler : HttpMessageHandler
{
    private readonly PacResolver _resolver;
    private readonly Func<Uri?, HttpMessageHandler> _factory;
    private readonly bool _redirects;
    private readonly Dictionary<string, HttpMessageInvoker> _transports = [];
    private readonly object _gate = new();

    internal PacTransportHandler(PacResolver resolver, Func<Uri?, HttpMessageHandler> factory, bool redirects)
    { _resolver = resolver; _factory = factory; _redirects = redirects; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Uri destination = request.RequestUri!;
        HttpMethod method = request.Method;
        HttpContent? content = request.Content;
        bool stripAuthorization = false;
        for (int redirects = 0; ; redirects++)
        {
            Uri?[] routes = await _resolver.ResolveAsync(destination, token).ConfigureAwait(false);
            HttpResponseMessage? response = null;
            for (int route = 0; route < routes.Length; route++)
            {
                using var forwarded = CopyRequest(request, destination, method, content, stripAuthorization);
                try { response = await Transport(routes[route]).SendAsync(forwarded, token).ConfigureAwait(false); break; }
                // Never replay a body/non-idempotent operation or retry an HTTP status/TLS failure.
                catch (HttpRequestException error) when (route + 1 < routes.Length && content is null
                    && (method == HttpMethod.Get || method == HttpMethod.Head)
                    && error.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError)
                { }
            }
            if (response is null) throw new HttpRequestException("PAC returned no usable route.");
            int status = (int)response.StatusCode;
            if (!_redirects || status is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is not { } location)
                return response;
            Uri next;
            try { next = location.IsAbsoluteUri ? location : new Uri(destination, location); }
            catch { response.Dispose(); throw; }
            if (redirects >= 9 || next.Scheme is not ("http" or "https") || next.UserInfo.Length != 0
                || destination.Scheme == "https" && next.Scheme != "https")
            { response.Dispose(); throw new HttpRequestException("PAC redirect was not permitted."); }
            response.Dispose();
            stripAuthorization = true;
            if (status == 303 && method != HttpMethod.Head || status is 301 or 302 && method == HttpMethod.Post)
            { method = HttpMethod.Get; content = null; }
            destination = next;
        }
    }

    private HttpMessageInvoker Transport(Uri? endpoint)
    {
        string key = endpoint?.AbsoluteUri ?? "DIRECT";
        lock (_gate)
        {
            if (_transports.TryGetValue(key, out var transport)) return transport;
            // A changing/malicious script cannot allocate an unbounded set of socket pools.
            if (_transports.Count == 32) throw new HttpRequestException("PAC endpoint pool limit exceeded; reload proxy settings.");
            return _transports[key] = new HttpMessageInvoker(_factory(endpoint));
        }
    }

    private static HttpRequestMessage CopyRequest(HttpRequestMessage source, Uri destination, HttpMethod method,
        HttpContent? content, bool redirect)
    {
        var copy = new HttpRequestMessage(method, destination) { Version = source.Version, VersionPolicy = source.VersionPolicy };
        foreach (var header in source.Headers)
        {
            if (header.Key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
                || redirect && (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))) continue;
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        foreach (var option in source.Options) copy.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        if (content is not null) copy.Content = new BorrowedContent(content);
        return copy;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resolver.Dispose();
            lock (_gate) { foreach (var transport in _transports.Values) transport.Dispose(); _transports.Clear(); }
        }
        base.Dispose(disposing);
    }

    private sealed class BorrowedContent : HttpContent
    {
        private readonly HttpContent _source;
        internal BorrowedContent(HttpContent source)
        { _source = source; foreach (var header in source.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value); }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => _source.CopyToAsync(stream);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token) => _source.CopyToAsync(stream, token);
        protected override bool TryComputeLength(out long length)
        { length = _source.Headers.ContentLength ?? 0; return _source.Headers.ContentLength is not null; }
    }
}
