using System.Text;

namespace Nexa.Services.Network;

/// <summary>Owned, bounded bootstrap client. PAC downloads never inherit proxy credentials.</summary>
internal sealed class PacResolver : IDisposable
{
    private readonly Uri _source;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _script;
    private DateTimeOffset _expires;

    internal PacResolver(Uri source, HttpMessageHandler? handler = null)
    {
        _source = source;
        _client = new HttpClient(handler ?? new SocketsHttpHandler
        { UseProxy = false, UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5) })
        { Timeout = TimeSpan.FromSeconds(10) };
    }

    internal async Task<Uri?[]> ResolveAsync(Uri destination, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_script is null || DateTimeOffset.UtcNow >= _expires)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await _client.GetAsync(_source, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > PacEvaluator.MaximumSourceBytes)
                    throw new InvalidDataException("PAC file exceeds its size limit.");
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var data = new MemoryStream();
                byte[] buffer = new byte[8192];
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                {
                    if (data.Length + count > PacEvaluator.MaximumSourceBytes) throw new InvalidDataException("PAC file exceeds its size limit.");
                    data.Write(buffer, 0, count);
                }
                _script = new UTF8Encoding(false, true).GetString(data.GetBuffer(), 0, (int)data.Length).TrimStart('﻿');
                _expires = DateTimeOffset.UtcNow.AddMinutes(5);
            }
            // IWebProxy is synchronous. Resolve before entering the socket transport and keep
            // script/DNS work on a worker, never on the caller's UI thread. Serial admission bounds memory.
            return await Task.Run(() => PacEvaluator.EvaluateAsync(_script, destination, token), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { throw new HttpRequestException("PAC configuration could not resolve a route.", error); }
        finally { _gate.Release(); }
    }

    public void Dispose() { _client.Dispose(); _gate.Dispose(); }
}
