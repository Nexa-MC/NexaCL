using System.Net;
using System.Net.Sockets;
using System.Text;
using Nexa.Native.Minecraft.Client;
using Nexa.Native.Network.Client;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Scheduling;
using Nexa.Services.Settings;
using Nexa.Services.Tasks;
using Nexa.Xsr;
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
        using var serverStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task serving = ServeAsync(server, serverStop.Token);
        minecraft.AttachHttp(described);
        var registry = new Registry();
        minecraft.AttachJavaRegistry(registry, [Path.Combine(directory, "runtime")]);
        var registeredInventory = await minecraft.ReadJavaInventoryAsync(new(true), token);
        if (!registeredInventory.IsSuccess || registeredInventory.Value!.RegistryRevision != 0)
            throw new InvalidDataException("Native Java inventory lost its canonical registry revision.");
        var diagnostics = await minecraft.ReadJavaPropertiesAsync(new("relative-java", 0), token);
        if (!diagnostics.IsSuccess || diagnostics.Value!.Status != JavaRuntimeDiagnosticStatus.Rejected)
            throw new InvalidDataException("Native Java diagnostics bypassed executable admission.");
        string runtime = Path.Combine(directory, "installed-runtime");
        string executable = await minecraft.InstallAsync("java-runtime-native-test", runtime, cancellationToken: token);
        if (await File.ReadAllTextAsync(executable, token) != "hello") throw new InvalidDataException("Native Java installation changed verified bytes.");
        var entries = tasks.ReadEntries();
        if (entries.Count != 1 || entries[0].State != TaskCenterEntryState.Finished || !entries[0].TaskId.StartsWith(minecraft.Prefix, StringComparison.Ordinal))
            throw new InvalidDataException("Native Java task did not reach the canonical host task collection.");
        if (!await tasks.DismissFinishedAsync(entries, token) || tasks.ReadEntries().Count != 0)
            throw new InvalidDataException("Transported Java task history could not be dismissed by its owner.");
        if (!(await minecraft.RecoverJavaAsync(runtime, token)).IsSuccess) throw new InvalidDataException("Native Java recovery rejected a completed installation.");
        if ((await minecraft.PreviewJavaAsync(new(21, 99), token)).IsSuccess) throw new InvalidDataException("Native Java preview accepted a stale registry.");
        var preview = await minecraft.PreviewJavaAsync(new(21, 0), token);
        if (!preview.IsSuccess || preview.Value!.LicenseUrls.Count != 1) throw new InvalidDataException("Native manual Java preview lost license admission.");
        if ((await minecraft.InstallManualJavaAsync(new(preview.Value.Id, false), token)).IsSuccess) throw new InvalidDataException("Native Java install bypassed license confirmation.");
        if ((await minecraft.InstallManualJavaAsync(new(preview.Value.Id, true), token)).IsSuccess) throw new InvalidDataException("Invalid fixture executable was registered as usable Java.");
        var receipt = await minecraft.JavaManualStatusAsync(new(preview.Value.Id), token);
        if (!receipt.IsSuccess || receipt.Value!.Status != JavaManualInstallStatus.InstalledUnverified || registry.Writes != 0)
            throw new InvalidDataException("Native manual Java lost its actual executable verification.");
        entries = tasks.ReadEntries();
        if (entries.Count != 1 || entries[0].State != TaskCenterEntryState.Failed) throw new InvalidDataException("Native manual Java created duplicate task registrations.");
        if ((await minecraft.ManageJavaAsync(new(executable, JavaRuntimeManagementAction.Disable, 99), token)).IsSuccess)
            throw new InvalidDataException("Native Java management accepted a stale revision.");
        var settings = new InstallSettings(); minecraft.AttachSettings(settings, settings);
        await minecraft.ConfigureInstallationAsync("US", token: token);
        string gameRoot = Path.Combine(directory, "native-installed-game");
        var game = await minecraft.InstallMinecraftAsync(new(gameRoot, "1.20.1"), token);
        if (!game.IsSuccess) throw new InvalidDataException("Native game installation failed: " + game.Error!.Message);
        if (await File.ReadAllTextAsync(Path.Combine(game.Value!.InstanceDirectory, "1.20.1.jar"), token) != "hello"
            || (await minecraft.InstancesAsync(gameRoot, token: token)).Count != 1 || settings.Reads < 2)
            throw new InvalidDataException("Native game installation lost committed bytes, discovery or canonical policy reads.");
        settings.Hold = true;
        var waitingInstall = minecraft.InstallMinecraftAsync(new(Path.Combine(directory, "waiting-install"), "1.20.1"), token);
        await settings.Entered.Task.WaitAsync(token);
        await minecraft.DisposeAsync();
        try
        {
            if ((await waitingInstall).IsSuccess) throw new InvalidDataException("Shutdown accepted a late game installation.");
        }
        catch (OperationCanceledException) { }
        if (settings.Active != 0) throw new InvalidDataException("Minecraft close returned before its host policy query retired.");
        await serverStop.CancelAsync();
        try { await serving; } catch (OperationCanceledException) when (serverStop.IsCancellationRequested) { }
        Console.WriteLine("Native Java installation used host HTTP, canonical native downloads/work and imported task controls.");
    }
    private static async Task ServeAsync(TcpListener server, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
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
    }
    private sealed class Metadata(string address) : HttpMessageHandler
    {
        private const string Index = """{"objects":{}}""";
        private static string VersionDocument => """{"id":"1.20.1","type":"release","mainClass":"fixture.Main","libraries":[],"assetIndex":{"id":"native-index","url":"https://piston-meta.mojang.com/v1/packages/native-index.json","sha1":"INDEX_HASH","size":14},"downloads":{"client":{"url":"https://piston-data.mojang.com/native-client.jar","sha1":"aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d","size":5}}}""".Replace("INDEX_HASH", Hash(Index), StringComparison.Ordinal);
#pragma warning disable CA5350 // Fixture identities match Mojang's SHA-1 metadata contract.
        private static string Hash(string value) => Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(value)));
