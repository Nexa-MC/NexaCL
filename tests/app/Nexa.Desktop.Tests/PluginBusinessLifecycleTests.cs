using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using Nexa.Desktop.Ui;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr.State;
using PluginSidecar.Packages;
using PluginSidecar.WasmRuntime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void PluginBusinessLifecycle() => PluginBusinessLifecycleAsync().GetAwaiter().GetResult();
    private static async Task PluginBusinessLifecycleAsync()
    {
        var recording = new RecordingStartRoute();
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), CreateRecordingRuntime(recording), addProfile: true);
        await using var business = new PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Foundation.Commands, fixture.Minecraft.Commands, fixture.Minecraft.Queries);
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
            RequestedPermissions = [.. original.Manifest.RequestedPermissions, "nexa.minecraft.processes.read", "nexa.minecraft.processes.write", "nexa.accounts.write"],
            OptionalCapabilities = [.. original.Manifest.OptionalCapabilities, new("nexa.minecraft.processes", 1)]
        };
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            using (var entry = zip.CreateEntry("plugin.json").Open()) entry.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, PackageJsonContext.Default.PluginManifest));
            foreach (var payload in manifest.Payloads) { using var entry = zip.CreateEntry(payload.Path).Open(); entry.Write(original.CopyPayload(payload.Path)); }
        }
        using var package = VerifiedPluginPackage.Parse(memory.ToArray(), manifest.PluginId, manifest.PluginVersion);
        var report = await controller.Application.LoadAsync(package, manifest.RequestedPermissions, default); AssertEqual(LoadState.Active, report.State);
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
        var accounts = await Invoke("Accounts", "List", PageRequest.Write(new(null, 32)));
        var account = Page<AccountPublic>.Read(accounts.Value).Items.Single();
        AssertTrue((await Invoke("Accounts", "Remove", EntityRequest.Write(new(account.Id)))).Success);
        AssertFalse((await Invoke("Accounts", "Remove", EntityRequest.Write(new(account.Id)))).Success);
        await controller.Application.StopAsync(manifest.PluginId);
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
