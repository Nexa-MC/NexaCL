using System.Net;
using Nexa.Services.Downloads;

namespace Nexa.Native.Network.Services;

public static class NativeDownloadResults
{
    public static NativeDownloadOutcome Encode(DownloadTransferResult result) => new(result.Success, result.DestinationPath,
        result.SuccessfulSource, result.TotalBytes, result.Duration, result.Errors.Select(error => new NativeDownloadFailure(error.Source, error.Message,
            error.Failure switch
            {
                HttpRequestException => NativeDownloadFailureKind.Http,
                UnauthorizedAccessException => NativeDownloadFailureKind.Unauthorized,
                InvalidDataException => NativeDownloadFailureKind.InvalidData,
                IOException => NativeDownloadFailureKind.Io,
                InvalidOperationException => NativeDownloadFailureKind.InvalidOperation,
                ArgumentException => NativeDownloadFailureKind.Argument,
                NotSupportedException => NativeDownloadFailureKind.NotSupported,
                _ => NativeDownloadFailureKind.Other
            }, error.Failure is HttpRequestException { StatusCode: { } status } ? (int)status : null)).ToArray());
    public static DownloadTransferResult Decode(NativeDownloadOutcome result) => new(result.Success, result.DestinationPath,
        result.SuccessfulSource, result.TotalBytes, result.Duration,
        result.Errors.Select(error => new DownloadAttemptError(error.Source, error.Message, Failure(error))).ToArray());
    private static Exception Failure(NativeDownloadFailure error) => error.Kind switch
    {
        NativeDownloadFailureKind.Http => new HttpRequestException(error.Message, null, error.HttpStatus is { } status ? (HttpStatusCode)status : null),
        NativeDownloadFailureKind.Unauthorized => new UnauthorizedAccessException(error.Message),
        NativeDownloadFailureKind.InvalidData => new InvalidDataException(error.Message),
        NativeDownloadFailureKind.InvalidOperation => new InvalidOperationException(error.Message),
        NativeDownloadFailureKind.Argument => new ArgumentException(error.Message),
        NativeDownloadFailureKind.NotSupported => new NotSupportedException(error.Message),
        _ => new IOException(error.Message)
    };
}
