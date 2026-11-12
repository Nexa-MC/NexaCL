using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using Nexa.Desktop.Ui;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition;
using Nexa.Services.Composition.Plugins;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;
using Nexa.Xsr.State;
using PluginSidecar.Packages;
using PluginSidecar.WasmRuntime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private sealed class PluginAdapterConfirmation : IActionConfirmation
    {
        public int Count;
        public ValueTask<bool> ConfirmAsync(ActionConfirmationRequest request, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Count++; return ValueTask.FromResult(true); }
    }
    private static void PluginBusinessLifecycle() => PluginBusinessLifecycleAsync().GetAwaiter().GetResult();
    private static async Task PluginBusinessLifecycleAsync()
    {
        var recording = new RecordingStartRoute();
        var instances = new List<MinecraftInstanceDescriptor>();
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource(instances), CreateRecordingRuntime(recording), addProfile: true, installSource: new PluginInstallSource());
        string instancePath = Path.Combine(fixture.TemporaryDirectory, "minecraft", "versions", "fixture"); Directory.CreateDirectory(instancePath);
        await File.WriteAllTextAsync(Path.Combine(instancePath, "fixture.json"), """{"id":"fixture","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","_minecraftVersion":"1.21.1","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.0"}]}""");
        await new MinecraftInstanceMetadataStore().SaveAsync(instancePath, new() { InstanceIsolation = true });
        await PreparePluginWorldFixture(instancePath);
        var descriptor = Instance("fixture"); instances.Add(descriptor with { DirectoryPath = instancePath, Version = descriptor.Version with { DirectoryPath = instancePath } });
        await fixture.Library.Service.RefreshAsync();
        var worldBackups = new Nexa.Services.Setup.ContentBackupService(Path.Combine(fixture.TemporaryDirectory, "world-backups"));
        var pluginFoundation = FoundationRuntimeComposer.ComposeWithRoutes(fixture.Foundation.Host, configureRoutes: (commands, queries) => WorldSnapshotRuntime.Register(commands, queries, fixture.Store, worldBackups));
        await using var business = new PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Onboarding.Commands, fixture.Minecraft.Commands, fixture.Minecraft.Queries, pluginFoundation.Commands, pluginFoundation.Queries);
        business.BindAccountLaunch(fixture.Service, fixture.Onboarding.LaunchIdentityResolver);
        business.BindAccounts(fixture.Onboarding.Queries);
        business.BindDownloads(fixture.Foundation.Host.Downloads);
        var installCommands = new Nexa.Xsr.Runtime.XsrCommandRouterBuilder(); int installCalls = 0;
        installCommands.Register<Nexa.Services.Minecraft.Install.MinecraftInstallCommand>(Nexa.Services.Minecraft.Install.MinecraftInstallRoutes.Run, (command, token) =>
        { token.ThrowIfCancellationRequested(); AssertTrue(command.EditFingerprint is not null); installCalls++; return ValueTask.FromResult(Nexa.Xsr.XsrResult.Success()); });
        business.BindInstall(fixture.InstallCatalog.Commands, fixture.InstallCatalog.Queries, installCommands.Build(new NoopDispatchObserver()));
        using var catalog = ResourceCatalogRuntimeComposer.Compose(new PluginCatalogSource(), favoritesPath: Path.Combine(fixture.TemporaryDirectory, "favorites.json"));
        business.BindCatalog(catalog.Commands!, catalog.Queries);
        await using var engine = new GuestExecutionServer(); using var endpoint = new ChunkedPluginEndpoint(engine); var exchange = new ChunkedPluginExchange(endpoint);
        var session = new MinecraftProcessSnapshot(Guid.NewGuid(), "fixture", 42, MinecraftProcessState.Running, null, DateTimeOffset.UtcNow, null)
        { InstanceDirectory = Path.Combine(fixture.TemporaryDirectory, "minecraft", "versions", "fixture") };
        Guid worker = Guid.NewGuid();
        var stateId = fixture.Store.Resolve(MinecraftProcessStateComposition.SessionsKey);
        Publish(session);
        await using var controller = new PluginPageController(fixture.Shell, fixture.Intents, fixture.Store, fixture.Feedback, Path.Combine(fixture.TemporaryDirectory, "plugins"), business.Routes,
            (bytes, token) => RemoteWasmPluginMachine.CreateAsync(exchange, bytes, token), _ => Task.FromResult<string?>(null), () => true, () => true, () => worker, false);
        controller.BindBusiness(business);
        using var original = VerifiedPluginPackage.Parse(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "plugin-fixtures", "advanced.zip")), "example.advanced", "1.0.0");
        var manifest = original.Manifest with
        {
            RequestedPermissions = [.. original.Manifest.RequestedPermissions, "nexa.minecraft.processes.read", "nexa.minecraft.processes.write", "nexa.accounts.write", "nexa.settings.read", "nexa.settings.write", "nexa.network.write", "nexa.minecraft.worlds.read", "nexa.minecraft.worlds.write", "nexa.minecraft.content.read", "nexa.minecraft.content.write", "nexa.catalog.read", "nexa.catalog.write", "nexa.platform.read", "nexa.platform.write", "nexa.minecraft.launch.read", "nexa.minecraft.launch.write", "nexa.minecraft.install.read", "nexa.minecraft.install.write", "nexa.minecraft.diagnostics.read", "nexa.minecraft.diagnostics.write", "nexa.downloads.read", "nexa.downloads.write"],
            OptionalCapabilities = [.. original.Manifest.OptionalCapabilities, new("nexa.minecraft.processes", 1), new("nexa.settings", 1), new("nexa.network", 1), new("nexa.minecraft.worlds", 1), new("nexa.minecraft.content", 1), new("nexa.catalog", 1), new("nexa.platform", 1), new("nexa.minecraft.launch", 1), new("nexa.minecraft.install", 1), new("nexa.minecraft.diagnostics", 1), new("nexa.downloads", 1)]
        };
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            using (var entry = zip.CreateEntry("plugin.json").Open()) entry.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, PackageJsonContext.Default.PluginManifest));
            foreach (var payload in manifest.Payloads) { using var entry = zip.CreateEntry(payload.Path).Open(); entry.Write(original.CopyPayload(payload.Path)); }
        }
        using var package = VerifiedPluginPackage.Parse(memory.ToArray(), manifest.PluginId, manifest.PluginVersion);
        var report = await controller.Application.LoadAsync(package, manifest.RequestedPermissions, default);
        if (report.State != LoadState.Active) throw new InvalidOperationException("Plugin admission failed: " + report.Error + " cleanup=" + string.Join(",", report.CleanupErrors));
        var listed = await Invoke("Minecraft.Processes", "ListSessions", PageRequest.Write(new(null, 32)));
        AssertTrue(listed.Success); var process = Page<GameSession>.Read(listed.Value).Items.Single(); AssertTrue(process.Running);
        var subscription = await Invoke("Minecraft.Processes", "WatchSession", WatchRequest.Write(new(process.Id, null, Abi100Schemas.GameSession)));
        AssertTrue(subscription.Success); var watch = SubscriptionRef.Read(subscription.Value);
        await Invoke("Resources", "ReadSubscription", SubscriptionRead.Write(new(watch, 32)));
        var stopped = await Invoke("Minecraft.Processes", "Stop", EntityRequest.Write(new(process.Id)));
        AssertTrue(stopped.Success); var operation = OperationRef.Read(stopped.Value);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (recording.LastCancelledSession != session.SessionId) await Task.Delay(5, timeout.Token);
        AssertTrue(operation.Id.Value.Length > 0);
        Publish(session with { State = MinecraftProcessState.Exited, ExitCode = 0, EndedAt = DateTimeOffset.UtcNow });
        while (true)
        {
            var response = await Invoke("Resources", "ReadSubscription", SubscriptionRead.Write(new(watch, 32)));
            if (SubscriptionBatch.Read(response.Value).Items.Any(v => !GameSession.Read(v).Running)) break;
            await Task.Delay(5, timeout.Token);
        }
        AssertFalse(business.DeveloperEnabled);
        var settingQuery = new SettingQuery(new("nexa:settings/developer.enabled"), new(new("nexa:application"), null, "global"));
        var setting = await Invoke("Settings", "GetEffective", SettingQuery.Write(settingQuery));
        AssertTrue(setting.Success); var originalSetting = EffectiveSetting.Read(setting.Value);
        AssertFalse((await Invoke("Settings", "GetEffective", SettingQuery.Write(settingQuery with { Id = new("nexa:settings/network.proxy-password") }))).Success);
        var changed = new SettingChange(settingQuery.Id, settingQuery.Target,
            DataCodec.Map(("mode", new AbiValue.Utf8String("Custom")), ("value", new AbiValue.Utf8String("true"))), originalSetting.Revision);
        var preview = await Invoke("Settings", "PreviewChange", SettingChange.Write(changed)); AssertTrue(preview.Success);
        var plan = MutationPlan.Read(preview.Value);
        var confirmation = new PluginAdapterConfirmation(); business.BindConfirmation(confirmation);
        var applied = await Invoke("Settings", "Apply", PlanRequest.Write(new(plan.Id, plan.Revision, "untrusted-token"))); AssertTrue(applied.Success);
        var applyOperation = OperationRef.Read(applied.Value);
        while (true)
        {
            var status = await Invoke("Tasks", "Read", EntityRequest.Write(new(applyOperation.Id))); AssertTrue(status.Success);
            var record = OperationRecord.Read(status.Value);
            if (record.State == OperationState.Completed) break;
            AssertFalse(record.State is OperationState.Failed or OperationState.Cancelled or OperationState.OutcomeUnknown);
            await Task.Delay(5, timeout.Token);
        }
        AssertEqual(1, confirmation.Count);
        while (!business.DeveloperEnabled) await Task.Delay(5, timeout.Token);
        AssertFalse((await Invoke("Settings", "Apply", PlanRequest.Write(new(plan.Id, plan.Revision, null)))).Success);
        AssertFalse((await Invoke("Settings", "PreviewChange", SettingChange.Write(changed))).Success);
        using (var udp = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
        {
            int port = ((System.Net.IPEndPoint)udp.Client.LocalEndPoint!).Port;
            var opened = await Invoke("Network", "OpenConnection", NetworkConnectionRequest.Write(new(new("nexa:udp"), "127.0.0.1", port, "plugin")));
            AssertTrue(opened.Success); var connection = NetworkConnection.Read(opened.Value);
            var payload = controller.Application.AddBlob(report.Identity, "hello"u8.ToArray());
            AssertTrue((await Invoke("Network", "Send", NetworkSend.Write(new(connection, payload, null)))).Success);
            var received = await udp.ReceiveAsync(timeout.Token); AssertEqual("hello", System.Text.Encoding.UTF8.GetString(received.Buffer));
            await udp.SendAsync(received.Buffer, received.RemoteEndPoint, timeout.Token);
            var echoed = await Invoke("Network", "Receive", NetworkReceive.Write(new(connection, 32))); AssertTrue(echoed.Success);
            var packet = NetworkPacket.Read(echoed.Value); AssertFalse(packet.EndOfStream);
            AssertEqual("hello", System.Text.Encoding.UTF8.GetString(controller.Application.CopyBlob(report.Identity, packet.Data)));
            AssertFalse((await Invoke("Network", "Close", NetworkConnection.Write(connection with { LoadInstanceId = Guid.NewGuid() }))).Success);
            AssertTrue((await Invoke("Network", "Close", NetworkConnection.Write(connection))).Success);
            AssertFalse((await Invoke("Network", "Close", NetworkConnection.Write(connection))).Success);
        }
        using (var http = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
        {
            http.Start(); int port = ((System.Net.IPEndPoint)http.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var socket = await http.AcceptTcpClientAsync(timeout.Token);
                using var stream = socket.GetStream(); byte[] buffer = new byte[4096]; int read = await stream.ReadAsync(buffer, timeout.Token); AssertTrue(read > 0);
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello"u8.ToArray(), timeout.Token);
            });
            var response = await Invoke("Network", "Request", NetworkRequest.Write(new(new("http://127.0.0.1:" + port + "/"), "GET", [], null, null)));
            AssertTrue(response.Success); var networkResponse = NetworkResponse.Read(response.Value); AssertEqual(200L, networkResponse.StatusCode);
            var body = await Invoke("Network", "OpenResponseStream", EntityRequest.Write(new(networkResponse.Id))); AssertTrue(body.Success);
            AssertEqual("hello", System.Text.Encoding.UTF8.GetString(controller.Application.CopyBlob(report.Identity, BlobRef.Read(body.Value))));
            AssertTrue((await Invoke("Network", "Cancel", OperationRequest.Write(new(networkResponse.Id)))).Success);
            await server;
        }
        var accounts = await Invoke("Accounts", "List", PageRequest.Write(new(null, 32)));
        var account = Page<AccountPublic>.Read(accounts.Value).Items.Single();
        var listedInstances = await Invoke("Minecraft.Instances", "List", InstanceListRequest.Write(new(null, new(null, 32)))); AssertTrue(listedInstances.Success);
        var instanceSummary = Page<InstanceSummary>.Read(listedInstances.Value).Items.Single();
        var memoryQuery = new SettingQuery(new("nexa:settings/game.memory"), new(new("nexa:instance"), instanceSummary.Id, "instance"));
        var memorySetting = EffectiveSetting.Read((await Invoke("Settings", "GetEffective", SettingQuery.Write(memoryQuery))).Value);
        var overlay = new SettingsOverlay(memoryQuery.Target, [memorySetting with { Value = DataCodec.Map(("mode", new AbiValue.Utf8String("Custom")), ("value", new AbiValue.Utf8String("4096"))) }]);
        var startedOverlay = await Invoke("Settings", "BeginTemporaryOverlay", SettingsOverlay.Write(overlay)); AssertTrue(startedOverlay.Success);
        var overlayRef = RegistrationRef.Read(startedOverlay.Value);
        var changedMemory = EffectiveSetting.Read((await Invoke("Settings", "GetEffective", SettingQuery.Write(memoryQuery))).Value);
        AssertEqual("Temporary", changedMemory.Source);
        AssertTrue((await Invoke("Settings", "EndTemporaryOverlay", RegistrationRequest.Write(new(overlayRef)))).Success);
        memorySetting = EffectiveSetting.Read((await Invoke("Settings", "GetEffective", SettingQuery.Write(memoryQuery))).Value);
        AssertFalse(memorySetting.Source == "Temporary");
        overlay = overlay with { Values = [overlay.Values[0] with { Revision = memorySetting.Revision }] };
        AssertTrue((await Invoke("Settings", "BeginTemporaryOverlay", SettingsOverlay.Write(overlay))).Success);
        await ExercisePluginWorldContent(Invoke, instanceSummary.Id, instancePath);
        await ExercisePluginContentTransactions(Invoke, instanceSummary.Id, instancePath, fixture.Store);
        await ExercisePluginContentTransfer(Invoke, path => controller.Application.Files.GrantPath(report.Identity, path, true), instanceSummary.Id, fixture.TemporaryDirectory);
        await ExercisePluginCatalog(Invoke);
        await ExercisePluginCatalogInstall(Invoke, business, instanceSummary);
        var analysis = await Invoke("Minecraft.Diagnostics", "Analyze", EntityRequest.Write(new(instanceSummary.Id))); AssertTrue(analysis.Success);
        AssertEqual(instanceSummary.Id, DiagnosticAnalysis.Read(analysis.Value).Instance);
        AssertFalse((await Invoke("Minecraft.Diagnostics", "PreviewRepair", RepairRequest.Write(new(instanceSummary.Id, [new("nexa:findings/forged")], instanceSummary.Revision)))).Success);
        await ExercisePluginPlatform(Invoke, business);
        await ExercisePluginDownloads(Invoke, path => controller.Application.Files.GrantPath(controller.Application.Runtime.Active.Single(), path, true), fixture.TemporaryDirectory);
        recording.OnStart = command => Publish(new MinecraftProcessSnapshot(Guid.NewGuid(), command.InstanceId, 43, MinecraftProcessState.Running, null, DateTimeOffset.UtcNow, null) { InstanceDirectory = instancePath });
        await ExercisePluginInstall(Invoke, instanceSummary.Id); AssertEqual(1, installCalls);
        bool preflightBlocked = false; int preflightCalls = 0;
        var launchExtension = new CompletionPluginEntry("example.launch-extension");
        launchExtension.Callbacks.Add("contribute", input => ValueTask.FromResult(AbiReply<AbiValue>.Ok(LaunchProfile.Write(LaunchProfile.Read(input) with { MemoryMiB = 3072 }))));
        launchExtension.Callbacks.Add("preflight", input =>
        {
            preflightCalls++; AssertEqual(3072L, LaunchProfile.Read(input).MemoryMiB);
            return ValueTask.FromResult(AbiReply<AbiValue>.Ok(ValidationReport.Write(new(!preflightBlocked, []))));
        });
        launchExtension.Initialize = async api =>
        {
            CompletionValue(await api.Minecraft.Launch.RegisterContribution(new(new("example.launch-extension:memory"), ["MemoryMiB"], launchExtension.Handlers["contribute"], true), CompletionPluginEntry.Options()));
            CompletionValue(await api.Minecraft.Launch.RegisterPreflight(new(new("example.launch-extension:preflight"), [], launchExtension.Handlers["preflight"], true), CompletionPluginEntry.Options()));
        };
        AssertEqual(LoadState.Active, (await controller.Application.Runtime.LoadAsync([launchExtension.Source()])).Single().State);
        await ExercisePluginLaunch(Invoke, instanceSummary.Id, account.Id, recording, blocked => preflightBlocked = blocked);
        AssertTrue(preflightCalls >= 3);
        await controller.Application.Runtime.StopAsync("example.launch-extension");
        var signIn = await Invoke("Accounts", "BeginSignIn", SignInRequest.Write(new(new("nexa:accounts/offline"), "application"))); AssertTrue(signIn.Success);
        var signInFlow = SignInChallenge.Read(signIn.Value); AssertEqual(SignInState.Challenge, signInFlow.State);
        var pollSignIn = await Invoke("Accounts", "ContinueSignIn", SignInContinue.Write(new(signInFlow.Flow, Empty.Write(new()), Abi100Schemas.Empty))); AssertTrue(pollSignIn.Success);
        AssertTrue((await Invoke("Accounts", "CancelSignIn", EntityRequest.Write(new(signInFlow.Flow)))).Success);
        var cancelledSignIn = await Invoke("Accounts", "ContinueSignIn", SignInContinue.Write(new(signInFlow.Flow, Empty.Write(new()), Abi100Schemas.Empty))); AssertTrue(cancelledSignIn.Success);
        AssertEqual(SignInState.Cancelled, SignInChallenge.Read(cancelledSignIn.Value).State);
        AssertTrue((await Invoke("Accounts", "SignOut", EntityRequest.Write(new(account.Id)))).Success);
        AssertFalse((await Invoke("Accounts", "SignOut", EntityRequest.Write(new(account.Id)))).Success);
        account = Page<AccountPublic>.Read((await Invoke("Accounts", "List", PageRequest.Write(new(null, 32)))).Value).Items.Single();
        AssertTrue((await Invoke("Accounts", "Remove", EntityRequest.Write(new(account.Id)))).Success);
        AssertFalse((await Invoke("Accounts", "Remove", EntityRequest.Write(new(account.Id)))).Success);
        AssertFalse((await Invoke("Accounts", "RequestLaunchGrant", GrantRequest.Write(new(account.Id, instanceSummary.Id)))).Success);
        await controller.Application.StopAsync(manifest.PluginId);
        var restored = fixture.Foundation.Host.SettingsPolicy.Read(new(instancePath)); AssertTrue(restored.IsSuccess);
        AssertFalse(restored.Value!.Values.Single(v => v.Key == "game.memory").Source == SettingsLayer.Temporary);
        AssertFalse((await Invoke("Minecraft.Processes", "ReadSession", EntityRequest.Write(new(process.Id)))).Success);
        AssertFalse((await Invoke("Resources", "ReadSubscription", SubscriptionRead.Write(new(watch, 32)))).Success);

        void Publish(MinecraftProcessSnapshot value) => fixture.Store.PublishDelta(stateId, new XsrCollectionDelta<MinecraftProcessSnapshot, Guid>(fixture.Store.ReadCollection<MinecraftProcessSnapshot>(stateId).Revision, [value], []));
        ValueTask<AbiReply<AbiValue>> Invoke(string group, string method, AbiValue input)
        {
            var route = Abi100Catalog.Operations.Single(o => o.Group == group && o.Name == method);
            return controller.Application.Runtime.InvokeAsync(report.Identity, route.ContractId, route.MethodId, input,
                new(Guid.NewGuid(), TimeSpan.FromSeconds(5), default) { IdempotencyKey = Guid.NewGuid().ToString("N") });
        }
    }
}
