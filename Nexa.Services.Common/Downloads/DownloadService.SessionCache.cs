using System.Security.Cryptography;

namespace Nexa.Services.Downloads;

public sealed partial class DownloadService
{
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, SemaphoreSlim> _cacheLocks = new(StringComparer.Ordinal);
    private string? _cacheRoot;
    private int _cacheUsers;
    private bool _cacheDisposed;
    internal string? SessionCacheDirectory { get { lock (_cacheGate) return _cacheRoot; } }

    private async Task<DownloadTransferResult> DownloadCachedAsync(DownloadRequest request,
        Action<DownloadProgress>? progress, CancellationToken token)
    {
        var identity = request.CacheIdentity!;
        if (identity.Digest.Length is not (40 or 64 or 128) || !identity.Digest.All(char.IsAsciiHexDigit) || identity.Size < 0)
            throw new ArgumentException("A valid artifact digest and size are required.", nameof(request));
        string key = identity.Digest.ToLowerInvariant() + "-" + (identity.Size?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown");
        SemaphoreSlim mutex;
        string cached;
        lock (_cacheGate)
        {
            ObjectDisposedException.ThrowIf(_cacheDisposed, this);
            _cacheRoot ??= Path.Combine(Path.GetTempPath(), "NexaCL-download-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_cacheRoot);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_cacheRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (!_cacheLocks.TryGetValue(key, out mutex!)) _cacheLocks.Add(key, mutex = new(1, 1));
            cached = Path.Combine(_cacheRoot, key);
            _cacheUsers++;
        }
        bool entered = false;
        try
        {
            await mutex.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            DownloadTransferResult? transfer = null;
            if (File.Exists(cached) && !await MatchesCacheAsync(cached, identity, token).ConfigureAwait(false)) DeleteCached(cached);
            string partial = cached + ".PCLDownloading";
            if (!File.Exists(cached) && File.Exists(partial) && identity.Size is { } expected && new FileInfo(partial).Length >= expected)
            {
                if (await MatchesCacheAsync(partial, identity, token).ConfigureAwait(false)) File.Move(partial, cached);
                else File.Delete(partial);
            }
            if (!File.Exists(cached))
            {
                transfer = await DownloadAsync(request with
                {
                    CacheIdentity = null,
                    DestinationPath = cached,
                    ExpectedSize = identity.Size,
                    ExpectedArtifact = identity,
                    WriterFactory = static path => new FileDownloadWriter(path),
                }, update =>
                {
                    if (update.Stage != DownloadStage.Completed) progress?.Invoke(update);
                }, token).ConfigureAwait(false);
                if (!transfer.Success) return transfer with { DestinationPath = request.DestinationPath };
            }
            token.ThrowIfCancellationRequested();
            // Materialize through the ordinary atomic writer. Only the verified cache survives
            // the owner's subsequent transaction cleanup; no links or installation output are cached.
            await using var writer = new FileDownloadWriter(request.DestinationPath);
            await using (var input = new FileStream(cached, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            {
                var output = await writer.CreateStreamAsync(0, token).ConfigureAwait(false);
                await input.CopyToAsync(output, token).ConfigureAwait(false);
                await writer.FinishAsync(token).ConfigureAwait(false);
            }
            long size = new FileInfo(cached).Length;
            try { progress?.Invoke(new(DownloadStage.Completed, transfer?.SuccessfulSource ?? "", size, size, 0)); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
            return new(true, request.DestinationPath, transfer?.SuccessfulSource, size, transfer?.Duration ?? TimeSpan.Zero, transfer?.Errors ?? []);
        }
        finally
        {
            if (entered) mutex.Release();
            lock (_cacheGate)
            {
                _cacheUsers--;
                if (_cacheDisposed && _cacheUsers == 0) ClearSessionCache();
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "SHA-1 artifact identities originate in Mojang/CurseForge HTTPS metadata.")]
    private static async Task<bool> MatchesCacheAsync(string path, DownloadArtifactIdentity identity, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (identity.Size is { } size && input.Length != size) return false;
        byte[] hash = identity.Digest.Length switch
        {
            40 => await SHA1.HashDataAsync(input, token).ConfigureAwait(false),
            64 => await SHA256.HashDataAsync(input, token).ConfigureAwait(false),
            _ => await SHA512.HashDataAsync(input, token).ConfigureAwait(false),
        };
        return Convert.ToHexString(hash).Equals(identity.Digest, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteCached(string path)
    {
        File.Delete(path);
        File.Delete(path + ".PCLDownloading");
    }

    public void Dispose()
    {
        lock (_cacheGate)
        {
            if (_cacheDisposed) return;
            _cacheDisposed = true;
            if (_cacheUsers == 0) ClearSessionCache();
        }
    }

    // Only engine-created regular files exist in this private, randomly named directory.
    private void ClearSessionCache()
    {
        if (_cacheRoot is not null)
            try { Directory.Delete(_cacheRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        foreach (var mutex in _cacheLocks.Values) mutex.Dispose();
        _cacheLocks.Clear();
    }
}
