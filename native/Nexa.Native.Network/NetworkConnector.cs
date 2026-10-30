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

    public async Task<Socket> ConnectAsync(string host, int port, bool destination, CancellationToken token)
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

    public static IEnumerable<IPAddress> OrderAddresses(IPAddress[] addresses, string preference) => preference == "auto" ? addresses
        : addresses.OrderBy(address => address.AddressFamily == (preference == "ipv4" ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6) ? 0 : 1);

    public void Dispose() => _resolver?.Dispose();
}
