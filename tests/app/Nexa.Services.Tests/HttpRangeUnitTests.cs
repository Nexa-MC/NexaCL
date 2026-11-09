using System.Net;
using System.Net.Http.Headers;
using Nexa.Services.Downloads;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static ValueTask HttpResumeRequiresByteRanges()
    {
        foreach (string unit in new[] { "items", "records" })
        {
            using var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([3, 4]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(2, 3, 4) { Unit = unit };
            bool rejected = false;
            try { DownloadHttpRanges.Read(response, 2); }
            catch (InvalidDataException) { rejected = true; }
            AssertTrue(rejected);
        }
        foreach (string unit in new[] { "bytes", "Bytes" })
        {
            using var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([3, 4]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(2, 3, 4) { Unit = unit };
            var range = DownloadHttpRanges.Read(response, 2);
            AssertEqual(2L, range.BeginOffset); AssertEqual(4L, range.Length);
        }
        using var full = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
        AssertEqual(0L, DownloadHttpRanges.Read(full, 2).BeginOffset);
        return ValueTask.CompletedTask;
    }
}