#pragma warning restore CA5350
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac-os" : "linux";
            string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            string platform = os + "-" + (arch == "arm64" ? "arm64" : "x64");
            if (OperatingSystem.IsLinux()) platform = "linux";
            if (OperatingSystem.IsMacOS() && arch == "x64") platform = "mac-os";
            string relative = OperatingSystem.IsWindows() ? "bin/java.exe" : "bin/java";
            string json = request.RequestUri!.AbsolutePath.EndsWith("all.json", StringComparison.Ordinal)
                ? """{"PLATFORM":{"java-runtime-native-test":[{"version":{"name":"21.0.2"},"manifest":{"url":"https://piston-data.mojang.com/native-java.json"}}]}}""".Replace("PLATFORM", platform, StringComparison.Ordinal)
                : """{"files":{"EXECUTABLE":{"executable":true,"downloads":{"raw":{"url":"ADDRESS","sha1":"aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d","size":5}}},"LICENSE":{"downloads":{"raw":{"url":"ADDRESS","sha1":"aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d","size":5}}}}}""".Replace("EXECUTABLE", relative, StringComparison.Ordinal).Replace("ADDRESS", address, StringComparison.Ordinal);
            if (request.RequestUri.AbsolutePath.EndsWith("version_manifest_v2.json", StringComparison.Ordinal))
                json = """{"versions":[{"id":"1.20.1","url":"https://piston-meta.mojang.com/v1/packages/native-version.json","sha1":"VERSION_HASH"}]}""".Replace("VERSION_HASH", Hash(VersionDocument), StringComparison.Ordinal);
            else if (request.RequestUri.AbsolutePath.EndsWith("native-version.json", StringComparison.Ordinal)) json = VersionDocument;
            else if (request.RequestUri.AbsolutePath.EndsWith("native-index.json", StringComparison.Ordinal)) json = Index;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class InstallSettings : ISettingsSnapshotReader, ISettingsPolicyReader
    {
        internal int Reads;
        internal volatile bool Hold;
        internal int Active;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<CapturedSettingsSnapshot> CaptureAsync(CancellationToken token = default) => ValueTask.FromResult(new CapturedSettingsSnapshot(0, []));
        public async ValueTask<XsrResult<SettingsEffectiveSnapshot>> ReadAsync(SettingsEffectiveQuery query, CancellationToken token = default)
        {
            await Task.Yield(); token.ThrowIfCancellationRequested(); Reads++;
            if (Hold)
            {
                Interlocked.Increment(ref Active); Entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { Interlocked.Decrement(ref Active); }
            }
            return XsrResult.Success(new SettingsEffectiveSnapshot(0,
            [
                new("network.file-concurrency", new(SettingsOverrideMode.Custom, "2"), SettingsLayer.Global, SettingsApplyTiming.Immediate, null),
                new("network.file-retry", new(SettingsOverrideMode.Custom, "false"), SettingsLayer.Global, SettingsApplyTiming.Immediate, null),
                new("network.game-source", new(SettingsOverrideMode.Custom, "official-only"), SettingsLayer.Global, SettingsApplyTiming.Immediate, null),
                new("install.inherit-vanilla", new(SettingsOverrideMode.Custom, "false"), SettingsLayer.Global, SettingsApplyTiming.Immediate, null),
                new("game.default-isolation", new(SettingsOverrideMode.Custom, "none"), SettingsLayer.Global, SettingsApplyTiming.Immediate, null)
            ]));
        }
    }
    private sealed class Registry : IJavaRuntimeRegistrationStore
    {
        internal int Writes;
        public JavaRuntimeRegistrySnapshot Read() => new(0, []);
        public XsrResult Write(long expectedRevision, IReadOnlyList<JavaRuntimeRegistration> registrations)
        { Writes++; throw new InvalidOperationException("An unverified fixture must not be registered."); }
    }
}
