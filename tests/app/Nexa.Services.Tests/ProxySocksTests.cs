using System.Net;
using System.Net.Sockets;
using System.Text;
using Nexa.Services.Network;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask SocksAuthenticatedSocketHandshakes()
    {
        foreach (string scheme in new[] { "socks4", "socks5" })
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string phase = "accept";
            var serving = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(stop.Token); await using var stream = socket.GetStream();
                async Task<byte[]> Read(int length) { phase = "read " + length; byte[] data = new byte[length]; await stream.ReadExactlyAsync(data, stop.Token); return data; }
                async Task<string> CString()
                {
                    var data = new List<byte>(); byte value;
                    while ((value = (await Read(1))[0]) != 0) { if (data.Count >= 1024) throw new InvalidDataException(); data.Add(value); }
                    return Encoding.UTF8.GetString(data.ToArray());
                }
                if (scheme == "socks5")
                {
                    byte[] hello = await Read(2); AssertEqual((byte)5, hello[0]);
                    byte[] methods = await Read(hello[1]); AssertTrue(methods.Contains((byte)2));
                    await stream.WriteAsync(new byte[] { 5, 2 }, stop.Token);
                    byte[] auth = await Read(2); AssertEqual((byte)1, auth[0]); AssertEqual("user", Encoding.UTF8.GetString(await Read(auth[1])));
                    int passwordLength = (await Read(1))[0]; AssertEqual("secret", Encoding.UTF8.GetString(await Read(passwordLength)));
                    await stream.WriteAsync(new byte[] { 1, 0 }, stop.Token);
                    byte[] command = await Read(4); AssertEqual((byte)1, command[1]);
                    int size = command[3] switch { 1 => 4, 4 => 16, 3 => (await Read(1))[0], _ => throw new InvalidDataException() };
                    await Read(size + 2);
                    await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 80 }, stop.Token);
                }
                else
                {
                    byte[] command = await Read(8); AssertEqual((byte)4, command[0]); AssertEqual((byte)1, command[1]);
                    AssertEqual("user", await CString());
                    await stream.WriteAsync(new byte[] { 0, 90, 0, 80, 127, 0, 0, 1 }, stop.Token);
                }
                phase = "HTTP request";
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                AssertTrue((await reader.ReadLineAsync(stop.Token))!.StartsWith("GET /value ", StringComparison.Ordinal));
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))) { }
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"u8.ToArray(), stop.Token);
            }, stop.Token);
            using var pool = new NetworkHttpClientPool(() => new() { ProxyMode = 2, ProxyAddress = $"{scheme}://127.0.0.1:{port}", ProxyUser = "user", ProxyPassword = "secret", DnsOverHttps = false });
            using var client = pool.CreateClient();
            string host = scheme == "socks4" ? "127.0.0.1" : "unresolvable.invalid";
            try { AssertEqual("OK", await client.GetStringAsync($"http://{host}/value", stop.Token)); }
            catch (Exception error)
            {
                if (serving.IsFaulted) await serving;
                throw new InvalidOperationException($"{scheme} failed during {phase}", error);
            }
            await serving;
        }
    }
}
