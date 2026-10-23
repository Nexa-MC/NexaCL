using System.Globalization;
using Nexa.Services.Logging;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask SessionLogsKeepLaunchesSeparateAndProtectLiveFiles()
    {
        string root = CreateTempDirectory();
        if (OperatingSystem.IsMacOS() && root.StartsWith("/var/", StringComparison.Ordinal)) root = "/private" + root;
        try
        {
            await using var first = FileLogSink.CreateSession(root);
            await using var second = FileLogSink.CreateSession(root);
            AssertTrue(first.FilePath != second.FilePath);
            foreach (var sink in new[] { first, second })
                AssertTrue(DateTime.TryParseExact(Path.GetFileNameWithoutExtension(sink.FilePath),
                    OwnedLogFiles.SessionTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
            var entry = new LogEntry(1, DateTimeOffset.UtcNow, LogLevel.Info, "Startup", "", null);
            first.Write(entry, "[12:00:00.000] [Info] [Startup] first-launch");
            second.Write(entry, "[12:00:00.000] [Info] [Startup] second-launch");
            await first.FlushAsync(); await second.FlushAsync();
            AssertTrue((await ReadLiveLog(first.FilePath)).Contains("first-launch", StringComparison.Ordinal));
            AssertFalse((await ReadLiveLog(second.FilePath)).Contains("first-launch", StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(first.FilePath, DateTime.UtcNow.AddDays(-30));
            string legacy = Path.Combine(root, "launcher.log"), unrelated = Path.Combine(root, "personal.log"), malformed = Path.Combine(root, "20261399T9999990000000Z.log");
            File.WriteAllText(legacy, "[12:00:00.000] [Info] [Launcher] old launcher");
            File.WriteAllText(unrelated, "private"); File.WriteAllText(malformed, "private");
            second.SetRetentionDays(1);
            await second.FlushAsync();
            AssertTrue(File.Exists(first.FilePath)); // A live writer survives another session's cleanup.
            await first.DisposeAsync();
            File.WriteAllText(first.FilePath + ".active", ""); // An unlocked lease left by an interrupted process.
            using var captured = await second.CaptureSnapshotAsync();
            AssertEqual(3, captured.Files.Count);
            second.SetRetentionDays(1);
            await second.FlushAsync();
            AssertFalse(File.Exists(first.FilePath));
            AssertFalse(File.Exists(first.FilePath + ".active"));
            AssertTrue(captured.Files.All(file => file.Stream.CanRead));
            AssertTrue(File.Exists(second.FilePath) && File.Exists(unrelated) && File.Exists(malformed));
            using var snapshot = await second.CaptureSnapshotAsync();
            AssertEqual(2, snapshot.Files.Count); // Legacy plus current, without arbitrary .log files.
            AssertEqual(1, snapshot.Files.Count(file => file.Current));
        }
        finally { Directory.Delete(root, true); }

        static async Task<string> ReadLiveLog(string path)
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            return await reader.ReadToEndAsync();
        }
    }
}
