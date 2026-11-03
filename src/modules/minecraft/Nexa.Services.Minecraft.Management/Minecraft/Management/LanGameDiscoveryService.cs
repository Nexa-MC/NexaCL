using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
namespace Nexa.Services.Minecraft.Management;

public static class LanGameDiscoveryService
{
    public static async Task<LanGameDiscoverySnapshot> DiscoverAsync(LanGameDiscoveryQuery query, CancellationToken token = default)
    {
        if (query.ListenMilliseconds is < 250 or > 5000) throw new ArgumentOutOfRangeException(nameof(query));
        using var socket = new UdpClient(AddressFamily.InterNetwork);
        socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Client.Bind(new IPEndPoint(IPAddress.Any, 4445));
        int joined = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast))
            foreach (var address in adapter.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                try { socket.JoinMulticastGroup(IPAddress.Parse("224.0.2.60"), address.Address); joined++; } catch (SocketException) { }
        if (joined == 0) socket.JoinMulticastGroup(IPAddress.Parse("224.0.2.60"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(query.ListenMilliseconds);
        Dictionary<string, LanGame> games = new(StringComparer.Ordinal); int packets = 0;
        try
        {
            while (packets++ < 2048 && games.Count < 128)
            {
                var packet = await socket.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                if (ParseAdvertisement(packet.Buffer, packet.RemoteEndPoint.Address) is { } game) games[game.Address] = game;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        token.ThrowIfCancellationRequested();
        return new(games.Values.OrderBy(g => g.Address, StringComparer.Ordinal).ToArray());
    }

    public static LanGame? ParseAdvertisement(byte[] data, IPAddress source)
    {
        if (data.Length is 0 or > 2048 || source.AddressFamily != AddressFamily.InterNetwork) return null;
        byte[] ip = source.GetAddressBytes();
        if (!(IPAddress.IsLoopback(source) || ip[0] == 10 || ip[0] == 172 && ip[1] is >= 16 and <= 31 || ip[0] == 192 && ip[1] == 168 || ip[0] == 169 && ip[1] == 254)) return null;
        string text;
        try { text = new UTF8Encoding(false, true).GetString(data); } catch (DecoderFallbackException) { return null; }
        const string separator = "[/MOTD][AD]";
        int split = text.IndexOf(separator, StringComparison.Ordinal);
        if (!text.StartsWith("[MOTD]", StringComparison.Ordinal) || !text.EndsWith("[/AD]", StringComparison.Ordinal) || split < 6) return null;
        string portText = text[(split + separator.Length)..^5];
        if (!int.TryParse(portText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535) return null;
        string name = text[6..split];
        if (name.Length > 512 || name.Any(c => char.IsControl(c))) return null;
        return new(name, source + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
