using Nexa.Services.Downloads;
using Nexa.Services.Minecraft.Launch;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask DownloadTimeoutFailsOver()
    {
        string root = CreateTempDirectory();
        try
        {
            foreach (bool duringRead in new[] { false, true })
            {
                using var service = CreateDownloadService();
                var timedOut = new TimedOutDownloadConnection(duringRead);
                var backup = new FakeConnection(3, [1, 2, 3]);
                string destination = Path.Combine(root, duringRead + ".bin");
                var result = await service.DownloadAsync(new()
                {
                    Sources = ["primary", "backup"],
                    DestinationPath = destination,
                    ConnectionFactory = source => source == "primary" ? timedOut : backup,
                });
                AssertTrue(result.Success);
                AssertEqual("backup", result.SuccessfulSource);
                AssertTrue(result.Errors.Single().Failure is OperationCanceledException);
                AssertTrue(timedOut.Stopped);
                AssertTrue((await File.ReadAllBytesAsync(destination)).SequenceEqual(new byte[] { 1, 2, 3 }));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class TimedOutDownloadConnection(bool duringRead) : IDownloadConnection
    {
        internal bool Stopped;
        public ValueTask<DownloadConnectionInfo> StartAsync(long beginOffset, CancellationToken cancellationToken = default)
            => duringRead ? ValueTask.FromResult(new DownloadConnectionInfo(3, 0, 2, false))
                : throw new TaskCanceledException("Source header timeout.");
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new TaskCanceledException("Source read timeout.");
        public ValueTask StopAsync(CancellationToken cancellationToken = default) { Stopped = true; return ValueTask.CompletedTask; }
    }

    private static async ValueTask OfflineTextureRejectionPreservesCapacity()
    {
        using var server = new OfflineTextureServer();
        var identity = new MinecraftLaunchIdentity("LocalPlayer", new string('1', 32), "0", MinecraftLaunchIdentityMode.Offline)
        { OfflineSkinPng = WardrobeCreatePng(), OfflineCapePng = [1, 2, 3] };
        for (int i = 0; i < 128; i++)
        {
            bool rejected = false;
            try { server.Register(identity); }
            catch (InvalidDataException) { rejected = true; }
            AssertTrue(rejected);
        }
        var valid = identity with { OfflineSkinPng = WardrobeCreatePng(width: 128, height: 128), OfflineCapePng = [] };
        var first = server.Register(valid);
        AssertEqual(first, server.Register(valid));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        string rejectedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(identity.OfflineSkinPng));
        using var response = await http.GetAsync(new Uri(new Uri(first.Server), "../textures/" + rejectedHash));
        AssertEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        for (int i = 1; i < 128; i++) server.Register(valid with { PlayerName = "Local" + i });
        bool full = false;
        try { server.Register(valid with { PlayerName = "Overflow" }); }
        catch (InvalidDataException) { full = true; }
        AssertTrue(full);
    }
}
