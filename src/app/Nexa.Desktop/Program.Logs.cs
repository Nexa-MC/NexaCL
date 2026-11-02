using Nexa.Services.Logging;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop;

internal static partial class Program
{
    internal static FileLogSink? TryCreateSessionLog(string directory)
    {
        try { return FileLogSink.CreateSession(directory); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static void WriteBootstrapLog(FileLogSink? sink, LogLevel level, string message, string? detail = null)
    {
        if (sink is null) return;
        LogEntry entry = new(0, DateTimeOffset.UtcNow, level, "Startup", LogRedactor.Redact(message),
            detail is null ? null : LogRedactor.Redact(detail));
        sink.Write(entry, entry.ToDisplayText());
    }

    internal static async Task<string?> FlushStartupFailureAsync(FileLogSink? sink, string stage, Exception error)
    {
        if (sink is null) return null;
        WriteBootstrapLog(sink, LogLevel.Error, "Initialization failed at " + stage, ExceptionDiagnostics.Describe(error));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await sink.FlushAsync(timeout.Token).ConfigureAwait(false); return sink.FilePath; }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or OperationCanceledException) { return null; }
    }

    private static async Task<bool> ExportLogsAsync(FileLogSink sink, AvaloniaUiPlatformActions actions, CancellationToken token)
    {
        string? directory = await actions.PickExportDirectoryAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (directory is null) return false;
        string destination = Path.Combine(directory, $"Nexa-Log-Facts-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        await sink.ExportAsync(destination, token).ConfigureAwait(false);
        return true;
    }
}
