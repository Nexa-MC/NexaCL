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
    internal static async Task RunAsync(NativeMinecraftClient minecraft, string directory, XsrStateStore replicas, string javaExecutable, CancellationToken token)
    {
        XsrStateStoreBuilder builder = new(); TaskCenterStateContract.DeclareState(builder); var state = builder.Build();
        await using var tasks = new TaskCenterService(state); tasks.AttachExternalSource(minecraft);
        minecraft.AttachTasks((revision, entries) => tasks.PublishExternalSnapshot(minecraft, revision, entries));
        using var scheduler = new WorkScheduler(new() { HttpConcurrency = 2, DiskConcurrency = 2, CpuConcurrency = 2 });
        minecraft.AttachWork(scheduler);
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
        await CatalogCases.RunAsync(minecraft, replicas, token);
        List<string> installationEvents = [];
        minecraft.Installed += root => installationEvents.Add("installed:" + root);
        minecraft.Renamed += (root, previous, current) => installationEvents.Add("renamed:" + previous + ":" + current);
        string gameRoot = Path.Combine(directory, "native-installed-game");
        var game = await minecraft.InstallMinecraftAsync(new(gameRoot, "1.20.1"), token);
        if (!game.IsSuccess) throw new InvalidDataException("Native game installation failed: " + game.Error!.Message);
        if (installationEvents.Count != 1 || installationEvents[0] != "installed:" + gameRoot)
            throw new InvalidDataException("Installation completed before its canonical notification was acknowledged.");
        if (await File.ReadAllTextAsync(Path.Combine(game.Value!.InstanceDirectory, "1.20.1.jar"), token) != "hello"
            || (await minecraft.InstancesAsync(gameRoot, token: token)).Count != 1 || settings.Reads < 2)
            throw new InvalidDataException("Native game installation lost committed bytes, discovery or canonical policy reads.");
        var edit = await minecraft.ReadInstallationAsync(new(gameRoot, "1.20.1"), token);
        var renamed = await minecraft.InstallMinecraftAsync(new(gameRoot, "1.20.1", InstanceName: "1.20.1", EditFingerprint: edit.Fingerprint)
        { NewInstanceName = "renamed-game" }, token);
        if (!renamed.IsSuccess || !File.Exists(Path.Combine(renamed.Value!.InstanceDirectory, "renamed-game.jar"))
            || Directory.Exists(game.Value.InstanceDirectory) || settings.RenameWrites != 1)
            throw new InvalidDataException("Native game rename did not coordinate files with the canonical settings transaction.");
        if (installationEvents.Count < 2 || !installationEvents.Contains("renamed:1.20.1:renamed-game"))
            throw new InvalidDataException("Rename completed before its canonical notification was acknowledged.");
        var recovery = await minecraft.ReadRecoveryAsync(new(renamed.Value!.InstanceDirectory), token);
        if (!recovery.IsSuccess || recovery.Value!.BaselineRevision is not null)
            throw new InvalidDataException("Native recovery comparison invented a successful-run baseline.");
        if (!(await minecraft.ResumeRecoveryAsync(new([gameRoot]), token)).IsSuccess)
            throw new InvalidDataException("Native recovery replay rejected a clean installation.");
        await ImportCases.RunAsync(minecraft, directory, gameRoot, renamed.Value.InstanceDirectory, token);
        await LaunchCases.RunAsync(minecraft, gameRoot, renamed.Value.InstanceDirectory, replicas, javaExecutable, token);
        if (settings.JavaWrites != 1) throw new InvalidDataException("Native Java remediation bypassed canonical settings writes.");
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
    private sealed class InstallSettings : ISettingsSnapshotReader, ISettingsRecoveryTransactions, ISettingsRenameTransactions, ISettingsPolicyWriter
    {
        internal int JavaWrites;
        public ValueTask<XsrResult> SetAsync(SettingsMutation command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (command.Key != "java.runtime" || command.Layer != SettingsLayer.Instance
                || command.Value.Mode != SettingsOverrideMode.Custom || !File.Exists(command.Value.Value)
                || command.InstanceId is null || !Path.IsPathFullyQualified(command.InstanceId))
                throw new InvalidDataException("Unexpected native Java settings mutation.");
            JavaWrites++; return ValueTask.FromResult(XsrResult.Success());
        }
        public async ValueTask<XsrResult<SettingsRecoverySnapshot>> CaptureRecoveryAsync(string instanceDirectory, CancellationToken token = default)
        {
            var effective = await ReadAsync(new(instanceDirectory), token);
            var keys = SettingsPolicySchema.Definitions.Where(item => item.InstanceOverride && item.Key != "instance.development-mode"
                && !item.Key.StartsWith("recovery.", StringComparison.Ordinal)).Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            return XsrResult.Success(new SettingsRecoverySnapshot(instanceDirectory, effective.Value!.Values.Where(item => keys.Contains(item.Key))
                .Select(item => new SettingsRecoveryValue(item.Key, null, item.Value)).ToArray()));
        }
        public ValueTask<XsrResult<RecoverySettingsPlan>> PlanRecoveryAsync(string instanceDirectory, SettingsRecoverySnapshot baseline,
            IReadOnlyList<string> selectedKeys, CancellationToken token = default) => throw new InvalidOperationException("Fixture does not restore settings.");
        public ValueTask<XsrResult> ApplyRecoveryAsync(RecoverySettingsPlan plan, bool reverse, CancellationToken token = default)
            => throw new InvalidOperationException("Fixture does not restore settings.");
        internal int Reads;
        internal int RenameWrites;
        private SettingsRenameReceipt? _rename;
        public async ValueTask<XsrResult<SettingsRenameReceipt>> PrepareRenameAsync(string source, string destination, CancellationToken token = default)
        {
            await Task.Yield(); token.ThrowIfCancellationRequested();
            _rename = new(source, destination, SettingsRenameIdentity.Capture("""{"future-setting":"preserved"}""", null));
            return XsrResult.Success(_rename);
        }
        public async ValueTask<XsrResult> ApplyRenameAsync(SettingsRenameReceipt receipt, bool reverse, CancellationToken token = default)
        {
            await Task.Yield(); token.ThrowIfCancellationRequested();
            if (receipt != _rename || reverse) throw new InvalidDataException("Unexpected native rename transaction.");
            RenameWrites++; return XsrResult.Success();
        }
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
            var values = SettingsPolicySchema.Definitions.Select(definition =>
            {
                string? selected = definition.Key switch
                {
                    "network.file-concurrency" => "2",
                    "network.file-retry" => "false",
                    "network.game-source" => "official-only",
                    "install.inherit-vanilla" => "false",
                    "game.default-isolation" => "none",
                    "game.environment" => "NEXA_NATIVE_PROCESS_MODE=delayed-exit",
                    "game.post-exit" => "echo native-finalization",
                    _ => null
                };
                var value = selected is not null ? new SettingsOverride(SettingsOverrideMode.Custom, selected)
                    : definition.SupportsAuto ? new SettingsOverride(SettingsOverrideMode.Auto)
                    : new SettingsOverride(SettingsOverrideMode.Custom, definition.DefaultValue);
                return new SettingsEffectiveValue(definition.Key, value, selected is null ? SettingsLayer.Builtin : SettingsLayer.Global, definition.Timing, null);
            }).ToArray();
            return XsrResult.Success(new SettingsEffectiveSnapshot(0, values));
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
