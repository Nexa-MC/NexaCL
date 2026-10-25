using System.Security.Cryptography;
using System.Text;
using Nexa.Desktop.Ui;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static SidecarRegistrationItem PageDeclaration(string id, string target, SidecarNxmlOperation operation, string markup)
    {
        byte[] payload = SidecarNxmlDocument.Encode(new(target, operation, "main.nxml", new Dictionary<string, string> { ["main.nxml"] = markup }));
        return new(SidecarRegistrationKind.UiModule, id, 0, 0, payload, SHA256.HashData(payload));
    }

    private static void SidecarStandalonePagesNavigateAndRetire()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = [];
        using var presentation = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        var (session, peer) = ActivateCaptionPatch(new(), "", declarations:
            [PageDeclaration("test.navigation", "#navigation-toggle", SidecarNxmlOperation.Before,
                "<NavigationItem Key=\"AccountNavigation\" Label=\"账户\" Content=\"未登录\" Icon=\"lucide/user\" Height=\"42\" Command=\"ui.sidecar.page.open\" Argument=\"#AccountPage\" />"),
             PageDeclaration("test.page", "#AccountPage", SidecarNxmlOperation.Page,
                "<nxml-page><Page Key=\"AccountPage\" Background=\"#FFFFFF\" /></nxml-page>")],
            features: SidecarFeatures.All, nxml: adapter).GetAwaiter().GetResult();
        using (session) using (peer)
        {
            fixture.Shell.Renderer.ReducedMotion = true;
            var scene = fixture.Shell.Render(new(1000, 650));
            var original = fixture.Shell.Stage.Navigation.Current;
            string originalName = fixture.Shell.Tree.Name(original);
            var originalNavigation = fixture.Shell.SelectedNavigationId;
            var button = FindByKey(fixture.Shell, scene, "AccountNavigation").Entity;
            AssertTrue(fixture.Shell.Renderer.Activate(button));
            scene = fixture.Shell.Render(new(1000, 650));
            var account = fixture.Shell.Stage.Navigation.Current;
            AssertEqual("AccountPage", fixture.Shell.Tree.Name(account));
            AssertEqual(0, fixture.Shell.Tree.Children(account).Count);
            AssertTrue(fixture.Shell.Renderer.Activate(fixture.Shell.NavigationEntities[originalNavigation]));
            fixture.Shell.Render(new(1000, 650));
            AssertEqual(originalName, fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
            AssertTrue(fixture.Shell.Renderer.Activate(button));
            fixture.Shell.Render(new(1000, 650));
            AssertEqual(account, fixture.Shell.Stage.Navigation.Current);
            session.Dispose();
            fixture.Shell.Render(new(1000, 650));
            AssertEqual(originalName, fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
            AssertFalse(fixture.Shell.Tree.IsAlive(account));
            AssertFalse(fixture.Shell.Renderer.Activate(button));
            AssertEqual(0, warnings.Count);
        }
    }

    private static void SidecarOnboardingResumesSetup()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var fallback = fixture.Shell.Stage.Navigation.Current;
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = [];
        using var presentation = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add, fallback);
        var (session, peer) = ActivateCaptionPatch(new(), "", declarations:
            [new(SidecarRegistrationKind.Command, "ui.test.login", 0, 0),
             PageDeclaration("test.onboarding", "#AccountSetup", SidecarNxmlOperation.OnboardingPage,
                "<nxml-page><Page Key=\"AccountSetup\"><StackPanel><TextInput Key=\"Password\" Label=\"密码\" IsPassword=\"true\" Height=\"36\" /><Button Key=\"PendingLogin\" Label=\"登录\" Height=\"36\" Command=\"ui.test.login\" /><Button Key=\"ContinueSetup\" Label=\"继续设置\" Height=\"36\" Content=\"稍后登录\" Command=\"ui.sidecar.onboarding.continue\" /></StackPanel></Page></nxml-page>")],
            features: SidecarFeatures.All, nxml: adapter).GetAwaiter().GetResult();
        using (session) using (peer)
        {
            fixture.Shell.Renderer.ReducedMotion = true;
            var scene = fixture.Shell.Render(new(1000, 650));
            var page = fixture.Shell.Stage.Navigation.Current;
            AssertEqual("AccountSetup", fixture.Shell.Tree.Name(page));
            var draft = FindByKey(fixture.Shell, scene, "Password").Entity;
            fixture.Shell.Renderer.SetTextInputValue(draft, "must not survive setup");
            var receive = session.RunReceiveLoopAsync().AsTask();
            AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "PendingLogin").Entity));
            var request = peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            AssertEqual(SidecarMessageType.CommandRequest, request.MessageType);
            AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "ContinueSetup").Entity));
            var cancelled = peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            AssertEqual(SidecarMessageType.Cancel, cancelled.MessageType);
            AssertEqual(request.CorrelationId.Value, SidecarControlMessages.DecodeCancel(cancelled.Payload.Span).CorrelationId);
            fixture.Shell.Render(new(1000, 650));
            AssertEqual(fallback, fixture.Shell.Stage.Navigation.Current);
            AssertFalse(fixture.Shell.Tree.IsAlive(draft));
            AssertFalse(fixture.Shell.Tree.IsAlive(page));
            fixture.Shell.Render(new(1000, 650));
            AssertEqual(fallback, fixture.Shell.Stage.Navigation.Current);
            AssertEqual(0, warnings.Count);
            session.Dispose(); receive.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
    }

    private static void SidecarNxmlPresentsFuturePagesAndRoutesActions()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = [];
        using var presentation = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        var document = new SidecarNxmlDocument("#NeverAdaptedPage", SidecarNxmlOperation.Replace, "main.nxml",
            new Dictionary<string, string>
            {
                ["main.nxml"] = "<StackPanel><TextInput Key=\"Draft\" Label=\"draft\" Height=\"32\" /><FutureAction Key=\"NewAction\" Label=\"action\" Content=\"{data plugin.title}\" ArgumentFrom=\"Draft\" Command=\"ui.test.host\" /><Button Key=\"HostAction\" Label=\"host\" Command=\"ui.test.native\" Height=\"36\" /><Dynamic Key=\"Rows\" /></StackPanel>",
                ["FutureAction.c.nxml"] = "<nxml-component><Button Height=\"36\" /></nxml-component>",
                ["Rows/001.d.nxml"] = "<nxml-dynamic><Text Content=\"{data plugin.title}\" /></nxml-dynamic>",
                ["style.nss"] = "/*" + new string(' ', 70 * 1024) + "*/",
            });
        byte[] payload = SidecarNxmlDocument.Encode(document);
        var (session, peer) = ActivateCaptionPatch(new(), "", declarations:
            [new(SidecarRegistrationKind.Command, "ui.test.host", 0, 0),
             new(SidecarRegistrationKind.State, "plugin.title", 0, 0),
             new(SidecarRegistrationKind.UiModule, "plugin.nxml", 0, 0, payload, SHA256.HashData(payload))],
            features: SidecarFeatures.All, initialState: [(1, Encoding.UTF8.GetBytes("NXML action"))], nxml: adapter).GetAwaiter().GetResult();
        using (session) using (peer)
        {
            var tree = fixture.Shell.Tree;
            var page = NxmlUiLoader.LoadDetached(NxmlCompiler.Compile(NxmlParser.Parse("<StackPanel Key=\"NeverAdaptedPage\"><Text Key=\"Original\" Content=\"host content\" /></StackPanel>")), tree, fixture.Store);
            var original = tree.Children(page).Single();
            fixture.Shell.Stage.Navigation.Replace(page); fixture.Shell.Renderer.ReducedMotion = true;
            var scene = fixture.Shell.Render(new(1000, 650));
            var button = FindByKey(fixture.Shell, scene, "NewAction").Entity;
            var draft = FindByKey(fixture.Shell, scene, "Draft").Entity;
            fixture.Shell.Renderer.SetTextInputValue(draft, "from NXML");
            AssertTrue(scene.Nodes.Any(node => node.Text == "NXML action"));
            AssertFalse(scene.Nodes.Any(node => node.Text == "host content"));
            AssertTrue(tree.IsAlive(original)); AssertEqual(1, tree.Children(page).Count);
            var loop = session.RunReceiveLoopAsync().AsTask();
            int received = 0, handledByHost = 0;
            fixture.Intents.IntentReceived += (_, _) => received++;
            fixture.Intents.IntentEmitted += (_, _) => handledByHost++;
            AssertTrue(fixture.Shell.Renderer.Activate(button));
            var request = peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var call = SidecarDataMessages.DecodeBinaryRequest(request.Payload.Span);
            AssertEqual(1u, call.ContractId); AssertEqual("from NXML", Encoding.UTF8.GetString(call.Value.Span));
            AssertEqual(1, received); AssertEqual(0, handledByHost);
            AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "HostAction").Entity));
            AssertEqual(2, received); AssertEqual(1, handledByHost);
            peer.SendAsync(new(SidecarProtocol.Version, SidecarMessageType.CommandResult, SidecarFrameTraits.Final,
                request.CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, new(0, []), null))).AsTask().GetAwaiter().GetResult();
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Store.Changed += change => { if (change.Id == adapter.State) changed.TrySetResult(); };
            session.Mirror!.Store.Publish(session.Mirror.Store.Resolve(XsrSemanticId.Parse("plugin.title")), "updated locally");
            changed.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            scene = fixture.Shell.Render(new(1000, 650));
            AssertEqual(button, FindByKey(fixture.Shell, scene, "NewAction").Entity);
            AssertEqual("from NXML", tree.GetComponent<XsrUiTextInput>(draft)!.ReadDraft());
            AssertTrue(scene.Nodes.Any(node => node.Text == "updated locally"));
            tree.GetComponent<XsrUiText>(original)!.Content = "host changed underneath";
            tree.MarkDirty(original, XsrUiDirtyKinds.State);
            session.Dispose(); loop.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            fixture.Intents.Emit(XsrSemanticId.Parse("ui.test.host"), button, XsrCorrelationId.Create());
            AssertEqual(1, handledByHost);
            scene = fixture.Shell.Render(new(1000, 650));
            AssertTrue(scene.Nodes.Any(node => node.Text == "host changed underneath"));
            AssertFalse(fixture.Shell.Renderer.Activate(button)); AssertEqual(0, warnings.Count);
        }
    }
}
