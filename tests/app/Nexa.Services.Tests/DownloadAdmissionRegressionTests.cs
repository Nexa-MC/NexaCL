using Nexa.Services.Downloads;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask DownloadPreCancellationDoesNotStartTransfer()
    {
        string root = CreateTempDirectory();
        try
        {
            using var service = CreateDownloadService();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            int connections = 0;
            bool canceled = false;
            try
            {
                await service.DownloadAsync(new()
                {
                    Sources = ["memory://canceled"],
                    DestinationPath = Path.Combine(root, "file.bin"),
                    ConnectionFactory = _ => { connections++; return new FakeConnection(1, [1]); },
                }, cancellationToken: cancellation.Token);
            }
            catch (OperationCanceledException) { canceled = true; }
            AssertTrue(canceled);
            AssertEqual(0, connections);
            AssertEqual(0, Directory.GetFiles(root).Length);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask DownloadProgressPermitsReentrantSubscriptions()
    {
        string root = CreateTempDirectory();
        try
        {
            using var service = CreateDownloadService();
            int connections = 0, nestedReports = 0;
            Task<DownloadTransferResult>? nested = null;
            bool subscribed = false;
            DownloadRequest request = new()
            {
                Sources = ["memory://reentrant"],
                DestinationPath = Path.Combine(root, "file.bin"),
                ConnectionFactory = _ => { connections++; return new FakeConnection(2, [1, 2]); },
            };
            var result = await service.DownloadAsync(request, _ =>
            {
                if (subscribed) return;
                subscribed = true;
                nested = service.DownloadAsync(request, _ => nestedReports++);
            });
            AssertTrue(result.Success);
            AssertTrue(nested is not null);
            AssertTrue((await nested!).Success);
            AssertEqual(1, connections);
            AssertTrue(nestedReports > 0);
            AssertTrue(File.ReadAllBytes(request.DestinationPath) is [1, 2]);
        }
        finally { Directory.Delete(root, true); }
    }
}
