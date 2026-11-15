using Nexa.Sidecar.Host.Peer.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>Session-owned calls to explicitly exported native peer services.</summary>
public sealed class SidecarPeerPool : IAsyncDisposable
{
    public const int PayloadLimit = 60 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<(Guid, long), Entry> _entries = [];
    private readonly Func<DateTimeOffset> _clock;
    private readonly Timer _expiry;
    private int _calls;
    private Task? _disposal;
    private Exception? _cleanupFailure;
    private sealed class Entry(SidecarHostSession caller, SidecarHostSession target, uint contract,
        Func<bool> active, DateTimeOffset used, CancellationToken callerStop, CancellationToken targetStop) : IDisposable
    {
        internal readonly SidecarHostSession Caller = caller, Target = target;
        internal readonly uint Contract = contract;
        internal readonly Func<bool> Active = active;
        internal readonly CancellationToken CallerStop = callerStop, TargetStop = targetStop;
        internal readonly CancellationTokenSource Stop = new();
        internal DateTimeOffset Used = used;
        internal TaskCompletionSource? Pending;
        internal Task? Retirement;
        internal bool IsActive => !CallerStop.IsCancellationRequested && !TargetStop.IsCancellationRequested
            && Caller.State == SidecarSessionState.Active && Target.State == SidecarSessionState.Active && Active();
        public void Dispose() => Stop.Dispose();
    }

    public SidecarPeerPool(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _expiry = new(_ => Sweep(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public HostPeerBinding Bind(SidecarHostSession caller, SidecarHostSession target, HostPeerBind input, Func<bool> active)
    {
        if (input.Id <= 0 || ReferenceEquals(caller, target) || input.PackageName != target.PluginName
            || input.Service is null || input.Service.Length > 256 || !input.Service.StartsWith("sidecar.service.", StringComparison.Ordinal)
            || !active() || !target.NegotiatedFeatures.HasFlag(SidecarFeatures.BinaryPayloads))
            throw new InvalidDataException("Peer service not available.");
        var registration = target.Registration?.TryResolve(SidecarRegistrationKind.Query, XsrSemanticId.Parse(input.Service));
        if (registration is null || registration.CodecId != SidecarWireCodecs.GeneratedDto || registration.ResultCodecId != SidecarWireCodecs.GeneratedDto)
            throw new InvalidDataException("Peer service not exported.");
        var callerStop = caller.CaptureActivationLifetime(); var targetStop = target.CaptureActivationLifetime();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            if (!active() || _entries.ContainsKey((caller.SessionId, input.Id)) || _entries.Count >= 256
                || _entries.Keys.Count(key => key.Item1 == caller.SessionId) >= 32)
                throw new InvalidOperationException("Peer binding not admitted.");
            _entries.Add((caller.SessionId, input.Id), new(caller, target, registration.ContractId, active, _clock(), callerStop, targetStop));
            _expiry.Change(1000, 1000);
            return new(input.Id, target.SessionId.ToString("N"));
        }
    }

    public async ValueTask<HostPeerReply> InvokeAsync(Guid owner, HostPeerCall input, CancellationToken token)
    {
        if (input.Payload is null || input.Payload.Length > PayloadLimit) throw new InvalidDataException("Peer payload exceeds its budget.");
        Entry entry; TaskCompletionSource pending; byte[] copy;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            if (!_entries.TryGetValue((owner, input.Id), out entry!) || entry.Retirement is not null
                || !entry.IsActive || _clock() - entry.Used >= TimeSpan.FromMinutes(2)) throw new IOException("Peer binding retired.");
            if (entry.Pending is not null || _calls >= 64) throw new InvalidOperationException("Peer call budget exceeded.");
            token.ThrowIfCancellationRequested();
            copy = input.Payload.ToArray();
            pending = entry.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); _calls++;
        }
        bool completed = false;
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, entry.Stop.Token, entry.CallerStop, entry.TargetStop);
            stop.CancelAfter(TimeSpan.FromSeconds(30)); stop.Token.ThrowIfCancellationRequested();
            using var writer = new ServiceWireWriter();
            writer.HostPeerEnvelope(new(entry.Caller.PluginName, owner.ToString("N"), copy));
            var result = await entry.Target.SendBinaryQueryByIdAsync(entry.Contract,
                new(SidecarWireCodecs.GeneratedDto, writer.Finish()), TimeSpan.FromSeconds(30), stop.Token).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                if (result.Error?.Kind == XsrErrorKind.Cancelled) throw new OperationCanceledException(stop.Token);
                if (result.Error?.Kind == XsrErrorKind.TimedOut) throw new TimeoutException("Peer service timed out.");
                throw new IOException("Peer service unavailable.");
            }
            if (result.Value is null || result.Value.CodecId != SidecarWireCodecs.GeneratedDto || result.Value.Length > PayloadLimit)
                throw new InvalidDataException("Invalid peer reply.");
            stop.Token.ThrowIfCancellationRequested();
            if (!entry.IsActive) throw new IOException("Peer binding retired.");
            completed = true;
            return new(result.Value.ToArray());
        }
        finally
        {
            Array.Clear(copy);
            lock (_gate)
            {
                entry.Pending = null; entry.Used = _clock(); _calls--; pending.TrySetResult();
                if (!completed || !entry.IsActive) Retire((owner, input.Id), entry);
            }
        }
    }

    public async ValueTask ReleaseAsync(Guid owner, HostPeerRef input, CancellationToken token)
    {
        Task retirement;
        lock (_gate)
            retirement = _entries.TryGetValue((owner, input.Id), out var entry) ? Retire((owner, input.Id), entry) : Task.CompletedTask;
        await retirement.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
    }

    private Task Retire((Guid, long) key, Entry entry)
    {
        if (entry.Retirement is not null) return entry.Retirement;
        Task admitted = entry.Pending?.Task ?? Task.CompletedTask;
        // Run cancellation outside the registry lock, including synchronous callbacks.
        entry.Retirement = Task.Run(async () =>
        {
            try { await Task.WhenAll(entry.Stop.CancelAsync(), admitted).ConfigureAwait(false); }
            finally
            {
                lock (_gate) { _entries.Remove(key); if (_entries.Count == 0 && _disposal is null) _expiry.Change(Timeout.Infinite, Timeout.Infinite); }
                entry.Dispose();
            }
        });
        _ = Observe(entry.Retirement);
        return entry.Retirement;
    }

    private async Task Observe(Task retirement)
    {
        try { await retirement.ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { lock (_gate) _cleanupFailure ??= error; }
    }

    private void Sweep()
    {
        lock (_gate)
        {
            if (_disposal is not null) return;
            foreach (var (key, entry) in _entries)
                if (!entry.IsActive || _clock() - entry.Used >= TimeSpan.FromMinutes(2)) Retire(key, entry);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is null)
            {
                _expiry.Dispose();
                var retiring = _entries.Select(pair => Retire(pair.Key, pair.Value)).ToArray();
                _disposal = Task.Run(async () =>
                {
                    await Task.WhenAll(retiring).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (_cleanupFailure is { } error) throw new IOException("Peer cleanup failed.", error);
                });
            }
            return new(_disposal);
        }
    }
}
