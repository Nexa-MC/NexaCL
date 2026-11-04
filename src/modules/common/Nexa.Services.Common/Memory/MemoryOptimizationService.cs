using System.Diagnostics;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Memory;

/// <summary>Bounded, cancelable working-set cleanup, with optional elevated system operations. No process is terminated.</summary>
public sealed class MemoryOptimizationService : IAsyncDisposable
{
    private readonly IProcessMemoryPort _port;
    private readonly Func<MemoryOptimizationOptions> _options;
    private readonly Func<IReadOnlySet<int>> _games;
    private readonly XsrStateStore _store;
    private readonly XsrStateId _state;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly object _gate = new();
    private Task? _monitor;
    private bool _disposed;
    private DateTimeOffset? _lastAutomatic;
    private MemoryOptimizationSnapshot _snapshot;
    private long _launchCleanupGeneration;
    private XsrResult _launchCleanupResult = XsrResult.Success();
    public bool IsSupported => _port.IsSupported;
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    public MemoryOptimizationService(IProcessMemoryPort port, Func<MemoryOptimizationOptions> options,
        Func<IReadOnlySet<int>> games, XsrStateStore store, TimeProvider? clock = null)
    {
        _port = port; _options = options; _games = games; _store = store;
        _clock = clock ?? TimeProvider.System; _state = store.Resolve(MemoryOptimizationContract.Snapshot);
        _snapshot = new(port.IsSupported, default, false, port.IsSupported ? "waiting" : "unsupported");
        Publish(_snapshot);
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_port.IsSupported) _monitor ??= Task.Run(MonitorAsync);
        }
    }

    private async Task MonitorAsync()
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(5), _clock);
            do { await ObserveAsync(_stop.Token).ConfigureAwait(false); }
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async Task ObserveAsync(CancellationToken token = default)
    {
        if (!_port.IsSupported) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        token = linked.Token;
        if (!await _operation.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            token.ThrowIfCancellationRequested();
            var sample = _port.Sample();
            Publish(_snapshot with { Memory = sample, Status = sample.IsValid ? _snapshot.Status : "unavailable" });
            var options = _options();
            if (!sample.IsValid || !options.Automatic || sample.UsedPercent <= Math.Clamp(options.ThresholdPercent, 50, 95)
                || _lastAutomatic is { } last && _clock.GetUtcNow() - last < Cooldown) return;
            _lastAutomatic = _clock.GetUtcNow();
            await CleanupCoreAsync(token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Publish(_snapshot with { Busy = false, Status = ErrorStatus(error) }); }
        finally { _operation.Release(); }
    }

    public async ValueTask<XsrResult> CleanAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        if (!_port.IsSupported) return Failure("unsupported", "Memory cleanup requires Windows.");
        if (!await _operation.WaitAsync(0, linked.Token).ConfigureAwait(false))
            return Failure("busy", "Memory cleanup is already running.");
        try
        {
            _lastAutomatic = _clock.GetUtcNow();
            await Task.Run(() => CleanupCoreAsync(linked.Token), linked.Token).ConfigureAwait(false);
            return _snapshot.Status == "unavailable" ? Failure("unavailable", "System memory could not be measured.") : XsrResult.Success();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Publish(_snapshot with { Busy = false, Status = ErrorStatus(error) }); return Failure(ErrorStatus(error), error.Message); }
        finally { _operation.Release(); }
    }

    public async ValueTask<XsrResult> CleanBeforeLaunchAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        if (!_port.IsSupported) return XsrResult.Success();
        long observed = Interlocked.Read(ref _launchCleanupGeneration);
        await _operation.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (observed != Interlocked.Read(ref _launchCleanupGeneration)) return _launchCleanupResult;
            try
            {
                await Task.Run(() => CleanupCoreAsync(linked.Token, forceDeep: true), linked.Token).ConfigureAwait(false);
                _launchCleanupResult = _snapshot.Status == "unavailable" ? Failure("unavailable", "System memory could not be measured.") : XsrResult.Success();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                Publish(_snapshot with { Busy = false, Status = ErrorStatus(error) });
                _launchCleanupResult = Failure(ErrorStatus(error), error.Message);
            }
            Interlocked.Increment(ref _launchCleanupGeneration);
            return _launchCleanupResult;
        }
        finally { _operation.Release(); }
    }

    private async Task CleanupCoreAsync(CancellationToken token, bool forceDeep = false)
    {
        long started = Stopwatch.GetTimestamp();
        var before = _port.Sample();
        if (!before.IsValid) { Publish(_snapshot with { Status = "unavailable" }); return; }
        Publish(_snapshot with { Memory = before, Busy = true, Status = "cleaning" });
        try
        {
            if (forceDeep || _options().Deep)
            {
                if (_port is not IElevatedMemoryCleanupPort elevated) throw new IOException("Elevated memory cleanup is unavailable.");
                var result = await elevated.CleanElevatedAsync(_games(), token).ConfigureAwait(false);
                Publish(new(true, _port.Sample(), false, "complete", result));
                return;
            }
            var processes = _port.CaptureProcesses();
            int cleaned = 0, skipped = 0, failed = 0;
            long reduction = 0;
            var protectedIds = new HashSet<int>();
            foreach (var process in processes)
                if (process.IsProtected || IsGameName(process.Name)) protectedIds.Add(process.Id);
            protectedIds.UnionWith(_games());
            ProtectDescendants(processes, protectedIds);
            foreach (var process in processes)
            {
                token.ThrowIfCancellationRequested();
                // Read live game roots again: a game may start after enumeration.
                int protectedCount = protectedIds.Count;
                protectedIds.UnionWith(_games());
                if (protectedIds.Count != protectedCount) ProtectDescendants(processes, protectedIds);
                if (protectedIds.Contains(process.Id)) { skipped++; continue; }
                if (_port.TryTrim(process, out long released)) { cleaned++; reduction += Math.Max(0, released); }
                else failed++;
                await Task.Delay(TimeSpan.FromMilliseconds(15), _clock, token).ConfigureAwait(false);
            }
            var after = _port.Sample();
            Publish(new(true, after, false, "complete", new(cleaned, skipped, failed, reduction,
                after.IsValid ? after.AvailableBytes - before.AvailableBytes : 0)
            { ElapsedMilliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds }));
        }
        finally
        {
            _lastAutomatic = _clock.GetUtcNow();
            if (_snapshot.Busy) Publish(_snapshot with { Busy = false, Status = "cancelled" });
        }
    }

    public static bool IsGameName(string name) => Path.GetFileNameWithoutExtension(name).ToLowerInvariant()
        is "java" or "javaw" or "minecraft.windows" or "minecraft" or "nexa.jvm.host";

    public static void ProtectDescendants(IReadOnlyList<MemoryProcess> processes, HashSet<int> protectedIds)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var process in processes)
                if (protectedIds.Contains(process.ParentId)) changed |= protectedIds.Add(process.Id);
        } while (changed);
    }

    private void Publish(MemoryOptimizationSnapshot snapshot) { _snapshot = snapshot; _store.Publish(_state, snapshot); }
    private static string ErrorStatus(Exception error) => error is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }
        ? "elevation-cancelled" : "unavailable";
    private static XsrResult Failure(string code, string message) => XsrResult.Failure(
        new(XsrErrorKind.Unavailable, XsrSemanticId.Parse("memory.optimization." + code), message));

    public async ValueTask DisposeAsync()
    {
        Task? monitor;
        lock (_gate) { if (_disposed) return; _disposed = true; monitor = _monitor; }
        await _stop.CancelAsync().ConfigureAwait(false);
        if (monitor is not null) await monitor.ConfigureAwait(false);
        await _operation.WaitAsync().ConfigureAwait(false);
        _operation.Release();
        // Keep cancellation/gate objects alive for an already-dispatched command to observe shutdown.
    }
}
