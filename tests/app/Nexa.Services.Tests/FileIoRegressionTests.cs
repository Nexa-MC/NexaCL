using System.Net;
using System.Net.Http.Headers;
using Nexa.Services.Downloads;
using Nexa.Services.Files;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static void DownloadRangesRequireBytes()
    {
        foreach (string unit in new[] { "bytes", "BYTES", "items" })
        {
            using var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            { Content = new ByteArrayContent([3, 4]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(2, 3, 4) { Unit = unit };
            if (unit == "items")
            {
                bool rejected = false;
                try { DownloadHttpRanges.Read(response, 2); } catch (InvalidDataException) { rejected = true; }
                AssertTrue(rejected);
            }
            else
            {
                var info = DownloadHttpRanges.Read(response, 2);
                AssertEqual(4L, info.Length); AssertEqual(2L, info.BeginOffset);
            }
        }
        using var full = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
        AssertEqual(0L, DownloadHttpRanges.Read(full, 2).BeginOffset);
    }

    private static async ValueTask SafeFileCanceledReplacementRemovesTemporary()
    {
        string root = CreateTempDirectory();
        try
        {
            var folders = new AppFolders(root);
            var port = new SafeFilePort(folders);
            string directory = folders.EnsureFolder(FolderNames.Settings);
            string destination = Path.Combine(directory, "blocked");
            Directory.CreateDirectory(destination);
            using var cancellation = new CancellationTokenSource();
            Task pending = port.WriteBytesAsync(FolderNames.Settings, "blocked", [42], cancellation.Token);
            cancellation.Cancel();
            // Some platforms reject a directory destination before cancellation is observed.
            await RejectFailedWrite(pending);
            AssertTrue(Directory.Exists(destination));
            AssertEqual(0, Directory.GetFiles(directory, "*.tmp-*").Length);
            await RejectFailedWrite(port.WriteBytesAsync(FolderNames.Settings, "blocked", [42]));
            AssertEqual(0, Directory.GetFiles(directory, "*.tmp-*").Length);
            await TrustRejectsAsync<OperationCanceledException>(() => port.WriteBytesAsync(FolderNames.Cache, "new", [42], cancellation.Token));
            AssertFalse(Directory.Exists(Path.Combine(root, FolderNames.Cache)));
        }
        finally { Directory.Delete(root, true); }

        static async Task RejectFailedWrite(Task pending)
        {
            bool rejected = false;
            try { await pending; }
            catch (Exception failure) when (failure is OperationCanceledException or IOException or UnauthorizedAccessException)
            { rejected = true; }
            AssertTrue(rejected);
        }
    }

    private static async ValueTask SafeFileLargeReadAllocation()
    {
        string root = CreateTempDirectory();
        try
        {
            var folders = new AppFolders(root);
            var port = new SafeFilePort(folders);
            byte[] bytes = new byte[8 * 1024 * 1024];
            new Random(23).NextBytes(bytes);
            await port.WriteBytesAsync(FolderNames.Settings, "large", bytes);
            await port.WriteBytesAsync(FolderNames.Settings, "empty", []);
            AssertEqual(0, (await port.TryReadBytesAsync(FolderNames.Settings, "empty"))!.Length);
            long before = GC.GetTotalAllocatedBytes(precise: true);
            byte[]? result = await port.TryReadBytesAsync(FolderNames.Settings, "large");
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            AssertTrue(result is not null && result.AsSpan().SequenceEqual(bytes));
            Console.WriteLine($"Safe file 8 MiB read allocated {allocated} bytes.");
            AssertTrue(allocated < bytes.Length * 2L);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await TrustRejectsAsync<OperationCanceledException>(() => port.TryReadBytesAsync(FolderNames.Settings, "large", canceled.Token));
            var bounded = new SafeFilePort(folders, bytes.Length - 1);
            await TrustRejectsAsync<InvalidDataException>(() => bounded.TryReadBytesAsync(FolderNames.Settings, "large"));
        }
        finally { Directory.Delete(root, true); }
    }
}
