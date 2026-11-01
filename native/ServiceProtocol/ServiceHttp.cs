using System.Net;
using System.Net.Http.Headers;
using Nexa.Native.Services.Wire;

namespace Nexa.Native.Services;

internal static class ServiceHttp
{
    internal const int Operation = 1;
    internal const int MaximumBody = 16 * 1024 * 1024;
    private static async Task<byte[]> BodyAsync(HttpContent? content, CancellationToken token)
    {
        if (content is null) return [];
        if (content.Headers.ContentLength > MaximumBody) throw new InvalidDataException("Account HTTP body budget exceeded.");
        await using var source = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using MemoryStream output = new();
        byte[] buffer = new byte[64 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (count > MaximumBody - output.Length) throw new InvalidDataException("Account HTTP body budget exceeded.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private static void Headers(ServiceWireWriter writer, HttpHeaders headers)
    {
        var entries = headers.ToArray(); writer.Count(entries.Length);
        foreach (var entry in entries)
        { writer.String(entry.Key); var values = entry.Value.ToArray(); writer.Count(values.Length); foreach (var value in values) writer.String(value); }
    }
    private static void Headers(ServiceWireReader reader, HttpHeaders headers)
    {
        int count = reader.Count(); if (count < 0) throw new InvalidDataException("Missing HTTP headers.");
        for (int index = 0; index < count; index++)
        {
            string key = reader.String() ?? throw new InvalidDataException("Missing HTTP header name.");
            int length = reader.Count(); if (length < 0) throw new InvalidDataException("Missing HTTP header values.");
            string[] values = new string[length];
            for (int value = 0; value < values.Length; value++) values[value] = reader.String() ?? throw new InvalidDataException("Missing HTTP header value.");
            if (!headers.TryAddWithoutValidation(key, values)) throw new InvalidDataException("Invalid HTTP header location.");
        }
    }
    internal static async Task<byte[]> ExecuteAsync(HttpClient client, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = new ServiceWireReader(bytes);
        if (reader.Int32() != Operation) throw new InvalidDataException("Unknown account reverse operation.");
        var method = new HttpMethod(reader.String() ?? throw new InvalidDataException("Missing HTTP method."));
        using var request = new HttpRequestMessage(method, reader.Uri() ?? throw new InvalidDataException("Missing HTTP address."));
        byte[] body = reader.Buffer(); bool hasContent = reader.Boolean();
        if (hasContent) request.Content = new ByteArrayContent(body);
        else if (body.Length != 0) throw new InvalidDataException("Unexpected HTTP body.");
        Headers(reader, request.Headers);
        if (hasContent) Headers(reader, request.Content!.Headers);
        reader.End();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        byte[] result = await BodyAsync(response.Content, token).ConfigureAwait(false);
        using var writer = new ServiceWireWriter();
        writer.Int32((int)response.StatusCode); writer.String(response.ReasonPhrase);
        writer.String(response.RequestMessage?.RequestUri?.AbsoluteUri ?? request.RequestUri!.AbsoluteUri);
        writer.Buffer(result); Headers(writer, response.Headers); Headers(writer, response.Content.Headers);
        return writer.Finish();
    }
    internal sealed class Handler(ServiceEndpoint endpoint) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] body = await BodyAsync(request.Content, cancellationToken).ConfigureAwait(false);
            using var writer = new ServiceWireWriter();
            writer.Int32(Operation); writer.String(request.Method.Method); writer.Uri(request.RequestUri);
            writer.Buffer(body); writer.Boolean(request.Content is not null); Headers(writer, request.Headers);
            if (request.Content is not null) Headers(writer, request.Content.Headers);
            byte[] bytes;
            try { bytes = await endpoint.InvokeAsync(writer.Finish(), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is InvalidOperationException or IOException)
            { throw new HttpRequestException("The configured network transport failed.", error); }
            using var reader = new ServiceWireReader(bytes);
            int status = reader.Int32(); if (status is < 100 or > 599) throw new InvalidDataException("Invalid HTTP status.");
            string? reason = reader.String(); var uri = reader.Uri();
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                ReasonPhrase = reason,
                Content = new ByteArrayContent(reader.Buffer()),
                RequestMessage = new(request.Method, uri)
            };
            try { Headers(reader, response.Headers); Headers(reader, response.Content.Headers); reader.End(); return response; }
            catch { response.Dispose(); throw; }
        }
    }
}
