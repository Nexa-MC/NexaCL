using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Nexa.Services.Network;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask NetworkConnectionTransfersStreamsAndSurvivesRetirement()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using Socket accepted = await listener.AcceptSocketAsync(budget.Token);
            using var stream = new NetworkStream(accepted, ownsSocket: false);
            byte[] bytes = new byte[4];
            await stream.ReadExactlyAsync(bytes, budget.Token);
            AssertEqual("ping", Encoding.ASCII.GetString(bytes));
            await stream.WriteAsync("pong"u8.ToArray(), budget.Token);
        });
        using var connection = new NetworkConnection(new() { ProxyMode = 0, DnsOverHttps = false, IpStack = "ipv4" }, false);
        var pending = connection.ConnectAsync("127.0.0.1", port, true, budget.Token);
        connection.Dispose(); // Native resolver/transport disposal must wait for the in-flight lease.
        using Stream client = await pending;
        client.WriteTimeout = client.ReadTimeout = 5000;
        client.WriteByte((byte)'p'); // Imported socket handles must support synchronous and asynchronous I/O.
        await client.WriteAsync("ing"u8.ToArray(), budget.Token);
        AssertEqual((int)'p', client.ReadByte());
        byte[] reply = new byte[3];
        await client.ReadExactlyAsync(reply, budget.Token);
        AssertEqual("ong", Encoding.ASCII.GetString(reply));
        await server;
    }

    private static async ValueTask NetworkConnectionCancellationAndRefusal()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var connection = new NetworkConnection(new() { ProxyMode = 0, DnsOverHttps = false, IpStack = "ipv4" }, false);
        using var stopAccept = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
        var server = Task.Run(async () =>
        {
            try { while (true) { using Socket accepted = await listener.AcceptSocketAsync(stopAccept.Token); } }
            catch (OperationCanceledException) when (stopAccept.IsCancellationRequested) { }
        });
        for (int i = 0; i < 32; i++)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            var pending = connection.ConnectAsync("127.0.0.1", port, true, cancellation.Token);
            cancellation.Cancel();
            try { using Stream stream = await pending; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        // Cancellation races must not poison later connections in the same generation.
        using (Stream stream = await connection.ConnectAsync("127.0.0.1", port, true, budget.Token)) { }
        stopAccept.Cancel();
        await server;
        listener.Stop();
        try
        {
            using Stream unexpected = await connection.ConnectAsync("127.0.0.1", port, true, budget.Token);
            throw new InvalidOperationException("A refused connection was reported as successful.");
        }
        catch (HttpRequestException) { }
        using var alreadyCanceled = new CancellationTokenSource();
        alreadyCanceled.Cancel();
        try
        {
            using Stream unexpected = await connection.ConnectAsync("127.0.0.1", port, true, alreadyCanceled.Token);
            throw new InvalidOperationException("A canceled connection was started.");
        }
        catch (OperationCanceledException) { }
    }

    private static async ValueTask NetworkTlsPreservesSpecializedIdentity()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using RSA serverKey = RSA.Create(2048), clientKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var clientRequest = new CertificateRequest("CN=nexa-network-test", clientKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var serverIdentity = serverRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var clientIdentity = clientRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        // Use the production platform key-storage policy: Schannel and macOS cannot use EphemeralKeySet.
        X509KeyStorageFlags keys = OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet
            : OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        using var serverCertificate = X509CertificateLoader.LoadPkcs12(serverIdentity.Export(X509ContentType.Pkcs12), null, keys);
        using var clientCertificate = X509CertificateLoader.LoadPkcs12(clientIdentity.Export(X509ContentType.Pkcs12), null, keys);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using Socket accepted = await listener.AcceptSocketAsync(budget.Token);
            using var stream = new SslStream(new NetworkStream(accepted, ownsSocket: false), false,
                (_, certificate, _, _) => certificate?.GetCertHashString() == clientCertificate.Thumbprint);
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            { ServerCertificate = serverCertificate, ClientCertificateRequired = true }, budget.Token);
            AssertEqual(clientCertificate.Thumbprint, stream.RemoteCertificate?.GetCertHashString());
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            string? line;
            do { line = await reader.ReadLineAsync(budget.Token); } while (!string.IsNullOrEmpty(line));
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), budget.Token);
        });
        using var root = new NetworkHttpClientPool(() => new() { ProxyMode = 0, DnsOverHttps = false, IpStack = "ipv4" });
        using var specialized = root.CreateSpecialized(handler =>
        {
            handler.SslOptions.ClientCertificates = new X509CertificateCollection { clientCertificate };
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == serverCertificate.Thumbprint;
        });
        using var client = specialized.CreateClient(false);
        AssertEqual("ok", await client.GetStringAsync($"https://127.0.0.1:{port}/", budget.Token));
        await server;
    }
}
