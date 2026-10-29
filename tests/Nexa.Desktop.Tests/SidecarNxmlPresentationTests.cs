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
    private static void SidecarLinksAllowOnlyUserFacingHttps()
    {
        foreach (var invalid in new[] { "file:///etc/passwd", "javascript:alert(1)", "http://example.com", "https://user:password@example.com", "https://example.com/\n" })
            AssertTrue(!DesktopSidecarNxml.TryHttpsLink(invalid, out _));
        AssertTrue(DesktopSidecarNxml.TryHttpsLink("https://example.com/settings", out var link));
        AssertEqual("example.com", link!.Host);
        AssertTrue(DesktopSidecarNxml.CanCopyText("ABCD-EFGH"));
        AssertFalse(DesktopSidecarNxml.CanCopyText(""));
        AssertFalse(DesktopSidecarNxml.CanCopyText("code\nsecret"));
        AssertFalse(DesktopSidecarNxml.CanCopyText(new string('x', 4097)));
    }

    private static void SidecarScrollDispatchesOnlyActivePage()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = []; int opened = 0; string? copied = null;
        var fallback = fixture.Shell.Stage.Navigation.Current;
        using var presentation = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add, openLink: _ => opened++,
            copyText: text => { copied = text; return Task.CompletedTask; });
        var (session, peer) = ActivateCaptionPatch(new(), "", declarations:
            [new(SidecarRegistrationKind.Command, "test.scroll", 0, 0),
             PageDeclaration("test.nav", "#navigation-toggle", SidecarNxmlOperation.Before,
                 """<NavigationItem Key="ScrollNav" Label="Browse" Content="Browse" Command="ui.sidecar.page.open" Argument="#ScrollPage" />"""),
             PageDeclaration("test.page", "#ScrollPage", SidecarNxmlOperation.Page,
                 """<Page Key="ScrollPage"><StackPanel><Button Key="WebLink" Content="Website" Height="44" Command="ui.sidecar.link.open" Argument="https://example.com/help" /><Button Key="CopyCode" Content="Copy" Height="44" Command="ui.sidecar.clipboard.copy" Argument="ABCD-EFGH" /><StackPanel Key="Observed" Scroll="true" ScrollCommand="test.scroll" Height="200"><Text Content="rows" Height="1000" /></StackPanel></StackPanel></Page>""")],
            features: SidecarFeatures.All, nxml: adapter).GetAwaiter().GetResult();
        using (session) using (peer)
        {
            var loop = session.RunReceiveLoopAsync().AsTask();
            fixture.Shell.Renderer.ReducedMotion = true;
            var scene = fixture.Shell.Render(new(1000, 650));
            AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "ScrollNav").Entity));
            scene = fixture.Shell.Render(new(1000, 650));
            scene = fixture.Shell.Render(new(1000, 650));
            var request = peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            AssertEqual(SidecarMessageType.CommandRequest, request.MessageType);
            var call = SidecarDataMessages.DecodeBinaryRequest(request.Payload.Span);
            AssertEqual(1u, call.ContractId); AssertEqual("0|200|1000", Encoding.UTF8.GetString(call.Value.Span));
            peer.SendAsync(new(SidecarProtocol.Version, SidecarMessageType.CommandResult, SidecarFrameTraits.Final,
                request.CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, new(0, []), null))).AsTask().GetAwaiter().GetResult();
            AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "WebLink").Entity));
            AssertEqual(1, opened); AssertEqual(0, warnings.Count);
            AssertTrue(copied is null);
            var copyNode = FindByKey(fixture.Shell, scene, "CopyCode");
            AssertTrue(fixture.Shell.Renderer.Activate(copyNode.Entity));
            AssertEqual("ABCD-EFGH", copied!);
            copied = null;
            fixture.Shell.Stage.Navigation.Replace(fallback);
            fixture.Intents.Emit(XsrSemanticId.Parse("ui.sidecar.clipboard.copy"), copyNode.Entity, XsrCorrelationId.Create());
            AssertTrue(copied is null);
            session.Dispose(); loop.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
    }

    private static void SidecarExternalActivationRequiresOwnedOptIn()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = []; int imports = 0;
        using var presentation = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add, importTheme: (path, token) =>
        { AssertEqual(Path.GetFullPath("theme.zip"), path); token.ThrowIfCancellationRequested(); imports++; return Task.FromResult(XsrResult.Success()); });
        var (session, peer) = ActivateCaptionPatch(new(), "", declarations:
            [new(SidecarRegistrationKind.Command, "test.external", 0, 0),
             PageDeclaration("test.page", "#MarketPage", SidecarNxmlOperation.Page,
                 $$"""<Page Key="MarketPage"><Button Key="ExternalActivation" IsVisible="false" Command="test.external" /><Button Key="ApplyTheme" Content="Apply" Height="44" Command="ui.sidecar.theme.import" Argument="{{Path.GetFullPath("theme.zip")}}" /></Page>"""),
             PageDeclaration("test.closed", "#ClosedPage", SidecarNxmlOperation.Page,
                 """<Page Key="ClosedPage"><Text Content="No external activation" /></Page>""")],
            features: SidecarFeatures.All, nxml: adapter).GetAwaiter().GetResult();
        using (session) using (peer)
        {
            var loop = session.RunReceiveLoopAsync().AsTask();
            fixture.Shell.Renderer.ReducedMotion = true;
            adapter.EnqueueExternal("nexacl://extension/MarketPage?argument=theme-123");
            fixture.Shell.Render(new(1000, 650));
            var request = peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var call = SidecarDataMessages.DecodeBinaryRequest(request.Payload.Span);
            AssertEqual(1u, call.ContractId); AssertEqual("theme-123", Encoding.UTF8.GetString(call.Value.Span));
            AssertEqual("MarketPage", fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
            peer.SendAsync(new(SidecarProtocol.Version, SidecarMessageType.CommandResult, SidecarFrameTraits.Final,
                request.CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, new(0, []), null))).AsTask().GetAwaiter().GetResult();
            adapter.EnqueueExternal("nexacl://extension/ClosedPage?argument=theme-123");
            fixture.Shell.Render(new(1000, 650));
            AssertEqual("MarketPage", fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
            AssertEqual(1, warnings.Count);
            AssertEqual(0, imports);
            var scene = fixture.Shell.Render(new(1000, 650));
            AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "ApplyTheme").Entity));
            AssertEqual(1, imports);
            session.Dispose(); loop.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
    }

    private static SidecarRegistrationItem PageDeclaration(string id, string target, SidecarNxmlOperation operation, string markup, string revision = "1")
    {
        byte[] payload = SidecarNxmlDocument.Encode(new(target, operation, "main.nxml", new Dictionary<string, string> { ["main.nxml"] = markup }) { OnboardingRevision = revision });
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

    private static void FirstRunAndSidecarShareOneOnboardingSequence()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nexa-integrated-setup-" + Guid.NewGuid().ToString("N"));
        string initial = Path.Combine(directory, "initial"), selected = Path.Combine(directory, "selected");
        try
        {
            using (var fixture = new LaunchPageFixture(new ImmediateInstanceSource([])))
            {
                fixture.Shell.Renderer.ReducedMotion = true;
                var service = new Nexa.Services.Setup.FirstRunService(initial, Path.Combine(directory, "storage.json"));
                bool closed = false;
                using var host = new FirstRunController(fixture.Shell, fixture.Intents, fixture.Store,
                    Nexa.Services.Composition.FirstRunRuntimeComposer.Compose(service), service.Read(),
                    () => Task.FromResult<string?>(selected), () => closed = true);
                var discovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                host.ConfigureExtensions(discovery.Task);
                var adapter = new DesktopSidecarNxml(fixture.Store);
                List<string> warnings = [];
                using var presentation = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add, host.Page, firstRun: host);
                var (session, peer) = ActivateCaptionPatch(new(), "", declarations: Declarations(),
                    features: SidecarFeatures.All, nxml: adapter).GetAwaiter().GetResult();
                using (session) using (peer)
                {
                    var scene = fixture.Shell.Render(new(1000, 650));
                    AssertEqual(host.Page, fixture.Shell.Stage.Navigation.Current);
                    AssertEqual(0, host.Step);
                    // A receipt in the initial directory must not filter a page from a different chosen root.
                    var snapshot = fixture.Store.Read<XsrNxmlModuleSnapshot>(adapter.State).Value!;
                    var module = snapshot.Modules.First(m => m.Source.Target == "#ExtensionOne");
                    new DesktopOnboardingHistory(Path.Combine(initial, "onboarding")).Complete(DesktopOnboardingHistory.Identity(module.PackageName, module.Source.Target, "1"));
                    Host("next"); Host("browse"); fixture.Shell.Render(new(1000, 650));
                    Host("next"); AssertEqual(2, host.Step);
                    Host("next");
                    scene = fixture.Shell.Render(new(1000, 650));
                    AssertEqual(host.Page, fixture.Shell.Stage.Navigation.Current);
                    AssertTrue(host.ExtensionsRequested);
                    AssertTrue(scene.Nodes.Any(n => fixture.Shell.Tree.Name(n.Entity) == "SetupExtensionsPending"));
                    AssertFalse(scene.Nodes.Any(n => fixture.Shell.Tree.Name(n.Entity) == "SetupSummary"));
                    discovery.SetResult();
                    fixture.Shell.Render(new(1000, 650));
                    AssertEqual("ExtensionOne", fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
                    Continue();
                    AssertEqual("ExtensionTwo", fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
                    Continue();
                    AssertEqual(host.Page, fixture.Shell.Stage.Navigation.Current);
                    AssertEqual(3, host.Step);
                    AssertFalse(closed);
                    AssertTrue(host.OnboardingHistory.HasPendingBatch);
                    AssertFalse(Directory.Exists(selected));
                    Host("next");
                    AssertTrue(SpinWait.SpinUntil(() => { fixture.Shell.Render(new(1000, 650)); return host.Completed; }, TimeSpan.FromSeconds(5)));
                    AssertTrue(closed);
                    AssertEqual(2, Directory.GetFiles(Path.Combine(selected, "onboarding"), "*.done").Length);
                    AssertFalse(host.OnboardingHistory.HasPendingBatch);
                    AssertEqual(0, warnings.Count);
                }
                void Host(string command)
                { Emit(fixture.Intents, "ui.setup." + command); fixture.Shell.Render(new(1000, 650)); }
                void Continue()
                {
                    var scene = fixture.Shell.Render(new(1000, 650));
                    AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "ContinueExtension").Entity));
                    fixture.Shell.Render(new(1000, 650));
                }
            }
            // Bootstrap restarts the process: completed content must not become a second OOBE.
            using var restarted = new LaunchPageFixture(new ImmediateInstanceSource([]));
            var fallback = restarted.Shell.Stage.Navigation.Current;
            var next = new DesktopSidecarNxml(restarted.Store);
            using var edge = next.Attach(restarted.Shell, restarted.Intents, restarted.Store, _ => { }, fallback,
                onboarding: new DesktopOnboardingHistory(Path.Combine(selected, "onboarding")));
            var (second, connection) = ActivateCaptionPatch(new(), "", declarations: Declarations(), features: SidecarFeatures.All, nxml: next).GetAwaiter().GetResult();
            using (second) using (connection)
            {
                restarted.Shell.Render(new(1000, 650));
                AssertEqual(fallback, restarted.Shell.Stage.Navigation.Current);
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        static SidecarRegistrationItem[] Declarations() =>
        [
            PageDeclaration("test.first", "#ExtensionOne", SidecarNxmlOperation.OnboardingPage,
                "<nxml-page><Page Key=\"ExtensionOne\"><Button Key=\"ContinueExtension\" Content=\"Continue\" Height=\"44\" Command=\"ui.sidecar.onboarding.continue\" /></Page></nxml-page>"),
            PageDeclaration("test.second", "#ExtensionTwo", SidecarNxmlOperation.OnboardingPage,
                "<nxml-page><Page Key=\"ExtensionTwo\"><Button Key=\"ContinueExtension\" Content=\"Skip\" Height=\"44\" Command=\"ui.sidecar.onboarding.continue\" /></Page></nxml-page>")
        ];
    }

    private static void VersionedOnboardingFramesPendingPages()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nexa-onboarding-" + Guid.NewGuid().ToString("N"));
        try
        {
            Run("1", expected: true, interrupt: true);
            Run("1", expected: true);
            Run("1", expected: false);
            Run("2", expected: true);
            Run("2", expected: false);
            // A crash after the last middle page must not lose the completion screen.
            new DesktopOnboardingHistory(directory).BeginBatch();
            Run("2", expected: true, onlyOutro: true);
            Run("2", expected: false);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        void Run(string revision, bool expected, bool interrupt = false, bool onlyOutro = false)
        {
            using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
            fixture.Shell.Renderer.ReducedMotion = true;
            var fallback = fixture.Shell.Stage.Navigation.Current;
            var adapter = new DesktopSidecarNxml(fixture.Store);
            List<string> warnings = [];
            using var presentation = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add, fallback,
                onboarding: new DesktopOnboardingHistory(directory));
            var (session, peer) = ActivateCaptionPatch(new(), "", declarations:
                [PageDeclaration("test.onboarding", "#AccountSetup", SidecarNxmlOperation.OnboardingPage,
                    "<nxml-page><Page Key=\"AccountSetup\"><Button Key=\"ContinueSetup\" Content=\"Continue\" Height=\"44\" Command=\"ui.sidecar.onboarding.continue\" /></Page></nxml-page>", revision)],
                features: SidecarFeatures.All, nxml: adapter).GetAwaiter().GetResult();
            using (session) using (peer)
            {
                fixture.Shell.Render(new(1000, 650));
                if (!expected) { AssertEqual(fallback, fixture.Shell.Stage.Navigation.Current); return; }
                AssertEqual("VersionedOnboardingStart", fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
                var welcome = fixture.Shell.Stage.Navigation.Current;
                fixture.Shell.Render(new(1000, 650));
                AssertEqual(welcome, fixture.Shell.Stage.Navigation.Current);
                Click("VersionedOnboardingBegin");
                if (interrupt) return;
                if (!onlyOutro)
                {
                    AssertEqual("AccountSetup", fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
                    Click("ContinueSetup");
                }
                AssertEqual("VersionedOnboardingEnd", fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
                fixture.Shell.Render(new(1000, 650));
                AssertEqual("VersionedOnboardingEnd", fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
                Click("VersionedOnboardingFinish");
                fixture.Shell.Render(new(1000, 650));
                AssertEqual(fallback, fixture.Shell.Stage.Navigation.Current);
                AssertEqual(0, warnings.Count);
            }
            void Click(string key)
            {
                var scene = fixture.Shell.Render(new(1000, 650));
                AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, key).Entity));
                fixture.Shell.Render(new(1000, 650));
            }
        }
    }

    private static void FirstRunWithoutSidecarReachesSummary()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nexa-empty-setup-" + Guid.NewGuid().ToString("N"));
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var service = new Nexa.Services.Setup.FirstRunService(directory, Path.Combine(directory, "storage.json"));
        using var host = new FirstRunController(fixture.Shell, fixture.Intents, fixture.Store,
            Nexa.Services.Composition.FirstRunRuntimeComposer.Compose(service), service.Read(),
            () => Task.FromResult<string?>(null), () => { });
        host.ConfigureExtensions(Task.CompletedTask);
        var adapter = new DesktopSidecarNxml(fixture.Store);
        using var presentation = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, _ => { }, host.Page, firstRun: host);
        for (int step = 0; step < 3; step++)
        { Emit(fixture.Intents, "ui.setup.next"); fixture.Shell.Render(new(1000, 650)); }
        AssertEqual(3, host.Step);
        AssertEqual(host.Page, fixture.Shell.Stage.Navigation.Current);
        AssertFalse(host.ExtensionsRequested);
        AssertFalse(host.Completed);
        AssertFalse(Directory.Exists(directory));
    }

    private static void OnboardingReceiptsAreScopedAndDurable()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nexa-onboarding-" + Guid.NewGuid().ToString("N"));
        try
        {
            var history = new DesktopOnboardingHistory(directory);
            string key = DesktopOnboardingHistory.Identity("package", "#Page", "1");
            AssertFalse(history.IsComplete(key));
            history.Complete(key);
            var next = new DesktopOnboardingHistory(directory);
            AssertTrue(next.IsComplete(key));
            AssertFalse(next.IsComplete(DesktopOnboardingHistory.Identity("other", "#Page", "1")));
            AssertFalse(next.IsComplete(DesktopOnboardingHistory.Identity("package", "#Other", "1")));
            AssertFalse(next.IsComplete(DesktopOnboardingHistory.Identity("package", "#Page", "2")));
            string staged = Path.Combine(directory, "staged"), moved = Path.Combine(directory, "moved");
            var deferred = new DesktopOnboardingHistory(staged, deferred: true);
            deferred.BeginBatch(); deferred.Complete(key);
            AssertTrue(deferred.IsComplete(key));
            AssertFalse(Directory.Exists(staged));
            deferred.ChangeDeferredDirectory(moved);
            AssertTrue(deferred.IsComplete(key));
            File.WriteAllText(moved, "obstacle");
            bool failed = false;
            try { deferred.Flush(); } catch (IOException) { failed = true; }
            AssertTrue(failed);
            File.Delete(moved);
            deferred.FinishBatch(); deferred.Flush();
            AssertTrue(new DesktopOnboardingHistory(moved).IsComplete(key));
            AssertFalse(new DesktopOnboardingHistory(moved).HasPendingBatch);
            AssertFalse(Directory.Exists(staged));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
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
