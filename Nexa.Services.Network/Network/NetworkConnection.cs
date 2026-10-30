using System.Net;
using System.Net.Sockets;
#if NEXA_EXTERNAL_MODULES
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Nexa.Native.Interop;
#endif

namespace Nexa.Services.Network;

/// <summary>One connection-engine lifetime for a committed HTTP transport generation.</summary>
internal sealed class NetworkConnection : IDisposable
{
#if !NEXA_EXTERNAL_MODULES
    private readonly Nexa.Native.Network.NetworkConnector _connector;
    internal NetworkConnection(NetworkPreferences policy, bool mainlandChina) =>
        _connector = new(mainlandChina, policy.ProxyMode, policy.DnsOverHttps, policy.IpStack);
    internal ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token) =>
        ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port,
            string.Equals(context.DnsEndPoint.Host, context.InitialRequestMessage.RequestUri?.IdnHost, StringComparison.OrdinalIgnoreCase), token);
    internal async ValueTask<Stream> ConnectAsync(string host, int port, bool destination, CancellationToken token)
    {
        var socket = await _connector.ConnectAsync(host, port, destination, token).ConfigureAwait(false);
        try { return new NetworkStream(socket, ownsSocket: true); }
        catch { socket.Dispose(); throw; }
    }
    public void Dispose() => _connector.Dispose();
#else
    private static readonly ConcurrentDictionary<nint, Pending> PendingConnections = new();
    private static long _next;
    private nint _transport;
    private readonly object _gate = new();

    internal unsafe NetworkConnection(NetworkPreferences policy, bool mainlandChina)
    {
        nint transport;
        int result = Api.Create(mainlandChina ? 1 : 0, policy.ProxyMode, policy.DnsOverHttps ? 1 : 0,
            policy.IpStack switch { "ipv4" => 1, "ipv6" => 2, _ => 0 }, &transport);
        if (result != 0 || transport == 0) throw new HttpRequestException("Native network transport could not be initialized.");
        _transport = transport;
    }

    internal ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token) =>
        ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port,
            string.Equals(context.DnsEndPoint.Host, context.InitialRequestMessage.RequestUri?.IdnHost, StringComparison.OrdinalIgnoreCase), token);

    // Also used by real native loopback/ownership tests, without relying on SocketsHttpHandler internals.
    internal async ValueTask<Stream> ConnectAsync(string host, int port, bool destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var pending = new Pending(token);
        nint context = checked((nint)Interlocked.Increment(ref _next));
        if (context <= 0 || !PendingConnections.TryAdd(context, pending)) throw new InvalidOperationException("Connection callback allocation failed.");
        nint request = 0;
        bool started = false;
        try
        {
            request = CreateRequest(host, port, destination, context);
            using var cancellation = token.UnsafeRegister(static state => Cancel((nint)state!), request);
            Start(request);
            started = true;
            return await pending.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            PendingConnections.TryRemove(context, out _);
            if (!started && request != 0) Abandon(request);
        }
    }

    private unsafe nint CreateRequest(string host, int port, bool destination, nint context)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(host);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_transport == 0, this);
            nint request;
            fixed (byte* name = bytes)
                if (Api.Connect(_transport, name, bytes.Length, port, destination ? 1 : 0,
                    (nint)(delegate* unmanaged[Cdecl]<nint, int, int, nint, byte*, int, int>)&Complete, context, &request) != 0)
                    throw new HttpRequestException("Native network connection could not be created.");
            return request;
        }
    }

    private static unsafe void Start(nint request)
    {
        if (Api.Start(request) != 0) throw new HttpRequestException("Native network connection could not be started.");
    }
    private static unsafe void Cancel(nint request) => Api.Cancel(request);
    private static unsafe void Abandon(nint request) => Api.Abandon(request);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Complete(nint context, int status, int detail, nint socket, byte* message, int length)
    {
        // Return 1 only when this runtime has taken responsibility for closing the OS handle.
        if (!PendingConnections.TryGetValue(context, out var pending)) return 0;
        SafeSocketHandle? handle = null;
        Socket? ownedSocket = null;
        Stream? stream = null;
        bool accepted = false;
        try
        {
            if (status == 0)
            {
                handle = new SafeSocketHandle(socket, ownsHandle: true);
                accepted = true;
                ownedSocket = new Socket(handle);
                handle = null;
                // A duplicated descriptor carries the originating runtime's nonblocking flag.
                // Re-establish this runtime's logical blocking state before exposing NetworkStream.
                ownedSocket.Blocking = true;
                stream = new NetworkStream(ownedSocket, ownsSocket: true);
                ownedSocket = null;
                if (pending.Token.IsCancellationRequested)
                    pending.Completion.TrySetCanceled(pending.Token);
                else if (pending.Completion.TrySetResult(stream)) stream = null;
            }
            else if (status == 1) pending.Completion.TrySetCanceled(pending.Token.IsCancellationRequested ? pending.Token : new CancellationToken(true));
            else
            {
                string text = length is >= 0 and <= 4096 && (length == 0 || message != null)
                    ? Encoding.UTF8.GetString(new ReadOnlySpan<byte>(message, length)) : "Invalid native network error.";
                Exception error = status switch
                {
                    2 => new SocketException(detail),
                    3 => new HttpRequestException((HttpRequestError)detail, text),
                    4 => new IOException(text),
                    _ => new HttpRequestException(text),
                };
                pending.Completion.TrySetException(error);
            }
        }
        catch (Exception error) { pending.Completion.TrySetException(error); }
        finally
        {
            try { stream?.Dispose(); ownedSocket?.Dispose(); handle?.Dispose(); }
            catch (Exception error) { pending.Completion.TrySetException(error); }
        }
        return accepted ? 1 : 0;
    }

    public unsafe void Dispose()
    {
        lock (_gate)
        {
            if (_transport == 0) return;
            Api.Release(_transport);
            _transport = 0;
        }
    }

    private sealed record Pending(CancellationToken Token)
    {
        internal TaskCompletionSource<Stream> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static unsafe class Api
    {
        private static readonly nint Library = NativeEngineClient.Load("nexa-network");
        internal static readonly delegate* unmanaged[Cdecl]<int, int, int, int, nint*, int> Create =
            (delegate* unmanaged[Cdecl]<int, int, int, int, nint*, int>)NativeLibrary.GetExport(Library, "nexa_network_create_v1");
        internal static readonly delegate* unmanaged[Cdecl]<nint, byte*, int, int, int, nint, nint, nint*, int> Connect =
            (delegate* unmanaged[Cdecl]<nint, byte*, int, int, int, nint, nint, nint*, int>)NativeLibrary.GetExport(Library, "nexa_network_connect_v1");
        internal static readonly delegate* unmanaged[Cdecl]<nint, int> Start =
            (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(Library, "nexa_network_start_v1");
        internal static readonly delegate* unmanaged[Cdecl]<nint, void> Cancel =
            (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(Library, "nexa_network_cancel_v1");
        internal static readonly delegate* unmanaged[Cdecl]<nint, void> Abandon =
            (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(Library, "nexa_network_abandon_v1");
        internal static readonly delegate* unmanaged[Cdecl]<nint, void> Release =
            (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(Library, "nexa_network_release_v1");
    }
#endif
}
