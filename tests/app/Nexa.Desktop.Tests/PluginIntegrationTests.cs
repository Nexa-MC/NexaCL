using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexa.Desktop.Ui;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;
using Nexa.Xsr.Renderer;
using PluginSidecar.Packages;
using PluginSidecar.WasmRuntime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void PluginDesktopIntegration() => PluginDesktopIntegrationAsync().GetAwaiter().GetResult();
    private static async Task PluginDesktopIntegrationAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), addProfile: true);
        var business = new PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Foundation.Commands);
        await using var engine = new GuestExecutionServer(); using var endpoint = new ChunkedPluginEndpoint(engine);
        var exchange = new ChunkedPluginExchange(endpoint);
        string sample = Environment.GetEnvironmentVariable("NEXA_DESKTOP_PLUGIN_SAMPLE") ?? Path.Combine(AppContext.BaseDirectory, "plugin-fixtures", "desktop.zip");
        if (!File.Exists(sample)) throw new FileNotFoundException("Run python eng/plugins/prepare.py before Desktop plugin tests.", sample);
        Guid session = Guid.NewGuid();
        await using var controller = new PluginPageController(fixture.Shell, fixture.Intents, fixture.Store, fixture.Feedback,
            Path.Combine(fixture.TemporaryDirectory, "plugins"), business.Routes,
            (bytes, token) => RemoteWasmPluginMachine.CreateAsync(exchange, bytes, token), _ => Task.FromResult<string?>(null),
            () => true, () => true, () => session, false);
        using var package = VerifiedPluginPackage.Parse(await File.ReadAllBytesAsync(sample), "example.desktop", "1.0.0");
        var report = await controller.Application.LoadAsync(package, package.Manifest.RequestedPermissions, default);
        if (report.State != LoadState.Active) throw new InvalidOperationException(report.Error?.Code + " at " + report.LastStage);
        fixture.Controller.PluginsPage = controller.Page;
        var scene = fixture.Shell.Render(new(1040, 900));
        Emit(fixture.Intents, "ui.navigation.plugins", FindByKey(fixture.Shell, scene, "NavigationPlugins").Entity);
        scene = fixture.Shell.Render(new(1040, 900));
        AssertEqual(controller.Page, fixture.Shell.Stage.Navigation.Current);
        AssertEqual("navigation.plugins", fixture.Shell.SelectedNavigationId.Value);
        AssertTrue(scene.Nodes.Any(node => node.Text == "example.desktop 1.0.0"));
        Emit(fixture.Intents, "ui.plugins.manage", FindByKey(fixture.Shell, scene, "PluginOpenPage").Entity);
        scene = fixture.Shell.Render(new(1040, 900));
        var button = FindByKey(fixture.Shell, scene, "ExamplePluginAction").Entity;
        AssertTrue(scene.Nodes.Any(node => node.Text == "Plugin page / 插件页面"));
        Emit(fixture.Intents, "ui.plugin.action", button);
        for (int i = 0; i < 200 && fixture.Feedback.Snapshot().Dialog is null; i++) { fixture.Shell.Render(new(1040, 900)); await Task.Delay(5); }
        AssertTrue(fixture.Feedback.Snapshot().Dialog is not null);
        var dialog = fixture.Feedback.Snapshot().Dialog!;
        AssertTrue(dialog.Message.Contains("example.desktop:hello", StringComparison.Ordinal));
        fixture.Feedback.ResolveDialog(dialog.Id, true);
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            while (true)
            {
                var tasks = await Invoke("Tasks", "List", PageRequest.Write(new(null, 32)));
                AssertTrue(tasks.Success); var operation = Page<OperationRecord>.Read(tasks.Value).Items.Single();
                if (operation.State == OperationState.Completed) { AssertEqual("Desktop action completed", TextResult.Read(operation.Result).Text); break; }
                if (operation.Failure is not null) throw new InvalidOperationException(operation.Failure.Code);
                await Task.Delay(5, deadline.Token);
            }
        }
        // Public account projection exposes no tokens and another load cannot reuse its entity IDs.
        var result = await Invoke("Accounts", "List", PageRequest.Write(new(null, 32)));
        AssertTrue(result.Success);
        var accounts = Page<AccountPublic>.Read(result.Value); AssertEqual(1, accounts.Items.Length);
        AssertEqual("Player", accounts.Items[0].DisplayName);
        result = await Invoke("Accounts", "ReadPublic", EntityRequest.Write(new(accounts.Items[0].Id)));
        AssertTrue(result.Success);
        var fileResult = await Invoke("Files", "OpenPackageFile", PackageFileRequest.Write(new("../../profiles/accounts.json")));
        AssertFalse(fileResult.Success);
        result = await Invoke("Accounts", "Select", EntityRequest.Write(new(accounts.Items[0].Id)));
        AssertFalse(result.Success); // sample has read permission only
        await controller.Application.StopAsync("example.desktop");
        fixture.Shell.Render(new(1040, 900));
        AssertEqual(controller.Page, fixture.Shell.Stage.Navigation.Current);
        AssertFalse(fixture.Shell.Tree.IsAlive(button));
        AssertEqual(0, controller.Application.Snapshot().Pages.Length);
        result = await Invoke("Accounts", "List", PageRequest.Write(new(null, 32))); AssertFalse(result.Success);
        var malicious = RepackagePage(await File.ReadAllBytesAsync(sample), "<nxml-page><Page Key=\"Bad\"><Text Content=\"{state accounts.profiles}\" /></Page></nxml-page>");
        using var rejected = VerifiedPluginPackage.Parse(malicious, "example.desktop", "1.0.0");
        var failure = await controller.Application.LoadAsync(rejected, rejected.Manifest.RequestedPermissions, default);
        AssertEqual(LoadState.Failed, failure.State); AssertEqual(0, controller.Application.Snapshot().Pages.Length);
        AssertEqual(0, controller.Application.Runtime.Active.Length);

        ValueTask<AbiReply<AbiValue>> Invoke(string group, string name, AbiValue value)
        {
            var route = Abi100Catalog.Operations.Single(o => o.Group == group && o.Name == name);
            return controller.Application.Runtime.InvokeAsync(report.Identity, route.ContractId, route.MethodId, value,
                new(Guid.NewGuid(), TimeSpan.FromSeconds(5), default) { IdempotencyKey = Guid.NewGuid().ToString("N") });
        }
    }
    private static byte[] RepackagePage(byte[] source, string markup)
    {
        using var package = VerifiedPluginPackage.Parse(source, "example.desktop", "1.0.0");
        var bytes = Encoding.UTF8.GetBytes(markup);
        var manifest = package.Manifest with
        {
            Payloads = package.Manifest.Payloads.Select(p => p.Path == "page.nxml" ? p with
            { Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() } : p).ToImmutableArray()
        };
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var entry = zip.CreateEntry("plugin.json").Open()) entry.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, PackageJsonContext.Default.PluginManifest));
            foreach (var payload in manifest.Payloads)
            { using var entry = zip.CreateEntry(payload.Path).Open(); entry.Write(payload.Path == "page.nxml" ? bytes : package.CopyPayload(payload.Path)); }
        }
        return output.ToArray();
    }
    private static void PluginManagerRecoveryAndSafeMode() => PluginManagerRecoveryAndSafeModeAsync().GetAwaiter().GetResult();
    private static async Task PluginManagerRecoveryAndSafeModeAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        foreach (bool storageFailure in new[] { true, false })
        {
            string storage = Path.Combine(fixture.TemporaryDirectory, "plugin-storage-" + storageFailure);
            if (storageFailure) File.WriteAllText(storage, "blocked");
            await using var controller = new PluginPageController(fixture.Shell, fixture.Intents, fixture.Store, fixture.Feedback,
                storage, [], (_, _) => throw new InvalidOperationException("Safe mode cannot execute plugins."),
                _ => throw new InvalidOperationException("Safe mode cannot pick a package."), () => true, () => true, () => Guid.Empty, !storageFailure);
            fixture.Shell.Stage.Navigation.Replace(controller.Page);
            var scene = fixture.Shell.Render(new(1040, 900));
            AssertTrue(scene.Nodes.Any(node => node.Text == (storageFailure ? "插件存储不可用" : "安全模式下已禁用插件")));
            AssertFalse(scene.Nodes.Any(node => node.Text == "加载开发包"));
        }
        var confirmation = new PluginConfirmation(fixture.Feedback);
        using var cancel = new CancellationTokenSource();
        var answer = confirmation.AskAsync("Action", "Captured request", cancel.Token);
        cancel.Cancel();
        try { await answer; throw new InvalidOperationException("Cancelled approval completed."); } catch (OperationCanceledException) { }
        AssertTrue(fixture.Feedback.Snapshot().Dialog is null);
    }

}
