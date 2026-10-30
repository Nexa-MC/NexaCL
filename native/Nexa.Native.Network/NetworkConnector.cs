using System.Net;
using System.Net.Sockets;
using Nexa.Services.Network;

namespace Nexa.Native.Network;

/// <summary>Owns resolver state and creates connected sockets; the caller owns successful sockets.</summary>
public sealed class NetworkConnector : IDisposable
{
    private readonly DnsOverHttpsResolver? _resolver;
    private readonly string _preference;

    public NetworkConnector(bool mainlandChina, int proxyMode, bool dnsOverHttps, string preference)
    {
        if (proxyMode is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(proxyMode));
        if (preference is not ("auto" or "ipv4" or "ipv6")) throw new ArgumentException("Invalid IP preference.", nameof(preference));
        _preference = preference;
        _resolver = dnsOverHttps && proxyMode != 2 ? new(mainlandChina, proxyMode) : null;
    }

    public Task<Socket> ConnectAsync(string host, int port, bool destination, CancellationToken token) =>
        ConnectCoreAsync(host, port, destination, transferable: false, token);

    internal Task<Socket> ConnectTransferableAsync(string host, int port, bool destination, CancellationToken token) =>
        ConnectCoreAsync(host, port, destination, transferable: true, token);

    private async Task<Socket> ConnectCoreAsync(string host, int port, bool destination, bool transferable, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IPAddress[] addresses = destination && _resolver is not null
            ? await _resolver.ResolveAsync(host, token).ConfigureAwait(false)
            : await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
        foreach (IPAddress address in OrderAddresses(addresses, _preference))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                attempt.CancelAfter(TimeSpan.FromSeconds(4));
                if (transferable && OperatingSystem.IsWindows())
                    await ConnectWithoutCompletionPortAsync(socket, new IPEndPoint(address, port), attempt.Token).ConfigureAwait(false);
                else
                    await socket.ConnectAsync(address, port, attempt.Token).ConfigureAwait(false);
                return socket;
            }
            catch (SocketException) { socket.Dispose(); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        token.ThrowIfCancellationRequested();
        throw new HttpRequestException("The destination could not be connected.");
    }

    private static async Task ConnectWithoutCompletionPortAsync(Socket socket, IPEndPoint endpoint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        socket.Blocking = false;
        try { socket.Connect(endpoint); }
        catch (SocketException error) when (error.SocketErrorCode is SocketError.WouldBlock or SocketError.InProgress or SocketError.AlreadyInProgress)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool writable = socket.Poll(0, SelectMode.SelectWrite);
                if (!writable && !socket.Poll(0, SelectMode.SelectError))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25), token).ConfigureAwait(false);
                    continue;
                }
                token.ThrowIfCancellationRequested();
                var failure = (SocketError)(int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)!;
                if (failure != SocketError.Success) throw new SocketException((int)failure);
                break;
            }
        }
        token.ThrowIfCancellationRequested();
        // Reading Connected completes Socket's managed nonblocking-connect bookkeeping.
        if (!socket.Connected) throw new SocketException((int)SocketError.NotConnected);
        socket.Blocking = true;
    }

    public static IEnumerable<IPAddress> OrderAddresses(IPAddress[] addresses, string preference) => preference == "auto" ? addresses
        : addresses.OrderBy(address => address.AddressFamily == (preference == "ipv4" ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6) ? 0 : 1);

    public void Dispose() => _resolver?.Dispose();
}
