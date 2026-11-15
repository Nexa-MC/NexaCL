using System.Security.Cryptography;
using Nexa.Core.Media;
using Nexa.Services.Composition.Sidecars;
using Nexa.Xsr.Runtime;
using SkiaSharp;

namespace Nexa.Desktop;

/// <summary>Bounded upload storage. No UI tree or guest policy is involved.</summary>
internal sealed class DesktopSidecarRasterPool : IDisposable
{
    private const int MaximumImageBytes = 16 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<(SidecarHostSession, long), Resource> _resources = [];
    private readonly Func<long> _clock;
    private readonly Timer _sweep;
    private readonly SemaphoreSlim _decodes = new(2, 2);
    private readonly CancellationTokenSource _stop = new();
    private bool _closed;
    internal DesktopSidecarRasterPool(Func<long>? clock = null)
    { _clock = clock ?? (() => Environment.TickCount64); _sweep = new(_ => Sweep(), null, Timeout.Infinite, Timeout.Infinite); }
    internal int Count { get { lock (_gate) return _resources.Count; } }
    internal long Bytes { get { lock (_gate) return _resources.Values.Sum(item => (long)item.Length); } }

    internal sealed class Resource(SidecarHostSession owner, HostRasterBegin input, long expires)
    {
        internal readonly SidecarHostSession Owner = owner;
        internal readonly long Id = input.Id;
        internal readonly int Length = input.Length;
        internal readonly byte[] Digest = input.Sha256.ToArray();
        internal byte[]? Buffer = new byte[input.Length];
        internal PngImage? Image;
        internal int Received, Pins;
        internal bool Sealing, Retired;
        internal long Expires = expires;
    }

    internal void Begin(SidecarHostSession owner, HostRasterBegin input, CancellationToken token)
    {
        if (input.Id <= 0 || input.Length is < 1 or > MaximumImageBytes || input.Sha256 is not { Length: 32 })
            throw new ArgumentException("Invalid raster upload metadata.");
        lock (_gate)
        {
            RequireActive(owner, token); SweepLocked();
            if (_resources.ContainsKey((owner, input.Id))) throw new InvalidOperationException("Raster identifier already exists.");
            var owned = _resources.Values.Where(item => item.Owner == owner).ToArray();
            if (_resources.Count >= 64 || owned.Length >= 16
                || _resources.Values.Sum(item => (long)item.Length) + input.Length > 128L * 1024 * 1024
                || owned.Sum(item => (long)item.Length) + input.Length > 64L * 1024 * 1024)
                throw new InvalidOperationException("Raster upload budget exceeded.");
            _resources.Add((owner, input.Id), new(owner, input, _clock() + 30000));
            if (_resources.Count == 1) _sweep.Change(1000, 1000);
        }
    }

    internal void Append(SidecarHostSession owner, HostRasterAppend input, CancellationToken token)
    {
        if (input.Bytes is not { Length: > 0 and <= 65536 }) throw new ArgumentException("Invalid raster chunk.");
        lock (_gate)
        {
            RequireActive(owner, token); SweepLocked(); var item = Find(owner, input.Id);
            if (item.Buffer is null || item.Sealing || input.Offset != item.Received || input.Bytes.Length > item.Length - item.Received)
                throw new InvalidOperationException("Raster chunks must be sequential and fit the upload.");
            input.Bytes.CopyTo(item.Buffer, item.Received); item.Received += input.Bytes.Length;
            item.Expires = _clock() + 30000;
        }
    }

    internal async ValueTask SealAsync(SidecarHostSession owner, long id, CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        await _decodes.WaitAsync(stop.Token).ConfigureAwait(false);
        try { await Task.Run(() => Seal(owner, id, stop.Token), stop.Token).ConfigureAwait(false); }
        finally { _decodes.Release(); }
    }

