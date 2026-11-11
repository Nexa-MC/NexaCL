using Nexa.Sidecar.Protocol;

namespace Nexa.Xsr.Runtime;

public sealed partial class SidecarHostSession
{
    public SidecarHostRequestRouter? HostRequests { get; init; }
    private readonly Dictionary<Guid, CancellationTokenSource> _hostRequests = [];
    public int PendingHostRequestCount { get { lock (_gate) return _hostRequests.Count; } }

    private async ValueTask AcceptHostRequestAsync(SidecarFrame frame, CancellationToken sessionCancellation)
    {
        if ((NegotiatedFeatures & (SidecarFeatures.HostRequests | SidecarFeatures.BinaryPayloads)) !=
            (SidecarFeatures.HostRequests | SidecarFeatures.BinaryPayloads))
            throw new SidecarProtocolException("Host requests were not negotiated.");
        if (!frame.CorrelationId.IsAssigned) throw new SidecarProtocolException("Host request has no correlation ID.");
        var request = SidecarDataMessages.DecodeBinaryRequestDetails(frame.Payload.Span);
        if (!request.IsBinary) throw new SidecarProtocolException("Host requests require binary data.");
        var route = HostRequests?.Resolve(request.ContractId);
        string? error = null;
        CancellationTokenSource? lifetime = null;
        var caller = new SidecarHostCaller(PluginName, SessionId);
        lock (_gate)
        {
            if (_hostRequests.ContainsKey(frame.CorrelationId.Value)) throw new SidecarProtocolException("Duplicate in-flight Host request.");
            if (!Accepting) error = "xsr.unavailable";
            else if (_hostRequests.Count >= _maxPending) error = "xsr.backpressure";
            else if (route is null) error = "xsr.route_not_found";
            else if (route.IsQuery != (frame.MessageType == SidecarMessageType.QueryRequest) || route.InputCodec != request.Value.CodecId) error = "xsr.contract_mismatch";
            else
            {
                lifetime = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation, _sessionEnded.Token, _activationEnded.Token);
                lifetime.CancelAfter(route.Timeout);
                _hostRequests.Add(frame.CorrelationId.Value, lifetime);
            }
        }
        if (error is not null)
        {
            await SendHostReplyAsync(frame, null, error, sessionCancellation).ConfigureAwait(false);
            return;
        }
        // The worker yields before entering application code; there is still only one frame reader.
        _ = ObserveBackgroundCleanupAsync(ExecuteHostRequestAsync(frame, route!, caller, request.Value, lifetime!));
    }

    private async Task ExecuteHostRequestAsync(SidecarFrame frame, SidecarHostRequestRoute route,
        SidecarHostCaller caller, SidecarBinaryValue argument, CancellationTokenSource lifetime)
    {
        await Task.Yield();
        Task<XsrResult<SidecarBinaryValue>>? pending = null;
        bool entered = false;
        try
        {
            SidecarBinaryValue? value = null;
            string? error = null;
            try
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (!(entered = HostRequests!.TryEnter())) error = "xsr.backpressure";
                else if (!route.Authorize(caller)) error = "xsr.permission_denied";
                else
                {
                    pending = Task.Run(async () => await route.Invoke(caller, argument, lifetime.Token).ConfigureAwait(false), lifetime.Token);
                    var result = await pending.WaitAsync(lifetime.Token).ConfigureAwait(false);
                    if (!result.IsSuccess) error = result.Error!.Code.Value;
                    else if (result.Value.CodecId != route.OutputCodec) error = "xsr.contract_mismatch";
                    else value = result.Value;
                }
            }
            catch (OperationCanceledException)
            { error = !route.IsQuery && pending is not null ? "xsr.operation_outcome_unknown" : "xsr.cancelled"; }
            catch (Exception exception) when (exception is not OutOfMemoryException and not AccessViolationException)
            { error = "xsr.handler_faulted"; }
            using var sendDeadline = CancellationTokenSource.CreateLinkedTokenSource(_sessionEnded.Token, _activationEnded.Token);
            sendDeadline.CancelAfter(TimeSpan.FromSeconds(2));
            lock (_gate) { if (!Accepting) return; }
            await SendHostReplyAsync(frame, value, error, sendDeadline.Token).ConfigureAwait(false);
        }
        finally
        {
            // A timed-out non-cooperative handler still occupies its slot. Cancellation
            // cannot be used to accumulate unbounded background work.
            if (pending is not null) await ObserveHostHandlerAsync(pending).ConfigureAwait(false);
            if (entered) HostRequests!.Exit();
            lock (_gate) _hostRequests.Remove(frame.CorrelationId.Value);
            lifetime.Dispose();
        }
    }

    private static async Task ObserveHostHandlerAsync(Task<XsrResult<SidecarBinaryValue>> pending)
    { try { await pending.ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { } }

    private ValueTask SendHostReplyAsync(SidecarFrame request, SidecarBinaryValue? value, string? error, CancellationToken cancellation) =>
        SendTrackedAsync(new(SidecarProtocol.Version,
            request.MessageType == SidecarMessageType.QueryRequest ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
            SidecarFrameTraits.None, request.CorrelationId,
            SidecarDataMessages.EncodeBinaryResult(error is null, value ?? new(SidecarWireCodecs.Bytes, []), error)), cancellation);

    private void CancelHostRequest(SidecarFrame frame)
    {
        if (!NegotiatedFeatures.HasFlag(SidecarFeatures.HostRequests)) throw new SidecarProtocolException("Host cancellation was not negotiated.");
        var request = SidecarStateSnapshot.DecodeCancel(frame.Payload.Span);
        if (request.CorrelationId != frame.CorrelationId.Value) throw new SidecarProtocolException("Cancellation correlation mismatch.");
        CancellationTokenSource? cancellation;
        lock (_gate) _hostRequests.TryGetValue(request.CorrelationId, out cancellation);
        try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }
}
