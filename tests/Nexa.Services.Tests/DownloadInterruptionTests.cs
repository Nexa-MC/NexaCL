using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Nexa.Services.Downloads;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Settings;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask DownloadDeclaredLengthGuardsResumedTransfers()
    {
        string root = CreateTempDirectory();
        using var service = CreateDownloadService();
        byte[] data = [1, 2, 3, 4, 5, 6];
        List<long> offsets = [];
        string destination = Path.Combine(root, "artifact.jar");
        try
        {
            await File.WriteAllBytesAsync(destination, [9]);
            var request = new DownloadRequest
            {
                Sources = ["official"],
                DestinationPath = destination,
                ConnectionFactory = _ => new RetryBytesConnection(data, offsets, 3),
            };
            var failed = await service.DownloadAsync(request);
            AssertFalse(failed.Success);
            AssertTrue(failed.Errors.Single().Failure is EndOfStreamException);
            AssertTrue(await File.ReadAllBytesAsync(destination) is [9]);
            AssertEqual(3L, new FileInfo(destination + ".PCLDownloading").Length);
            var recovered = await service.DownloadAsync(request with
            {
                Sources = ["mirror"],
                ConnectionFactory = _ => new RetryBytesConnection(data, offsets),
            });
            AssertTrue(recovered.Success);
            AssertTrue(offsets.SequenceEqual([0L, 3L]));
            AssertTrue((await File.ReadAllBytesAsync(destination)).SequenceEqual(data));
            AssertFalse(File.Exists(destination + ".PCLDownloading"));

            // A response larger than its declared total must not replace the final file either.
            var oversized = await service.DownloadAsync(request with { ConnectionFactory = _ => new FakeConnection(2, [1, 2, 3]) });
            AssertFalse(oversized.Success);
            AssertTrue(oversized.Errors.Single().Failure is InvalidDataException);
            AssertTrue((await File.ReadAllBytesAsync(destination)).SequenceEqual(data));

            // EOF is still authoritative when the transport has no length to declare.
            var unknownLength = await service.DownloadAsync(request with { ConnectionFactory = _ => new FakeConnection(-1, data) });
            AssertTrue(unknownLength.Success);
            AssertTrue((await File.ReadAllBytesAsync(destination)).SequenceEqual(data));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask DownloadUnknownArtifactSizeRetainsInterruptedPrefix()
    {
        string root = CreateTempDirectory();
        using var service = CreateDownloadService();
        byte[] data = [1, 2, 3, 4, 5, 6];
        List<long> offsets = [];
        try
        {
            var request = new DownloadRequest
            {
                Sources = ["official", "mirror"],
                DestinationPath = Path.Combine(root, "artifact.jar"),
                CacheIdentity = new(Convert.ToHexString(SHA1.HashData(data)), null),
                ConnectionFactory = source => new RetryBytesConnection(data, offsets, source == "official" ? 3 : int.MaxValue),
            };
            var result = await service.DownloadAsync(request);
            AssertTrue(result.Success);
            AssertEqual("mirror", result.SuccessfulSource);
            AssertTrue(result.Errors.Single().Failure is EndOfStreamException);
            AssertTrue(offsets.SequenceEqual([0L, 3L]));
            AssertTrue((await File.ReadAllBytesAsync(request.DestinationPath)).SequenceEqual(data));
            AssertEqual(0, Directory.GetFiles(service.SessionCacheDirectory!, "*.PCLDownloading").Length);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask NeoForgeDependencyInterruptionPreservesCauseAndRetries()
    {
        foreach (string scenario in new[] { "network-failure", "retry", "retry-exhausted", "bad-digest" })
        {
            string root = CreateTempDirectory();
            using var downloads = CreateDownloadService();
            byte[] data = [1, 2, 3, 4, 5, 6];
            const string relative = "net/neoforged/fancymodloader/earlydisplay/4.0.44/earlydisplay-4.0.44.jar";
            string url = "https://maven.neoforged.net/releases/" + relative;
            var manifest = new JsonObject
            {
                ["id"] = "loader-built",
                ["inheritsFrom"] = "1.21.1",
                ["mainClass"] = "bootstrap.Main",
                ["libraries"] = new JsonArray(new JsonObject
                {
                    ["name"] = "net.neoforged.fancymodloader:earlydisplay:4.0.44",
                    ["downloads"] = new JsonObject
                    {
                        ["artifact"] = new JsonObject
                        {
                            ["path"] = relative,
                            ["url"] = url,
                            ["size"] = data.Length,
                            ["sha1"] = Convert.ToHexString(SHA1.HashData(data)),
                        }
                    },
                }),
            };
            byte[] archive = InstallerFixtureArchive(manifest);
            using var http = new HttpClient(new StaticHttpMessageHandler(Convert.ToHexString(SHA1.HashData(archive))));
            var (_, settings) = PolicyFixture(new InMemorySettingsPort());
            bool retry = scenario is "retry" or "retry-exhausted";
            AssertTrue(settings.Set(new("network.file-retry", SettingsLayer.Global, new(SettingsOverrideMode.Custom, retry ? "true" : "false"))).IsSuccess);
            AssertTrue(settings.Set(new("network.game-source", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "official-only"))).IsSuccess);
            var reset = new SocketException((int)SocketError.ConnectionReset);
            var failure = new IOException("TLS read failed", new HttpRequestException("Connection interrupted", reset));
            List<long> offsets = [];
            int attempts = 0;
            bool ran = false;
            var service = new ForgeInstallService(downloads, http, source =>
            {
                if (source.EndsWith("-installer.jar", StringComparison.Ordinal)) return new RetryBytesConnection(archive, []);
                AssertEqual(url, source);
                attempts++;
                return scenario == "bad-digest" ? new RetryBytesConnection([9, 9, 9, 9, 9, 9], offsets)
                    : scenario == "retry" && attempts > 1 ? new RetryBytesConnection(data, offsets)
                    : new InterruptedBytesConnection(data, offsets, failure);
            }, (_, _) => Task.FromResult("fixture-java"), async (start, token) =>
            {
                ran = true;
                AssertTrue((await File.ReadAllBytesAsync(Path.Combine(start.WorkingDirectory, "libraries", relative), token)).SequenceEqual(data));
                string version = Path.Combine(start.WorkingDirectory, "versions", "loader-built");
                Directory.CreateDirectory(version);
                await File.WriteAllTextAsync(Path.Combine(version, "loader-built.json"), manifest.ToJsonString(), token);
            })
            { SettingsPolicy = settings };
            try
            {
                string vanilla = Path.Combine(root, "versions", "1.21.1");
                Directory.CreateDirectory(vanilla);
                await File.WriteAllTextAsync(Path.Combine(vanilla, "1.21.1.jar"), "client");
                IOException? caught = null;
                try { await service.InstallAsync(new(root, "1.21.1", "instance", InstallLoader.NeoForge, "21.1.235", new()), null, CancellationToken.None); }
                catch (IOException error) { caught = error; }
                string final = Path.Combine(root, "libraries", relative);
                if (scenario == "retry")
                {
                    AssertTrue(caught is null); AssertTrue(ran); AssertEqual(2, attempts);
                    AssertTrue(offsets.SequenceEqual([0L, 3L]));
                    AssertTrue((await File.ReadAllBytesAsync(final)).SequenceEqual(data));
                }
                else
                {
                    AssertTrue(caught is not null);
                    AssertEqual("加载器文件下载失败：earlydisplay-4.0.44.jar", caught!.Message);
                    AssertTrue(caught.InnerException is AggregateException);
                    var causes = ((AggregateException)caught.InnerException!).InnerExceptions;
                    AssertEqual(retry ? 2 : 1, causes.Count);
                    AssertTrue(causes.All(cause => scenario == "bad-digest" ? cause is InvalidDataException : ReferenceEquals(failure, cause)));
                    AssertFalse(ran); AssertFalse(File.Exists(final));
                    string[] partials = Directory.GetFiles(downloads.SessionCacheDirectory!, "*.PCLDownloading");
                    if (scenario != "bad-digest") AssertEqual(3L, new FileInfo(partials.Single()).Length);
                    else AssertEqual(0, partials.Length);
                }
                AssertEqual(0, Directory.GetDirectories(Path.Combine(root, ".nexa-install")).Length);
                string cache = downloads.SessionCacheDirectory!;
                downloads.Dispose(); AssertFalse(Directory.Exists(cache));
            }
            finally { Directory.Delete(root, true); }
        }
    }

    private sealed class InterruptedBytesConnection(byte[] bytes, List<long> offsets, Exception failure) : IDownloadConnection
    {
        private readonly RetryBytesConnection _prefix = new(bytes, offsets, 3);
        public ValueTask<DownloadConnectionInfo> StartAsync(long beginOffset, CancellationToken cancellationToken = default) => _prefix.StartAsync(beginOffset, cancellationToken);
        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await _prefix.ReadAsync(buffer, cancellationToken);
            if (read == 0) throw failure;
            return read;
        }
        public ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
