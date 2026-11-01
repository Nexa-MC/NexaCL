using Nexa.Services.Network;
using Nexa.Services.Scheduling;

namespace Nexa.Native.Network.Services;

public sealed record NativeHttpHeader(string Name, IReadOnlyList<string> Values);

/// <summary>Value-only source admission; the owning runtime creates its connection.</summary>
public sealed record NativeDownloadSource(
    string Address,
    NetworkPreferences Preferences,
    bool AllowAutoRedirect = true,
    string? AuthoritativeOrigin = null,
    long? MaximumLength = null,
    long? ExactLength = null,
    bool ResourceHostsOnly = false,
    bool MainlandChina = false,
    string? UserAgent = null,
    IReadOnlyList<NativeHttpHeader>? Headers = null,
    TimeSpan? HeaderTimeout = null,
    long Certificate = 0,
    long CertificateContext = 0,
    bool Available = true,
    bool ModpackHostsOnly = false,
    int MaximumRedirects = 5);

/// <summary>File download configuration without managed connection or writer factories.</summary>
public sealed record NativeDownloadRequest(
    IReadOnlyList<NativeDownloadSource> Sources,
    string DestinationPath,
    bool AllowResume = true,
    int MaxParallelSegments = 1,
    string TemporaryExtension = ".PCLDownloading",
    string? Digest = null,
    long? ExpectedSize = null,
    long BandwidthBytesPerSecond = 0,
    WorkPriority Priority = WorkPriority.Interactive,
    bool BackgroundDownloadsEnabled = true);

internal enum NativeDownloadFailureKind { Http, Io, Unauthorized, InvalidData, InvalidOperation, Argument, NotSupported, Other }
internal sealed record NativeDownloadFailure(string Source, string Message, NativeDownloadFailureKind Kind, int? HttpStatus);
internal sealed record NativeDownloadOutcome(bool Success, string DestinationPath, string? SuccessfulSource,
    long TotalBytes, TimeSpan Duration, IReadOnlyList<NativeDownloadFailure> Errors);
