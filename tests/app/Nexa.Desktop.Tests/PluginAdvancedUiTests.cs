using Nexa.Desktop.Ui;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Xsr.Renderer;
using PluginSidecar.Packages;
using PluginSidecar.WasmRuntime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void PluginAdvancedUiIntegration() => PluginAdvancedUiIntegrationAsync().GetAwaiter().GetResult();
    private static async Task PluginAdvancedUiIntegrationAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), addProfile: true);
        await using var business = new Nexa.Services.Composition.Plugins.PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Foundation.Commands, fixture.Minecraft.Commands, fixture.Minecraft.Queries);
        await using var engine = new GuestExecutionServer(); using var endpoint = new ChunkedPluginEndpoint(engine); var exchange = new ChunkedPluginExchange(endpoint);
        Guid session = Guid.NewGuid();
        await using var controller = new PluginPageController(fixture.Shell, fixture.Intents, fixture.Store, fixture.Feedback,
            Path.Combine(fixture.TemporaryDirectory, "plugins"), business.Routes, (bytes, token) => RemoteWasmPluginMachine.CreateAsync(exchange, bytes, token),
            _ => Task.FromResult<string?>(null), () => true, () => true, () => session, false);
        controller.BindBusiness(business);
        using var package = VerifiedPluginPackage.Parse(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "plugin-fixtures", "advanced.zip")), "example.advanced", "1.0.0");
        var report = await controller.Application.LoadAsync(package, package.Manifest.RequestedPermissions, default);
        if (report.State != LoadState.Active) throw new InvalidOperationException(report.Error?.Code + " " + report.Error?.Operation);
        var accounts = await Invoke("Accounts", "List", PageRequest.Write(new(null, 32)));
        AssertTrue(accounts.Success); var account = Page<AccountPublic>.Read(accounts.Value).Items.Single();
        var accountWatch = await Invoke("Accounts", "Watch", WatchRequest.Write(new(account.Id, null, Abi100Schemas.AccountPublic)));
        AssertTrue(accountWatch.Success);
        var accountEvents = await Invoke("Resources", "ReadSubscription", SubscriptionRead.Write(new(SubscriptionRef.Read(accountWatch.Value), 32)));
        AssertEqual(account, AccountPublic.Read(SubscriptionBatch.Read(accountEvents.Value).Items.Single()));
        AssertFalse((await Invoke("Accounts", "Watch", WatchRequest.Write(new(account.Id, null, Abi100Schemas.GameSession)))).Success);
        fixture.Shell.Stage.Navigation.Replace(controller.Page); var scene = fixture.Shell.Render(new(1040, 900));
        AssertTrue(scene.Nodes.Any(n => n.Text == "Advanced plugin contribution"));
        AssertTrue(scene.Nodes.Any(n => n.Text == "Advanced command"));
        var targets = await Invoke("UI", "ListExtensionTargets", PageRequest.Write(new(null, 32)));
        AssertTrue(targets.Success); AssertEqual("nexa:plugins/content", Page<UIViewContract>.Read(targets.Value).Items.Single().Id.Value);
        AssertFalse((await Invoke("UI", "DescribeViewContract", QualifiedRequest.Write(new(new("nexa:accounts/sign-in"))))).Success);
        var dialogReply = await Invoke("UI", "ShowDialog", UIDialog.Write(new(new("example.advanced:ask"), new("example.advanced:home"), TextResult.Write(new("initial")))));
        AssertTrue(dialogReply.Success); var dialog = UIDialogRef.Read(dialogReply.Value);
        var watchReply = await Invoke("UI", "WatchViewLifecycle", UIViewWatch.Write(new(dialog.Id, dialog.ViewScope, null)));
        AssertTrue(watchReply.Success); var watch = SubscriptionRef.Read(watchReply.Value);
        scene = fixture.Shell.Render(new(1040, 900));
        AssertTrue(fixture.Shell.Stage.Overlays.Count > 0);
        AssertFalse(controller.HandleShortcut("Ctrl+Alt+P"));
        fixture.Shell.Renderer.SetTextInputValue(FindByKey(fixture.Shell, scene, "text").Entity, "local draft");
        var update = await Invoke("UI", "UpdateModel", UIModelUpdate.Write(new(dialog.Id, Abi100Schemas.TextResult, TextResult.Write(new("updated")), null)));
        if (!update.Success) throw new InvalidOperationException(update.Error!.Code + " dialogs=" + controller.Application.AdvancedUi().Dialogs.Length + " notices=" + string.Join(";", fixture.Feedback.Snapshot().Notifications.Select(n => n.Message)));
        scene = fixture.Shell.Render(new(1040, 900));
        AssertEqual("updated", TextResult.Read(controller.Application.AdvancedUi().Dialogs.Single().Page.Model).Text);
        AssertEqual("local draft", fixture.Shell.Tree.GetComponent<XsrUiTextInput>(FindByKey(fixture.Shell, scene, "text").Entity)!.ReadDraft());
        var surfaceReply = await Invoke("UI", "CreateDrawingSurface", DrawingSurfaceRequest.Write(new(new("example.advanced:canvas"), 120, 100, dialog.ViewScope)));
        AssertTrue(surfaceReply.Success); var surface = DrawingSurfaceRef.Read(surfaceReply.Value);
        var nextSurface = surface with { Revision = new("frame-1") };
        var draw = new DrawSubmission(nextSurface, [new(DrawCommandKind.Rectangle, [0, 0, 120, 100], "#224466", null, null)], surface.Revision);
        AssertTrue((await Invoke("UI", "SubmitDrawCommands", DrawSubmission.Write(draw))).Success);
        AssertFalse((await Invoke("UI", "SubmitDrawCommands", DrawSubmission.Write(draw))).Success);
        var inputReply = await Invoke("UI", "SubscribeInput", InputSubscription.Write(new(nextSurface, [InputEventKind.Pointer, InputEventKind.Text], 2)));
        AssertTrue(inputReply.Success); var inputWatch = SubscriptionRef.Read(inputReply.Value);
        scene = fixture.Shell.Render(new(1040, 900));
        var canvas = FindByKey(fixture.Shell, scene, "plugin-surface-" + surface.Id.Value);
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiRasterImage>(canvas.Entity) is not null);
        fixture.Shell.Renderer.PointerPressed(new(canvas.Rect.X + 5, canvas.Rect.Y + 5));
        fixture.Shell.Renderer.PointerReleased(new(canvas.Rect.X + 5, canvas.Rect.Y + 5));
        var inputEvents = await Invoke("Resources", "ReadSubscription", SubscriptionRead.Write(new(inputWatch, 32)));
        AssertTrue(inputEvents.Success); AssertTrue(SubscriptionBatch.Read(inputEvents.Value).Items.Length > 1);
        var input = FindByKey(fixture.Shell, scene, "text").Entity;
        AssertTrue(fixture.Shell.Renderer.SetTextInputValue(input, "answer from the player"));
        Emit(fixture.Intents, "ui.plugin.action", FindByKey(fixture.Shell, scene, "AdvancedAction").Entity);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (fixture.Feedback.Snapshot().Dialog is null) { fixture.Shell.Render(new(1040, 900)); await Task.Delay(5, timeout.Token); }
        fixture.Shell.Renderer.InsertText("host-only-answer");
        var blocked = await Invoke("Resources", "ReadSubscription", SubscriptionRead.Write(new(inputWatch, 32)));
        AssertEqual(0, SubscriptionBatch.Read(blocked.Value).Items.Length);
        fixture.Feedback.ResolveDialog(fixture.Feedback.Snapshot().Dialog!.Id, true);
        while (true)
        {
            var tasks = await Invoke("Tasks", "List", PageRequest.Write(new(null, 32)));
            var operation = Page<OperationRecord>.Read(tasks.Value).Items.Single();
            if (operation.State == OperationState.Completed) { AssertEqual("answer from the player", TextResult.Read(operation.Result).Text); break; }
            if (operation.Failure is not null) throw new InvalidOperationException(operation.Failure.Code);
            await Task.Delay(5, timeout.Token);
        }
        AssertTrue((await Invoke("UI", "CloseDialog", EntityRequest.Write(new(dialog.Id)))).Success);
        fixture.Shell.Render(new(1040, 900)); AssertFalse(fixture.Shell.Stage.Overlays.Any(overlay => fixture.Shell.Tree.GetComponent<XsrUiOverlayLayer>(overlay)?.IsModal == true));
        AssertEqual(0, controller.Application.Drawings().Length);
        var events = await Invoke("Resources", "ReadSubscription", SubscriptionRead.Write(new(watch, 32)));
        AssertTrue(events.Success); AssertTrue(SubscriptionBatch.Read(events.Value).Items.Any(v => TextResult.Read(v).Text == "closed"));
        AssertFalse((await Invoke("UI", "CloseDialog", EntityRequest.Write(new(dialog.Id)))).Success);
        var pageWatch = await Invoke("UI", "WatchViewLifecycle", UIViewWatch.Write(new(new("example.advanced:home"), report.Identity.LoadInstanceId, null)));
        AssertTrue(pageWatch.Success);
        AssertTrue((await Invoke("UI", "Navigate", UINavigateRequest.Write(new(new("example.advanced:home"), new AbiValue.Null())))).Success);
        fixture.Shell.Render(new(1040, 900));
        AssertTrue(controller.HandleShortcut("Ctrl+Alt+P"));
        while (fixture.Feedback.Snapshot().Dialog is null) { fixture.Shell.Render(new(1040, 900)); await Task.Delay(5, timeout.Token); }
        fixture.Feedback.ResolveDialog(fixture.Feedback.Snapshot().Dialog!.Id, true);
        var lifecycle = await Invoke("Resources", "ReadSubscription", SubscriptionRead.Write(new(SubscriptionRef.Read(pageWatch.Value), 32)));
        AssertTrue(SubscriptionBatch.Read(lifecycle.Value).Items.Any(v => TextResult.Read(v).Text == "open"));
        await controller.Application.StopAsync("example.advanced");
        scene = fixture.Shell.Render(new(1040, 900));
        AssertFalse(scene.Nodes.Any(n => n.Text == "Advanced plugin contribution" || n.Text == "Advanced command"));
        AssertFalse(controller.HandleShortcut("Ctrl+Alt+P"));

        ValueTask<AbiReply<AbiValue>> Invoke(string group, string method, AbiValue input)
        {
            var operation = Abi100Catalog.Operations.Single(o => o.Group == group && o.Name == method);
            return controller.Application.Runtime.InvokeAsync(report.Identity, operation.ContractId, operation.MethodId, input,
                new(Guid.NewGuid(), TimeSpan.FromSeconds(5), default) { IdempotencyKey = Guid.NewGuid().ToString("N") });
        }
    }
}
