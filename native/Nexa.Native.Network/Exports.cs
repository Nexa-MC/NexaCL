using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Nexa.Native.Network;

internal static class Exports
{
    private static readonly ConcurrentDictionary<nint, Transport> Transports = new();
    private static readonly ConcurrentDictionary<nint, Operation> Operations = new();
    private static long _next;
    private static nint NextId() => checked((nint)Interlocked.Increment(ref _next));

    [UnmanagedCallersOnly(EntryPoint = "nexa_network_create_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Create(int mainland, int proxy, int doh, int preference, nint* result)
    {
        if (result == null) return -1;
        *result = 0;
        try
        {
            if (mainland is < 0 or > 1 || doh is < 0 or > 1 || preference is < 0 or > 2) return -1;
            var transport = new Transport(new NetworkConnector(mainland != 0, proxy, doh != 0, preference switch { 1 => "ipv4", 2 => "ipv6", _ => "auto" }));
            nint id = NextId();
            if (id <= 0 || !Transports.TryAdd(id, transport)) { transport.Retire(); return -1; }
            *result = id;
            return 0;
        }
        catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_network_connect_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Connect(nint transportId, byte* host, int length, int port, int destination,
        nint callback, nint context, nint* result)
    {
        if (result == null) return -1;
        *result = 0;
        Transport? lease = null;
        Operation? operation = null;
        try
        {
            if (host == null || length is < 1 or > 4096 || port is < 1 or > 65535
                || destination is < 0 or > 1 || callback == 0) return -1;
            string name = new UTF8Encoding(false, true).GetString(new ReadOnlySpan<byte>(host, length));
            if (!Transports.TryGetValue(transportId, out var transport) || !transport.Acquire()) return -1;
            lease = transport;
            operation = new(transport, name, port, destination != 0, callback, context);
            nint id = NextId();
            if (id <= 0 || !Operations.TryAdd(id, operation)) throw new InvalidOperationException("Connection allocation failed.");
            *result = id;
            return 0;
        }
        catch { operation?.Dispose(); lease?.Release(); return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_network_start_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Start(nint id)
    {
        Operation? operation = null;
        bool claimed = false;
        try
        {
            if (!Operations.TryGetValue(id, out operation) || Interlocked.CompareExchange(ref operation.Started, 1, 0) != 0) return -1;
            claimed = true;
            _ = Task.Run(() => RunAsync(id, operation));
            return 0;
        }
        catch { if (claimed) Finish(id, operation!); return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_network_cancel_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static void Cancel(nint id)
    {
        try
        {
            if (Operations.TryGetValue(id, out var operation))
                lock (operation.Gate) { if (!operation.Completed) operation.Stop.Cancel(); }
        }
        catch { /* Cancellation must never unwind across the ABI. */ }
    }

    // Abandon is only valid before a successful start, for adapter setup failures.
    [UnmanagedCallersOnly(EntryPoint = "nexa_network_abandon_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static void Abandon(nint id)
    {
        try
        {
            if (!Operations.TryGetValue(id, out var operation) || Interlocked.CompareExchange(ref operation.Started, 1, 0) != 0) return;
            Finish(id, operation);
        }
        catch { }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_network_release_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(nint id)
    {
        try { if (Transports.TryRemove(id, out var transport)) transport.Retire(); }
        catch { }
    }

    private static async Task RunAsync(nint id, Operation operation)
    {
        Socket? socket = null;
        try
        {
            socket = await operation.Transport.Connector.ConnectTransferableAsync(operation.Host, operation.Port, operation.Destination, operation.Stop.Token).ConfigureAwait(false);
            operation.Stop.Token.ThrowIfCancellationRequested();
            Transfer(operation, socket);
        }
        catch (Exception error)
        {
            int status = error switch { OperationCanceledException => 1, SocketException => 2, HttpRequestException => 3, IOException => 4, _ => 5 };
            int detail = error switch { SocketException failure => (int)failure.SocketErrorCode, HttpRequestException failure => (int)failure.HttpRequestError, _ => 0 };
            Notify(operation, status, detail, 0, error.GetType().Name + ": " + error.Message);
        }
        finally { socket?.Dispose(); Finish(id, operation); }
    }

    private static void Transfer(Operation operation, Socket socket)
    {
        using var transferred = SocketTransfer.Duplicate(socket);
        // Dispose normally: SetHandleAsInvalid on an async Socket would bypass epoll/IOCP cleanup.
        socket.Dispose();
        nint handle = transferred.DangerousGetHandle();
        transferred.SetHandleAsInvalid();
        // If the Host no longer has a receiver, ownership remains here for disposal.
        if (Notify(operation, 0, 0, handle, "") == 0) new SafeSocketHandle(handle, ownsHandle: true).Dispose();
    }

    private static unsafe int Notify(Operation operation, int status, int detail, nint socket, string message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message.Length > 1024 ? message[..1024] : message);
        fixed (byte* data = bytes)
            return ((delegate* unmanaged[Cdecl]<nint, int, int, nint, byte*, int, int>)operation.Callback)
                (operation.Context, status, detail, socket, data, bytes.Length);
    }

    private static void Finish(nint id, Operation operation)
    {
        Operations.TryRemove(id, out _);
        lock (operation.Gate) { operation.Completed = true; operation.Dispose(); }
        operation.Transport.Release();
    }

    private sealed class Operation(Transport transport, string host, int port, bool destination, nint callback, nint context) : IDisposable
    {
        public void Dispose() => Stop.Dispose();
        internal readonly Transport Transport = transport;
        internal readonly string Host = host;
        internal readonly int Port = port;
        internal readonly bool Destination = destination;
        internal readonly nint Callback = callback, Context = context;
        internal readonly CancellationTokenSource Stop = new();
        internal readonly object Gate = new();
        internal bool Completed;
        internal int Started;
    }

    private sealed class Transport(NetworkConnector connector)
    {
        internal readonly NetworkConnector Connector = connector;
        private readonly object _gate = new();
        private int _leases;
        private bool _retired;
        internal bool Acquire() { lock (_gate) { if (_retired) return false; _leases++; return true; } }
        internal void Release() { lock (_gate) { if (--_leases == 0 && _retired) Connector.Dispose(); } }
        internal void Retire() { lock (_gate) { if (_retired) return; _retired = true; if (_leases == 0) Connector.Dispose(); } }
    }
}
