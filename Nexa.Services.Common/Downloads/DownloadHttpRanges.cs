using System.Net;

namespace Nexa.Services.Downloads;

internal static class DownloadHttpRanges
{
    internal static DownloadConnectionInfo Read(HttpResponseMessage response, long requestedOffset)
    {
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content.Headers.ContentRange is not { From: { } from, To: { } to, Length: { } total }
                || from != requestedOffset || to != total - 1
                || response.Content.Headers.ContentLength is { } length && length != total - from)
                throw new InvalidDataException("Download response range does not match the requested artifact.");
            return new(total, from, to, false);
        }
        // A server ignoring Range sends the whole body. Restart rather than append twice.
        long size = response.Content.Headers.ContentLength ?? -1;
        return new(size, 0, size < 0 ? -1 : size - 1, false);
    }
}
