using System.Buffers.Binary;

namespace Nexa.Native.Ui;

internal static class NativeUiAsyncProtocol
{
    internal const byte Start = 0, Cancel = 1, Success = 2, Canceled = 3, Failed = 4;
    internal const int Header = 13;
    internal static byte[] Message(byte kind, long id, ReadOnlyMemory<byte> body = default)
    {
        if (id <= 0 || body.Length > NativeUiCallRegistration.MaximumBytes - Header)
            throw new InvalidDataException("Invalid native UI async envelope.");
        byte[] bytes = new byte[Header + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 0x3141554e); bytes[4] = kind;
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(5), id); body.CopyTo(bytes.AsMemory(Header));
        return bytes;
    }
    internal static (byte Kind, long Id, ReadOnlyMemory<byte> Body) Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < Header or > NativeUiCallRegistration.MaximumBytes
            || BinaryPrimitives.ReadInt32LittleEndian(bytes.Span) != 0x3141554e)
            throw new InvalidDataException("Invalid native UI async envelope.");
        long id = BinaryPrimitives.ReadInt64LittleEndian(bytes.Span[5..]);
        if (id <= 0) throw new InvalidDataException("Invalid native UI async operation ID.");
        return (bytes.Span[4], id, bytes[Header..]);
    }
}

internal sealed class NativeUiAsyncClient(Func<ReadOnlyMemory<byte>, byte[]> send) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Pending> _pending = [];
    private long _next;
    private bool _closed;

    internal Task<byte[]> InvokeAsync(ReadOnlyMemory<byte> body, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled<byte[]>(token);
        long id; Pending pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_pending.Count >= 128) throw new InvalidOperationException("Native UI async operation budget exceeded.");
            id = checked(++_next); pending = new(); _pending.Add(id, pending);
        }
        _ = StartAsync(id, pending, body, token);
        return pending.Source.Task;
    }

    private async Task StartAsync(long id, Pending pending, ReadOnlyMemory<byte> body, CancellationToken token)
    {
        try
        {
            byte[] message = NativeUiAsyncProtocol.Message(NativeUiAsyncProtocol.Start, id, body);
            int delay = 1;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                lock (_gate) { if (_closed || !_pending.ContainsKey(id)) return; }
                try { _ = send(message); break; }
                catch (NativeUiCallBusyException)
                {
                    await Task.Delay(delay, token).ConfigureAwait(false); delay = Math.Min(delay * 2, 32);
                }
            }
            bool admitted;
            lock (_gate) admitted = !_closed && _pending.ContainsKey(id);
            if (!admitted) CancelRemote(id);
            else pending.AttachCancellation(() =>
            {
                pending.Source.TrySetCanceled(token); CancelRemote(id);
            }, token);
        }
        catch (OperationCanceledException)
        {
            Remove(id)?.Dispose(); pending.Source.TrySetCanceled(token); CancelRemote(id);
        }
        catch (Exception error)
        {
            Remove(id)?.Dispose(); pending.Source.TrySetException(error); CancelRemote(id);
        }
    }

    internal byte[] Complete(ReadOnlyMemory<byte> message)
    {
        var (kind, id, body) = NativeUiAsyncProtocol.Read(message);
        if (kind is not (NativeUiAsyncProtocol.Success or NativeUiAsyncProtocol.Canceled or NativeUiAsyncProtocol.Failed)
            || kind != NativeUiAsyncProtocol.Success && !body.IsEmpty)
            throw new InvalidDataException("Invalid native UI completion.");
        var pending = Remove(id);
        if (pending is null) return []; // Late completion after caller retirement.
        try
        {
            if (kind == NativeUiAsyncProtocol.Success) pending.Source.TrySetResult(body.ToArray());
            else if (kind == NativeUiAsyncProtocol.Canceled) pending.Source.TrySetCanceled();
            else pending.Source.TrySetException(new InvalidOperationException("The native UI async operation failed."));
        }
        finally { pending.Dispose(); }
        return [];
    }

    private Pending? Remove(long id)
    {
        lock (_gate) return _pending.Remove(id, out var pending) ? pending : null;
    }
    private void CancelRemote(long id) => _ = SendCancellationAsync(id);
    private async Task SendCancellationAsync(long id)
    {
        int delay = 1;
        while (true)
        {
            try { _ = send(NativeUiAsyncProtocol.Message(NativeUiAsyncProtocol.Cancel, id)); return; }
            catch (NativeUiCallBusyException)
            { await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32); }
            catch { return; } // Session shutdown separately drains or reports a failed peer.
        }
    }
    public void Dispose()
    {
        KeyValuePair<long, Pending>[] pending;
        lock (_gate)
        {
            if (_closed) return;
            _closed = true; pending = _pending.ToArray(); _pending.Clear();
        }
        foreach (var (id, operation) in pending)
        {
            operation.Source.TrySetCanceled(); operation.Dispose(); CancelRemote(id);
        }
    }

    private sealed class Pending : IDisposable
    {
        private readonly object _gate = new();
        private CancellationTokenRegistration _registration;
        private bool _finished;
        internal TaskCompletionSource<byte[]> Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void AttachCancellation(Action cancel, CancellationToken token)
        {
            if (!token.CanBeCanceled) return;
            var registration = token.Register(cancel);
            lock (_gate)
            {
                if (_finished) registration.Unregister();
                else _registration = registration;
            }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_finished) return;
                _finished = true; _registration.Unregister();
            }
        }
    }
}

