using System.Net;
using System.Net.Sockets;
using System.Text;
using Nexa.Native.Minecraft.Client;
using Nexa.Native.Network.Client;
using Nexa.Services.Scheduling;
using Nexa.Services.Tasks;
using Nexa.Xsr.State;

internal static class JavaInstallCases
{
    internal static async Task RunAsync(NativeMinecraftClient minecraft, string directory, CancellationToken token)
    {
        XsrStateStoreBuilder builder = new(); TaskCenterStateContract.DeclareState(builder); var state = builder.Build();
        await using var tasks = new TaskCenterService(state); tasks.AttachExternalSource(minecraft);
        minecraft.AttachTasks((revision, entries) => tasks.PublishExternalSnapshot(minecraft, revision, entries));
        using var scheduler = new WorkScheduler(new() { HttpConcurrency = 2, DiskConcurrency = 2, CpuConcurrency = 2 });
        await using var network = new NativeNetworkClient(); network.AttachDownloads(scheduler);
        using var transport = network.CreateClient(() => new() { ProxyMode = 0, DnsOverHttps = false, IpStack = "ipv4" });
        using var server = new TcpListener(IPAddress.Loopback, 0); server.Start();
        string address = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/java";
        using var described = NativeHttpClients.CreateDescribed(new Metadata("https://piston-data.mojang.com/native-test/java"),
            (_, cancellation) => NativeHttpClients.DescribeAsync(transport, address, cancellation));
        minecraft.AttachDownloads(network, described);
        Task serving = ServeAsync(server, token);
        minecraft.AttachHttp(described);
        string runtime = Path.Combine(directory, "installed-runtime");
        string executable = await minecraft.InstallAsync("java-runtime-native-test", runtime, cancellationToken: token);
        await serving;
        if (await File.ReadAllTextAsync(executable, token) != "hello") throw new InvalidDataException("Native Java installation changed verified bytes.");
        var entries = tasks.ReadEntries();
        if (entries.Count != 1 || entries[0].State != TaskCenterEntryState.Finished || !entries[0].TaskId.StartsWith(minecraft.Prefix, StringComparison.Ordinal))
            throw new InvalidDataException("Native Java task did not reach the canonical host task collection.");
        if (!await tasks.DismissFinishedAsync(entries, token) || tasks.ReadEntries().Count != 0)
            throw new InvalidDataException("Transported Java task history could not be dismissed by its owner.");
        if (!(await minecraft.RecoverJavaAsync(runtime, token)).IsSuccess) throw new InvalidDataException("Native Java recovery rejected a completed installation.");
        await minecraft.DisposeAsync();
        Console.WriteLine("Native Java installation used host HTTP, canonical native downloads/work and imported task controls.");
    }
    private static async Task ServeAsync(TcpListener server, CancellationToken token)
    {
        using var peer = await server.AcceptTcpClientAsync(token); await using var stream = peer.GetStream();
        byte[] bytes = new byte[16384]; int received = 0;
        while (received < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(received), token); if (read == 0) throw new IOException("Missing Java request.");
            received += read; if (bytes.AsSpan(0, received).IndexOf("\r\n\r\n"u8) >= 0) break;
        }
        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello"u8.ToArray(), token);
    }
    private sealed class Metadata(string address) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac-os" : "linux";
            string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            string platform = os + "-" + (arch == "arm64" ? "arm64" : "x64");
            if (OperatingSystem.IsLinux()) platform = "linux";
            if (OperatingSystem.IsMacOS() && arch == "x64") platform = "mac-os";
            string relative = OperatingSystem.IsWindows() ? "bin/java.exe" : "bin/java";
            string json = request.RequestUri!.AbsolutePath.EndsWith("all.json", StringComparison.Ordinal)
                ? """{"PLATFORM":{"java-runtime-native-test":[{"version":{"name":"21.0.2"},"manifest":{"url":"https://example.invalid/native-java.json"}}]}}""".Replace("PLATFORM", platform, StringComparison.Ordinal)
                : """{"files":{"EXECUTABLE":{"executable":true,"downloads":{"raw":{"url":"ADDRESS","sha1":"aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d","size":5}}}}}""".Replace("EXECUTABLE", relative, StringComparison.Ordinal).Replace("ADDRESS", address, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
