using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Nexa.Sidecar.Host.Clipboard.Wire;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>Bounded clipboard snapshots and staged writes, owned by a Sidecar activation.</summary>
public sealed partial class SidecarClipboardApi : IDisposable
{
    private const int ImageLimit = 16 * 1024 * 1024, TextLimit = 65536, ChunkLimit = 256 * 1024;
    private const long StorageLimit = 64 * 1024 * 1024, ReadReservation = ImageLimit + TextLimit * 6;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly object _gate = new();
    private readonly Func<SidecarHostCaller, SidecarHostSession?> _resolve;
    private readonly Func<SidecarHostCaller, string, bool> _authorize;
    private readonly IXsrDispatchObserver _observer;
    private readonly TimeProvider _clock;
    private readonly Dictionary<(SidecarHostSession, CancellationToken), Owner> _owners = [];
    private readonly HashSet<Transfer> _transfers = [];
    private Binding? _binding;
    private bool _disposed, _nativeBusy;
    private long _reserved;
    public IReadOnlyList<SidecarHostRequestRoute> Routes { get; }

    public SidecarClipboardApi(Func<SidecarHostCaller, SidecarHostSession?> resolve, IXsrDispatchObserver observer,
        TimeProvider? clock = null)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _clock = clock ?? TimeProvider.System;
        _authorize = (caller, _) => Resolve(caller) is { } session && session.NegotiatedFeatures.HasFlag(SidecarFeatures.LargeGeneratedDtos);
        Routes = Array.AsReadOnly(CreateRoutes().Append(new(3071, true, SidecarWireCodecs.GeneratedDto,
            SidecarWireCodecs.GeneratedDto, TimeSpan.FromSeconds(10), caller => Resolve(caller) is not null,
            (caller, input, _) =>
            {
                if (input.Length != 0) throw new InvalidDataException("Catalog request must be empty.");
                using var writer = new ServiceWireWriter();
                writer.UInt32(1); writer.String(ServiceWireSchema.Fingerprint + ":" + CatalogFingerprint);
                var contracts = Available(caller); writer.Count(contracts.Count);
                foreach (uint id in contracts) writer.UInt32(id);
                return ValueTask.FromResult(XsrResult.Success(new SidecarBinaryValue(SidecarWireCodecs.GeneratedDto, writer.Finish())));
            })).ToArray());
    }
    public IDisposable Bind(HostClipboardActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.Read); ArgumentNullException.ThrowIfNull(actions.Write);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_binding is not null) throw new InvalidOperationException("Clipboard is already bound.");
            return _binding = new(this, actions);
        }
    }
    private SidecarHostSession? Resolve(SidecarHostCaller caller)
    {
        lock (_gate) if (_disposed) return null;
        var session = _resolve(caller);
        if (session is null || session.PluginName != caller.PackageName || session.SessionId != caller.SessionId) return null;
        try { session.CaptureActivationLifetime().ThrowIfCancellationRequested(); return session; }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException) { return null; }
    }
    private Owner? GetOwner(SidecarHostCaller caller)
    {
        var session = Resolve(caller);
        if (session is null || !session.NegotiatedFeatures.HasFlag(SidecarFeatures.LargeGeneratedDtos)) return null;
        CancellationToken lifetime;
        try { lifetime = session.CaptureActivationLifetime(); }
        catch (InvalidOperationException) { return null; }
        Owner owner;
        lock (_gate)
        {
            if (_disposed || lifetime.IsCancellationRequested) return null;
            if (!_owners.TryGetValue((session, lifetime), out owner!))
            {
                if (_owners.Count >= 128) return null;
                _owners.Add((session, lifetime), owner = new(this, session, lifetime));
            }
        }
        owner.Observe(); return owner;
    }
    private XsrQueryRouter? Queries(string domain, SidecarHostCaller caller)
    {
        if (domain != "clipboard" || !_authorize(caller, domain)) return null;
        XsrQueryRouterBuilder routes = new();
        routes.Register<HostClipboardTransfer, HostClipboardSnapshot>(SidecarClipboardRoutes.Read, (input, token) => Read(caller, input, token));
        routes.Register<HostClipboardReadChunk, HostClipboardChunk>(SidecarClipboardRoutes.ReadChunk, (input, token) => ReadChunk(caller, input, token));
        return routes.Build(_observer);
    }
    private XsrCommandRouter? Commands(string domain, SidecarHostCaller caller)
    {
        if (domain != "clipboard" || !_authorize(caller, domain)) return null;
        XsrCommandRouterBuilder routes = new();
        routes.Register<HostClipboardWrite>(SidecarClipboardRoutes.BeginWrite, (input, token) => BeginWrite(caller, input, token));
        routes.Register<HostClipboardAppend>(SidecarClipboardRoutes.Append, (input, token) => Append(caller, input, token));
        routes.Register<HostClipboardTransfer>(SidecarClipboardRoutes.Commit, (input, token) => Commit(caller, input, token));
        routes.Register<HostClipboardTransfer>(SidecarClipboardRoutes.Release, (input, token) => Release(caller, input, token));
        return routes.Build(_observer);
    }
    private XsrResult<Transfer> Reserve(SidecarHostCaller caller, long id, long bytes, bool read)
    {
        if (id <= 0) return XsrResult.Failure<Transfer>(Rejected("A positive transfer ID is required."));
        var owner = GetOwner(caller);
        if (owner is null) return XsrResult.Failure<Transfer>(Unavailable());
        lock (_gate)
        {
            if (!owner.IsActive) return XsrResult.Failure<Transfer>(XsrRuntimeErrors.Cancelled());
            if (id <= owner.LastId) return XsrResult.Failure<Transfer>(Rejected("Transfer IDs must increase and cannot be reused."));
            owner.LastId = id;
            if (_binding is not { } binding) return XsrResult.Failure<Transfer>(Unavailable());
            if (_transfers.Count >= 8 || owner.Transfers.Count >= 2 || _reserved + bytes > StorageLimit || read && _nativeBusy)
                return XsrResult.Failure<Transfer>(new(XsrErrorKind.Backpressure, XsrSemanticId.Parse("xsr.backpressure"), "Clipboard transfer capacity is exhausted."));
            var transfer = new Transfer(owner, binding, id, bytes, read);
            _reserved += bytes; _transfers.Add(transfer); owner.Transfers.Add(transfer);
            if (read) _nativeBusy = true;
            return XsrResult.Success(transfer);
        }
    }
    private async ValueTask<XsrResult<HostClipboardSnapshot>> Read(SidecarHostCaller caller, HostClipboardTransfer input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var reserved = Reserve(caller, input.TransferId, ReadReservation, true);
        if (!reserved.IsSuccess) return XsrResult.Failure<HostClipboardSnapshot>(reserved.Error!);
        var transfer = reserved.Value; transfer.Observe();
        using var cancelled = token.Register(transfer.Retire);
        bool retained = false;
        try
        {
            var result = await RunRead().WaitAsync(transfer.Lifetime).ConfigureAwait(false);
            transfer.Lifetime.ThrowIfCancellationRequested();
            retained = result.IsSuccess; return result;
        }
        catch (OperationCanceledException) { return XsrResult.Failure<HostClipboardSnapshot>(XsrRuntimeErrors.Cancelled()); }
        finally { if (!retained) transfer.Retire(); }

        async Task<XsrResult<HostClipboardSnapshot>> RunRead()
        {
            await Task.Yield();
            try
            {
                transfer.Lifetime.ThrowIfCancellationRequested();
                var (text, image) = await transfer.Binding.Actions.Read(transfer.Lifetime).ConfigureAwait(false);
                transfer.Lifetime.ThrowIfCancellationRequested();
                if (text?.Length > TextLimit) throw new InvalidDataException("Clipboard text exceeds its budget.");
                byte[]? encoded = text is null ? null : Utf8.GetBytes(text);
                if (image is not null) ValidatePng(image);
                // The native provider may reuse its own buffer. Own an immutable snapshot.
                byte[]? copy = image?.ToArray(), hash = copy is null ? null : SHA256.HashData(copy);
                transfer.Lifetime.ThrowIfCancellationRequested();
                lock (_gate) { transfer.Image = copy; transfer.Ready = true; transfer.FinalReservation = copy?.Length ?? 0; }
                return XsrResult.Success(new HostClipboardSnapshot(encoded, copy?.Length ?? -1, hash));
            }
            catch (OperationCanceledException) { return XsrResult.Failure<HostClipboardSnapshot>(XsrRuntimeErrors.Cancelled()); }
            catch (Exception error) when (Nonfatal(error)) { return XsrResult.Failure<HostClipboardSnapshot>(Unavailable()); }
            finally { transfer.FinishWork(); }
        }
    }
    private ValueTask<XsrResult<HostClipboardChunk>> ReadChunk(SidecarHostCaller caller, HostClipboardReadChunk input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var owner = GetOwner(caller);
        lock (_gate)
        {
            var transfer = Find(owner, input.TransferId);
            if (transfer is null || !transfer.IsRead || transfer.Image is not { } image || input.Offset < 0
                || input.Length is < 1 or > ChunkLimit || input.Offset > image.Length - input.Length)
                return ValueTask.FromResult(XsrResult.Failure<HostClipboardChunk>(Rejected("Invalid clipboard snapshot range.")));
            return ValueTask.FromResult(XsrResult.Success(new HostClipboardChunk(input.Offset, image.AsSpan(input.Offset, input.Length).ToArray())));
        }
    }
    private async ValueTask<XsrResult> BeginWrite(SidecarHostCaller caller, HostClipboardWrite input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string? text;
        try
        {
            if (input.TextUtf8?.Length > TextLimit * 4 || !ValidImageDescriptor(input.ImageLength, input.ImageSha256))
                return XsrResult.Failure(Rejected("Invalid clipboard write descriptor."));
            text = input.TextUtf8 is null ? null : Utf8.GetString(input.TextUtf8);
            if (text?.Length > TextLimit) return XsrResult.Failure(Rejected("Clipboard text exceeds its budget."));
        }
        catch (DecoderFallbackException) { return XsrResult.Failure(Rejected("Clipboard text must be UTF-8.")); }
        var reserved = Reserve(caller, input.TransferId, Math.Max(0, input.ImageLength) + (text?.Length ?? 0) * 2L, false);
        if (!reserved.IsSuccess) return XsrResult.Failure(reserved.Error!);
        var transfer = reserved.Value; transfer.Observe();
        using var cancelled = token.Register(transfer.Retire);
        bool retained = false;
        try
        {
            var result = await Prepare().WaitAsync(transfer.Lifetime).ConfigureAwait(false);
            transfer.Lifetime.ThrowIfCancellationRequested(); retained = result.IsSuccess; return result;
        }
        catch (OperationCanceledException) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        finally { if (!retained) transfer.Retire(); }

        async Task<XsrResult> Prepare()
        {
            await Task.Yield();
            try
            {
                transfer.Lifetime.ThrowIfCancellationRequested();
                var image = input.ImageLength < 0 ? null : new byte[input.ImageLength];
                var hash = input.ImageSha256?.ToArray();
                transfer.Lifetime.ThrowIfCancellationRequested();
                lock (_gate) { transfer.Text = text; transfer.Image = image; transfer.Hash = hash; transfer.Ready = true; }
                return XsrResult.Success();
            }
            catch (OperationCanceledException) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
            catch (Exception error) when (Nonfatal(error)) { return XsrResult.Failure(Unavailable()); }
            finally { transfer.FinishWork(); }
        }
    }
    private ValueTask<XsrResult> Append(SidecarHostCaller caller, HostClipboardAppend input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var owner = GetOwner(caller);
        lock (_gate)
        {
            var transfer = Find(owner, input.TransferId);
            if (transfer is null || transfer.IsRead || transfer.Committed || transfer.Image is not { } image
                || input.Bytes is null || input.Bytes.Length is < 1 or > ChunkLimit || input.Offset != transfer.Received
                || input.Bytes.Length > image.Length - transfer.Received)
                return ValueTask.FromResult(XsrResult.Failure(Rejected("Invalid clipboard append range.")));
            input.Bytes.CopyTo(image, transfer.Received); transfer.Received += input.Bytes.Length;
            return ValueTask.FromResult(XsrResult.Success());
        }
    }
    private async ValueTask<XsrResult> Commit(SidecarHostCaller caller, HostClipboardTransfer input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var owner = GetOwner(caller); Transfer transfer;
        lock (_gate)
        {
            var candidate = Find(owner, input.TransferId);
            if (candidate is null || candidate.IsRead || candidate.Committed || candidate.Received != (candidate.Image?.Length ?? 0))
                return XsrResult.Failure(Rejected("Clipboard write is incomplete or already committed."));
            if (_nativeBusy) return XsrResult.Failure(new(XsrErrorKind.Backpressure, XsrSemanticId.Parse("xsr.backpressure"), "The native clipboard is busy."));
            transfer = candidate; transfer.Committed = true; transfer.Working = true;
            transfer.Work = new(TaskCreationOptions.RunContinuationsAsynchronously);
            transfer.NativeOperation = true; _nativeBusy = true;
        }
        using var cancelled = token.Register(transfer.Retire);
        try
        {
            var result = await RunCommit().WaitAsync(transfer.Lifetime).ConfigureAwait(false);
            transfer.Lifetime.ThrowIfCancellationRequested(); return result;
        }
        catch (OperationCanceledException) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        finally { transfer.Retire(); }

        async Task<XsrResult> RunCommit()
        {
            await Task.Yield();
            try
            {
                transfer.Lifetime.ThrowIfCancellationRequested();
                if (transfer.Image is { } image)
                {
                    if (!SHA256.HashData(image).AsSpan().SequenceEqual(transfer.Hash)) return XsrResult.Failure(Rejected("Clipboard image digest mismatch."));
                    ValidatePng(image);
                }
                transfer.Lifetime.ThrowIfCancellationRequested();
                await transfer.Binding.Actions.Write(transfer.Text, transfer.Image, transfer.Lifetime).ConfigureAwait(false);
                transfer.Lifetime.ThrowIfCancellationRequested(); return XsrResult.Success();
            }
            catch (OperationCanceledException) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
            catch (InvalidDataException) { return XsrResult.Failure(Rejected("Invalid clipboard image.")); }
            catch (Exception error) when (Nonfatal(error)) { return XsrResult.Failure(Unavailable()); }
            finally { transfer.FinishWork(); }
        }
    }
    private ValueTask<XsrResult> Release(SidecarHostCaller caller, HostClipboardTransfer input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (input.TransferId <= 0) return ValueTask.FromResult(XsrResult.Failure(Rejected("A positive transfer ID is required.")));
        var owner = GetOwner(caller); Transfer? transfer;
        lock (_gate)
        {
            if (owner?.IsActive != true) return ValueTask.FromResult(XsrResult.Failure(Unavailable()));
            owner.LastId = Math.Max(owner.LastId, input.TransferId);
            transfer = owner.Transfers.FirstOrDefault(value => value.Id == input.TransferId);
        }
        transfer?.Retire(); return ValueTask.FromResult(XsrResult.Success());
    }
    // Called only under the admission gate. Working includes the final completion signal,
    // so a pipelined commit cannot replace the initial worker's completion source.
    private Transfer? Find(Owner? owner, long id) => owner?.IsActive == true
        ? owner.Transfers.FirstOrDefault(value => value.Id == id && !value.Retired && !value.Lifetime.IsCancellationRequested
            && ReferenceEquals(value.Binding, _binding) && value.Ready && !value.Working && value.Work.Task.IsCompleted) : null;
    private static bool ValidImageDescriptor(int length, byte[]? hash) => length == -1 ? hash is null : length is >= 33 and <= ImageLimit && hash?.Length == 32;
    private static void ValidatePng(byte[] image)
    {
        if (image.Length is < 33 or > ImageLimit || !image.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(8, 4)) != 13 || !image.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("A bounded PNG is required.");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(16, 4)), height = BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(20, 4));
        if (width is 0 or > 4096 || height is 0 or > 4096 || (ulong)width * height > 4 * 1024 * 1024)
            throw new InvalidDataException("Clipboard image exceeds its pixel budget.");
    }
    private static bool Nonfatal(Exception error) => error is not OutOfMemoryException and not AccessViolationException;
    private static XsrError Rejected(string message) => new(XsrErrorKind.Rejected, XsrSemanticId.Parse("xsr.rejected"), message);
    private static XsrError Unavailable() => new(XsrErrorKind.Unavailable, XsrSemanticId.Parse("xsr.unavailable"), "Clipboard is unavailable.");
    private static void WriteOutcome(ServiceWireWriter writer, XsrError? error)
    {
        writer.Boolean(error is null); if (error is null) return;
        writer.Int32((int)error.Kind); writer.String(error.Code.Value);
    }
    public void Dispose()
    {
        Binding? binding; Owner[] owners;
        lock (_gate) { if (_disposed) return; _disposed = true; binding = _binding; _binding = null; owners = _owners.Values.ToArray(); }
        binding?.Dispose(); foreach (var owner in owners) owner.Retire();
    }
}
