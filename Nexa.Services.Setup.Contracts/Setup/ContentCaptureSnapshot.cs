namespace Nexa.Services.Setup;

/// <summary>An optional immutable source lease. The provider owns native cleanup.</summary>
public sealed class ContentCaptureSnapshot(string sourceDirectory, IAsyncDisposable lease) : IAsyncDisposable
{
    public string SourceDirectory { get; } = sourceDirectory;
    public ValueTask DisposeAsync() => lease.DisposeAsync();
}
