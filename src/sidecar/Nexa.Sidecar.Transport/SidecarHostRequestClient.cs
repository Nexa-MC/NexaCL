using Nexa.Sidecar.Protocol;

namespace Nexa.Sidecar.Transport;

/// <summary>A bounded client for the existing bidirectional Host request frames.</summary>
/// <remarks>The connection owner remains the only frame reader and passes replies to TryAcceptReply.
/// Cancellation after sending a command leaves its effect unknown; this client never retries it.</remarks>
public sealed class SidecarHostRequestClient : IDisposable
{
    private readonly Func<SidecarFrame, CancellationToken, ValueTask> _send;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Pending> _pending = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _active, _closed;
    private SidecarFeatures _features;
    public const int MaximumPendingRequests = 32;
    private sealed record Pending(SidecarMessageType Reply, uint Codec, TaskCompletionSource<SidecarHostReply> Completion);

    public SidecarHostRequestClient(Func<SidecarFrame, CancellationToken, ValueTask> send)
        => _send = send ?? throw new ArgumentNullException(nameof(send));

    public void Activate(SidecarFeatures negotiatedFeatures)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            const SidecarFeatures required = SidecarFeatures.BinaryPayloads | SidecarFeatures.HostRequests;
            if ((negotiatedFeatures & required) != required)
                throw new InvalidOperationException("The peer did not negotiate binary Host requests.");
            if (_active) throw new InvalidOperationException("The Host request client is already active.");
            _active = true;
            _features = negotiatedFeatures;
        }
    }

    public async ValueTask<SidecarHostReply> InvokeAsync(uint contractId, bool query, SidecarBinaryValue input,
        uint outputCodec, TimeSpan timeout, CancellationToken cancellation = default)
    {
        if (contractId == 0 || timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(contractId));
        cancellation.ThrowIfCancellationRequested();
        var correlation = SidecarCorrelationId.Create();
        var pending = new Pending(query ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
            outputCodec, new(TaskCreationOptions.RunContinuationsAsynchronously));
        // Validate the input before consuming a request slot.
        var frame = new SidecarFrame(SidecarProtocol.Version,
            query ? SidecarMessageType.QueryRequest : SidecarMessageType.CommandRequest,
            SidecarFrameTraits.None, correlation, SidecarDataMessages.EncodeBinaryRequest(contractId, input));
        CancellationToken lifetime;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (!_active) throw new InvalidOperationException("Host requests require an active session.");
            if (input.Length > SidecarWireCodecs.MaximumValueLength && !_features.HasFlag(SidecarFeatures.LargeGeneratedDtos))
                throw new SidecarProtocolException("Large Host requests were not negotiated.");
            if (_pending.Count >= MaximumPendingRequests) throw new SidecarTransportBackpressureException();
            _pending.Add(correlation.Value, pending);
            lifetime = _lifetime.Token;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime);
        deadline.CancelAfter(timeout);
        Task sending;
        try { sending = _send(frame, lifetime).AsTask(); }
        catch { lock (_gate) _pending.Remove(correlation.Value); throw; }
        try
        {
            // The caller can stop waiting without interrupting a partially written frame.
            await sending.WaitAsync(deadline.Token).ConfigureAwait(false);
            return await pending.Completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _ = ObserveSendAsync(sending, correlation.Value);
            if (lifetime.IsCancellationRequested) throw;
            // Retain the slot until the peer answers: cancellation cannot evade backpressure
            // or make a valid late reply look like a reply for a different request.
            using var sendDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            sendDeadline.CancelAfter(TimeSpan.FromSeconds(2));
            Task? cancelling = null;
            try
            {
                cancelling = _send(new(SidecarProtocol.Version, SidecarMessageType.Cancel, SidecarFrameTraits.None,
                    correlation, SidecarControlMessages.EncodeCancel(correlation.Value, "request_cancelled")), lifetime).AsTask();
                await cancelling.WaitAsync(sendDeadline.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
            finally { if (cancelling is not null) _ = ObserveCancelAsync(cancelling); }
            throw;
        }
        catch { lock (_gate) _pending.Remove(correlation.Value); throw; }
    }

    private async Task ObserveSendAsync(Task sending, Guid correlation)
    {
        try { await sending.ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { lock (_gate) _pending.Remove(correlation); }
    }

    private static async Task ObserveCancelAsync(Task sending)
    {
        try { await sending.ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
    }

    public bool TryAcceptReply(SidecarFrame frame)
    {
        Pending? pending;
        lock (_gate)
        {
            if (!_pending.TryGetValue(frame.CorrelationId.Value, out pending)) return false;
            if (frame.MessageType != pending.Reply)
                throw new SidecarProtocolException("Host reply kind does not match its request.");
            var reply = SidecarDataMessages.DecodeBinaryResultDetails(frame.Payload.Span);
            if (!reply.IsBinary || reply.Success && reply.Value.CodecId != pending.Codec)
                throw new SidecarProtocolException("Host reply codec does not match its request.");
            if (reply.Value.Length > SidecarWireCodecs.MaximumValueLength && !_features.HasFlag(SidecarFeatures.LargeGeneratedDtos))
                throw new SidecarProtocolException("Large Host replies were not negotiated.");
            _pending.Remove(frame.CorrelationId.Value);
            pending.Completion.TrySetResult(new(reply.Success, reply.Value, reply.ErrorCode));
            return true;
        }
    }

    public void Dispose()
    {
        Pending[] pending;
        lock (_gate)
        {
            if (_closed) return;
            _closed = true; _active = false;
            pending = [.. _pending.Values]; _pending.Clear();
        }
        _lifetime.Cancel();
        foreach (var request in pending) request.Completion.TrySetCanceled();
        _lifetime.Dispose();
    }
}

public sealed record SidecarHostReply(bool Success, SidecarBinaryValue Value, string ErrorCode);
