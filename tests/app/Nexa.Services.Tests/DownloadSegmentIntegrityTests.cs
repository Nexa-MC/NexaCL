using System.Security.Cryptography;
using Nexa.Services.Downloads;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask SegmentedDownloadsVerifyArtifactBeforePublication()
    {
        string root = CreateTempDirectory();
        byte[] expected = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        try
        {
            foreach (bool wrongSize in new[] { false, true })
            {
                using var service = CreateDownloadService(minimumSegmentBytes: 16);
                byte[] corrupt = wrongSize ? expected[..80] : Enumerable.Repeat((byte)255, 100).ToArray();
                string destination = Path.Combine(root, wrongSize + ".jar");
                await File.WriteAllBytesAsync(destination, [42]);
                var request = new DownloadRequest
                {
                    Sources = ["broken"],
                    DestinationPath = destination,
                    MaxParallelSegments = 4,
                    CacheIdentity = new(Convert.ToHexString(SHA256.HashData(expected)), expected.Length),
                    ConnectionFactory = source => new FakeSegmentedConnection(source == "broken" ? corrupt : expected),
                };
                var rejected = await service.DownloadAsync(request);
                AssertFalse(rejected.Success);
                AssertTrue((await File.ReadAllBytesAsync(destination)).SequenceEqual(new byte[] { 42 }));
                AssertEqual(0, Directory.GetFiles(service.SessionCacheDirectory!, "*.NexaSegment.*").Length);
                AssertEqual(0, Directory.GetFiles(service.SessionCacheDirectory!, "*.PCLDownloading").Length);
                var recovered = await service.DownloadAsync(request with { Sources = ["broken", "healthy"] });
                AssertTrue(recovered.Success);
                AssertEqual("healthy", recovered.SuccessfulSource);
                AssertEqual(1, recovered.Errors.Count);
                AssertTrue((await File.ReadAllBytesAsync(destination)).SequenceEqual(expected));
                var cached = await service.DownloadAsync(request with
                {
                    DestinationPath = Path.Combine(root, wrongSize + "-cached.jar"),
                    ConnectionFactory = _ => throw new InvalidOperationException("Verified cache should be reused."),
                });
                AssertTrue(cached.Success);
                AssertTrue((await File.ReadAllBytesAsync(cached.DestinationPath)).SequenceEqual(expected));
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