internal sealed class NativeUiAsyncServer(
    Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]>> execute,
    Func<ReadOnlyMemory<byte>, byte[]> complete,
    Action<Exception>? failure = null) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, (CancellationTokenSource Source, int Bytes)> _pending = [];
    private long _requestBytes;
    private readonly TaskCompletionSource _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;

    internal byte[] Exchange(ReadOnlyMemory<byte> message)
    {
        var (kind, id, body) = NativeUiAsyncProtocol.Read(message);
        if (kind == NativeUiAsyncProtocol.Cancel)
        {
            if (!body.IsEmpty) throw new InvalidDataException("Unexpected UI cancellation payload.");
            CancellationTokenSource? cancellation;
            lock (_gate) cancellation = _pending.TryGetValue(id, out var entry) ? entry.Source : null;
            Cancel(cancellation); return [];
        }
        if (kind != NativeUiAsyncProtocol.Start) throw new InvalidDataException("Unknown UI async operation.");
        CancellationTokenSource source;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_pending.Count >= 128 || _pending.ContainsKey(id) || body.Length > 128L * 1024 * 1024 - _requestBytes)
                throw new InvalidOperationException("Native UI async admission failed.");
            source = new(); _pending.Add(id, (source, body.Length)); _requestBytes += body.Length;
        }
        _ = RunAsync(id, body, source);
        return [];
    }

    private async Task RunAsync(long id, ReadOnlyMemory<byte> body, CancellationTokenSource source)
    {
        try
        {
            byte[] message;
            try
            {
                byte[] result = await execute(body, source.Token).ConfigureAwait(false);
                if (result is null) throw new InvalidDataException("Missing native UI async result.");
                message = NativeUiAsyncProtocol.Message(NativeUiAsyncProtocol.Success, id, result);
            }
            catch (OperationCanceledException) { message = NativeUiAsyncProtocol.Message(NativeUiAsyncProtocol.Canceled, id); }
            catch (Exception error)
            {
                Report(error); message = NativeUiAsyncProtocol.Message(NativeUiAsyncProtocol.Failed, id);
            }
            int delay = 1;
            while (true)
            {
                try { _ = complete(message); break; }
                catch (NativeUiCallBusyException)
                { await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32); }
                catch (Exception error) { Report(error); break; }
            }
        }
        finally
        {
            source.Dispose();
            lock (_gate)
            {
                if (_pending.Remove(id, out var entry)) _requestBytes -= entry.Bytes;
                if (_closed && _pending.Count == 0) _retired.TrySetResult();
            }
        }
    }
    private void Report(Exception error) { try { failure?.Invoke(error); } catch { } }
    private void Cancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (Exception error) { Report(error); }
    }
    public ValueTask DisposeAsync()
    {
        CancellationTokenSource[] pending;
        lock (_gate)
        {
            _closed = true; pending = _pending.Values.Select(entry => entry.Source).ToArray();
            if (_pending.Count == 0) _retired.TrySetResult();
        }
        foreach (var source in pending) Cancel(source);
        return new(_retired.Task);
    }
}