    private void Seal(SidecarHostSession owner, long id, CancellationToken token)
    {
        Resource item; byte[] bytes;
        lock (_gate)
        {
            RequireActive(owner, token); SweepLocked(); item = Find(owner, id);
            if (item.Buffer is null || item.Sealing || item.Received != item.Length)
                throw new InvalidOperationException("Raster upload is not complete.");
            item.Sealing = true; bytes = item.Buffer;
        }
        try
        {
            token.ThrowIfCancellationRequested();
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), item.Digest))
                throw new InvalidDataException("Raster digest mismatch.");
            var image = PngImage.TryCreatePreview(bytes);
            if (image is null || image.Width is < 1 or > 2048 || image.Height is < 1 or > 2048)
                throw new InvalidDataException("Invalid raster PNG.");
            using (var data = SKData.CreateCopy(bytes))
            using (var codec = SKCodec.Create(data))
            {
                if (codec is null || codec.Info.Width != image.Width || codec.Info.Height != image.Height)
                    throw new InvalidDataException("Invalid raster decoder metadata.");
                using var decoded = new SKBitmap(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
                    throw new InvalidDataException("Incomplete raster PNG.");
            }
            lock (_gate)
            {
                RequireActive(owner, token);
                if (item.Retired) throw new InvalidOperationException("Raster upload retired during validation.");
                item.Image = image; item.Expires = _clock() + 30000;
                CryptographicOperations.ZeroMemory(bytes); item.Buffer = null;
            }
        }
        catch
        {
            lock (_gate) item.Retired = true;
            throw;
        }
        finally
        {
            lock (_gate)
            {
                item.Sealing = false;
                if (item.Retired) Remove(item);
            }
        }
    }

    /// <summary>Pins a complete candidate until UI validation either commits it or releases every pin.</summary>
    internal Resource[] Acquire(SidecarHostSession owner, IReadOnlyList<long> ids, CancellationToken token)
    {
        lock (_gate)
        {
            RequireActive(owner, token); SweepLocked();
            var items = ids.Select(id => Find(owner, id)).ToArray();
            if (items.Any(item => item.Image is null || item.Sealing)) throw new InvalidOperationException("Raster is not ready.");
            foreach (var item in items) item.Pins++;
            return items;
        }
    }

    internal bool IsLive(Resource item)
    { lock (_gate) return !_closed && !item.Retired && item.Owner.State == SidecarSessionState.Active; }

    internal void Unpin(Resource item)
    {
        lock (_gate)
        {
            if (item.Pins <= 0) throw new InvalidOperationException("Raster pin imbalance.");
            item.Pins--; item.Expires = _clock() + 30000;
            if (item.Retired && item.Pins == 0) item.Image = null;
        }
    }

    internal void Release(SidecarHostSession owner, long id, CancellationToken token)
    {
        lock (_gate)
        {
            RequireActive(owner, token); SweepLocked();
            if (!_resources.TryGetValue((owner, id), out var item)) return;
            if (item.Pins != 0) throw new InvalidOperationException("Bound raster must be removed before release.");
            Remove(item);
        }
    }

    private void RequireActive(SidecarHostSession owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_closed || owner.State != SidecarSessionState.Active) throw new InvalidOperationException("Raster session is not active.");
    }
    private Resource Find(SidecarHostSession owner, long id)
        => _resources.TryGetValue((owner, id), out var item) && !item.Retired ? item : throw new InvalidOperationException("Raster not found.");
    internal void Sweep() { lock (_gate) { if (!_closed) SweepLocked(); } }
    private void SweepLocked()
    {
        long now = _clock();
        foreach (var item in _resources.Values.Where(item => item.Owner.State != SidecarSessionState.Active || item.Pins == 0 && item.Expires <= now).ToArray()) Remove(item);
    }
    private void Remove(Resource item)
    {
        item.Retired = true;
        if (item.Sealing) return; // Keep the reservation and bytes until the validating caller exits.
        _resources.Remove((item.Owner, item.Id));
        if (!_closed && _resources.Count == 0) _sweep.Change(Timeout.Infinite, Timeout.Infinite);
        if (item.Buffer is { } bytes) CryptographicOperations.ZeroMemory(bytes);
        item.Buffer = null;
        if (item.Pins == 0) item.Image = null;
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            foreach (var item in _resources.Values.ToArray()) Remove(item);
        }
        _sweep.Dispose(); _stop.Cancel(); _stop.Dispose();
    }
}
