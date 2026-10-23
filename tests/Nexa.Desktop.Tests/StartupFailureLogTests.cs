using Nexa.Services.Logging;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void StartupFailureLogFlushesBeforeLinkAndSurvivesAttemptDisposal()
    {
        string root = Path.Combine(Path.GetTempPath(), "nexa-startup-log-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsMacOS() && root.StartsWith("/var/", StringComparison.Ordinal)) root = "/private" + root;
        Directory.CreateDirectory(root);
        var sink = FileLogSink.CreateSession(root);
        try
        {
            Nexa.Desktop.Program.WriteBootstrapLog(sink, LogLevel.Info, "first-run-before-foundation");
            string? link = Nexa.Desktop.Program.FlushStartupFailureAsync(sink, "compose_foundation",
                new IOException("token=PRIVATE-STARTUP-SECRET")).GetAwaiter().GetResult();
            AssertEqual(sink.FilePath, link);
            string text = ReadLiveLog(link!);
            AssertTrue(text.Contains("first-run-before-foundation", StringComparison.Ordinal));
            AssertTrue(text.Contains("compose_foundation", StringComparison.Ordinal));
            AssertFalse(text.Contains("PRIVATE-STARTUP-SECRET", StringComparison.Ordinal));
            // The host LogService may already be disposed when its composition unwinds.
            // The process still owns this sink, so a retry/failure writes to the same file.
            Nexa.Desktop.Program.WriteBootstrapLog(sink, LogLevel.Info, "retry-attempt");
            sink.FlushAsync().GetAwaiter().GetResult();
            AssertTrue(ReadLiveLog(link!).Contains("retry-attempt", StringComparison.Ordinal));
        }
        finally { sink.DisposeAsync().AsTask().GetAwaiter().GetResult(); Directory.Delete(root, true); }
        AssertTrue(Nexa.Desktop.Program.FlushStartupFailureAsync(null, "folders", new IOException()).GetAwaiter().GetResult() is null);
        string occupied = Path.GetTempFileName();
        try { AssertTrue(Nexa.Desktop.Program.TryCreateSessionLog(occupied) is null); }
        finally { File.Delete(occupied); }

        static string ReadLiveLog(string path)
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            return reader.ReadToEnd();
        }
    }
}
