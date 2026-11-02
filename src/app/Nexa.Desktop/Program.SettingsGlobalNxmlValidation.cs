using System.Globalization;
using Nexa.Desktop.Ui;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop;

internal static partial class Program
{
    private static async Task ValidateDynamicSettingsGlobalAsync(XsrUiShell shell, XsrStateStore store,
        DesktopUiIntentSink intents)
    {
        var previousPage = shell.Stage.Navigation.Current;
        var previousLocalizer = shell.Renderer.TextLocalizer;
        var previousScheme = shell.Renderer.ColorScheme;
        bool previousReducedMotion = shell.Renderer.ReducedMotion;
        var previousFocus = shell.Renderer.Focused;
        long revision = store.Read<long>(store.Resolve(SettingsPolicyContract.RevisionKey)).Value;
        const string originalHook = "echo fixture\r\n\tprintf 'literal <hook> {state private}'\n";
        var values = SettingsPolicySchema.Definitions.Select(definition => new SettingsEffectiveValue(definition.Key,
            new(SettingsOverrideMode.Custom, definition.Key switch
            {
                "developer.enabled" => "true",
                "game.arguments" => "--demo",
                "game.pre-launch" => originalHook,
                _ => definition.DefaultValue,
            }), SettingsLayer.Builtin, definition.Timing, null)).ToArray();
        var effective = new SettingsEffectiveSnapshot(revision, values);
        int settingsWrites = 0;
        var queries = new XsrQueryRouterBuilder();
        queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
            (query, _) => ValueTask.FromResult(XsrResult.Success(SettingsCatalog.Read(query))));
        queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
            (_, _) => ValueTask.FromResult(XsrResult.Success(effective)));
        var commands = new XsrCommandRouterBuilder();
        commands.Register<SettingsMutation>(SettingsPolicyContract.SetCommand, (_, _) =>
        {
            settingsWrites++;
            return ValueTask.FromResult(XsrResult.Success());
        });
        commands.Register<SettingsBatchCommand>(SettingsPolicyContract.BatchCommand, (_, _) =>
        {
            settingsWrites++;
            return ValueTask.FromResult(XsrResult.Success());
        });
        var observation = new SettingsGlobalValidationDispatchObserver();
        using var feedback = new DesktopFeedbackService();
        var controller = new SettingsPageController(shell, intents, queries.Build(observation), commands.Build(observation), store, feedback);
        var language = new UiLocalizationCatalog();
        var size = new XsrUiSize(810, 500);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            language.SetLanguage("zh-Hans");
            shell.Renderer.TextLocalizer = language.Translate;
            shell.Renderer.ReducedMotion = true;
            controller.SetStartupUpdateOffline();
            await controller.PrepareStartupAsync(timeout.Token).ConfigureAwait(false);
            shell.Stage.Navigation.Replace(controller.Page);
            shell.Render(size);
            await Select("game");
            var argument = Find("SettingsArgument.game.arguments.0");
            const string argumentDraft = "--demo literal <argument> {state private}";
            shell.Renderer.SetTextInputValue(argument, argumentDraft);
            Activate("SettingsArgumentAdd.game.arguments");
            argument = Find("SettingsArgument.game.arguments.0");
            Require(Find("SettingsArgument.game.arguments.1").IsAssigned, "The actual argument editor did not add an owned row.");
            var argumentInput = Input(argument);
            var argumentRow = shell.Tree.Parent(argument);
            var argumentSlot = shell.Tree.Parent(argumentRow);
            Require(shell.Tree.GetComponent<NxmlDynamicSlot>(argumentSlot) is not null
                && argumentInput.ReadDraft() == argumentDraft,
                "Argument collection projection lost its owned row or unsaved literal draft.");
            var hook = Find("SettingsHookLine.game.pre-launch.1");
            var hookInput = Input(hook);
            Require(hookInput.PreserveTabs && hookInput.ReadDraft() == "\tprintf 'literal <hook> {state private}'",
                "The controlled hook editor did not retain authored tab indentation.");
            const string hookDraft = "\tprintf 'edited <hook> {data command}'";
            shell.Renderer.SetTextInputValue(hook, hookDraft);
            Activate("SettingsHookAdd.game.pre-launch");
            Require(Find("SettingsHookLine.game.pre-launch.1") == hook && ReferenceEquals(hookInput, Input(hook)),
                "Adding a hook line replaced an existing owned hook input.");
            var hookRow = shell.Tree.Parent(hook);
            var hookSlot = shell.Tree.Parent(hookRow);
            Require(shell.Tree.GetComponent<NxmlDynamicSlot>(hookSlot) is not null,
                "Hook lines were not projected through their declared collection slot.");
            EnsureVisible(argument);
            Require(shell.Renderer.Focus(argument, showIndicator: false) && shell.Renderer.EditText(XsrUiTextEdit.Left),
                "The actual argument input could not establish a caret.");
            int argumentCaret = argumentInput.SelectionEnd;
            int hookCaret = hookInput.SelectionEnd;
            var gameSections = ActiveSections();
            var gameScroll = shell.Tree.GetComponent<XsrUiScroll>(gameSections)!;
            var gameMotion = shell.Tree.GetComponent<XsrUiTransition>(gameSections)!;
            double gameOffset = gameScroll.OffsetY;
            language.SetLanguage("en");
            shell.Render(size);
            Require(Find("SettingsArgument.game.arguments.0") == argument && shell.Renderer.Focused == argument
                && ReferenceEquals(argumentInput, Input(argument)) && argumentInput.ReadDraft() == argumentDraft
                && argumentInput.SelectionEnd == argumentCaret && Find("SettingsHookLine.game.pre-launch.1") == hook
                && ReferenceEquals(hookInput, Input(hook)) && hookInput.ReadDraft() == hookDraft && hookInput.SelectionEnd == hookCaret
                && ReferenceEquals(gameScroll, shell.Tree.GetComponent<XsrUiScroll>(gameSections))
                && ReferenceEquals(gameMotion, shell.Tree.GetComponent<XsrUiTransition>(gameSections)),
                "Global catalogue locale projection replaced draft, caret, focus, scroll or section motion.");
            Close(gameScroll.OffsetY, gameOffset, "The locale change reset the catalogue scroll position.");
            EnsureVisible(hook);
            Require(shell.Renderer.Focus(hook, showIndicator: false) && shell.Renderer.EditText(XsrUiTextEdit.Left),
                "The hook draft could not establish a retained caret.");
            hookCaret = hookInput.SelectionEnd;
            foreach (bool dark in new[] { false, true })
            {
                shell.Renderer.ColorScheme = new(dark);
                var scene = shell.Render(size);
                Require(shell.Renderer.Focused == hook && hookInput.ReadDraft() == hookDraft && hookInput.SelectionEnd == hookCaret
                    && argumentInput.ReadDraft() == argumentDraft && argumentInput.SelectionEnd == argumentCaret,
                    "A constrained catalogue theme scene lost the lossless command or argument draft.");
                FitsWidth(scene, hook, "The hook editor did not fit the 810 by 500 theme scene.");
            }
            Require(!controller.SettingsWritePending && settingsWrites == 0,
                "Catalogue draft interactions submitted a controlled settings write.");

            await Select("network");
            var proxy = Find("SettingsProxy");
            var proxyOwner = shell.Tree.Parent(proxy);
            Require(shell.Tree.GetComponent<NxmlDynamicSlot>(proxyOwner) is not null,
                "The custom proxy workspace is not an owned fragment.");
            Activate("SettingsProxyMode.2");
            var address = Find("SettingsInput.network.proxy-address");
            var password = Find("SettingsInput.network.proxy-password");
            var username = Find("SettingsInput.network.proxy-user");
            var addressInput = Input(address);
            var passwordInput = Input(password);
            Require(passwordInput.IsPassword && !passwordInput.PreserveTabs,
                "The external proxy password input did not establish its protected sanitization contract.");
            shell.Renderer.SetTextInputValue(address, "http://fixture.invalid:8181");
            shell.Renderer.SetTextInputValue(username, "fixture-user\t\r\n");
            shell.Renderer.SetTextInputValue(password, "controlled-fixture-secret\t\r\n\0");
            Require(Input(username).ReadDraft() == "fixture-user" && passwordInput.ReadDraft() == "controlled-fixture-secret",
                "Proxy credentials did not remove control characters from their ephemeral drafts.");
            EnsureVisible(password);
            var passwordScene = shell.Render(size);
            Require(passwordScene.Nodes.All(node => node.Text?.Contains("controlled-fixture-secret", StringComparison.Ordinal) != true
                && node.Label?.Contains("controlled-fixture-secret", StringComparison.Ordinal) != true
                && node.TextInput?.DisplayText.Contains("controlled-fixture-secret", StringComparison.Ordinal) != true),
                "A controlled password draft escaped into scene text or accessibility.");
            var passwordSnapshot = passwordScene.Nodes.Single(node => node.Entity == password).TextInput;
            Require(passwordSnapshot is { IsPassword: true } masked
                && masked.DisplayText == new string('•', passwordInput.ReadDraft().Length),
                "The proxy scene did not expose a masked password snapshot.");
            Activate("SettingsProxyMode.0");
            Require(!shell.Renderer.Focus(password) && Find("SettingsProxy") == proxy && ReferenceEquals(passwordInput, Input(password)),
                "Disabling manual proxy replaced its input or left the hidden password focusable.");
            Activate("SettingsProxyMode.1");
            Activate("SettingsProxyMode.2");
            Activate("SettingsProxyProtocol.socks4");
            Require(!shell.Renderer.Focus(password) && shell.Tree.GetComponent<XsrUiElement>(Find("SettingsProxyRow.network.proxy-password"))?.IsVisible == false,
                "SOCKS4 exposed the unsupported password field.");
            Activate("SettingsProxyProtocol.http");
            EnsureVisible(address);
            Require(shell.Renderer.Focus(address, showIndicator: false) && shell.Renderer.EditText(XsrUiTextEdit.Left),
                "The proxy address draft could not establish its caret.");
            int addressCaret = addressInput.SelectionEnd;
            string addressDraft = addressInput.ReadDraft();
            language.SetLanguage("zh-Hant");
            foreach (bool dark in new[] { false, true })
            {
                shell.Renderer.ColorScheme = new(dark);
                var scene = shell.Render(size);
                Require(Find("SettingsProxy") == proxy && shell.Tree.Parent(proxy) == proxyOwner
                    && Find("SettingsInput.network.proxy-address") == address && shell.Renderer.Focused == address
                    && ReferenceEquals(addressInput, Input(address)) && addressInput.ReadDraft() == addressDraft
                    && addressInput.SelectionEnd == addressCaret && ReferenceEquals(passwordInput, Input(password))
                    && passwordInput.ReadDraft() == "controlled-fixture-secret",
                    "Proxy mode/locale/theme reload replaced owned inputs, focus, caret or protected drafts.");
                FitsWidth(scene, address, "The manual proxy editor did not fit the constrained theme scene.");
            }
            Require(!controller.SettingsWritePending && settingsWrites == 0
                && effective.Values.Single(value => value.Key == "network.proxy-password").Value.Value == "",
                "Proxy fixture interactions submitted or committed an ephemeral secret.");
            shell.Renderer.SetTextInputValue(password, string.Empty);
            Require(passwordInput.ReadDraft().Length == 0, "The controlled secret draft was not cleared before leaving the proxy workspace.");

            await Select("advanced");
            var diagnostics = Find("AdvancedWorkspace.Diagnostics");
            var diagnosticsOwner = shell.Tree.Parent(diagnostics);
            var capture = Find("AdvancedWorkspace.Capture");
            var captureInput = shell.Tree.GetComponent<XsrUiInput>(capture)!;
            var nodes = Find("AdvancedWorkspace.Fact.nodes");
            var nodesText = shell.Tree.GetComponent<XsrUiText>(nodes)!;
            Require(shell.Tree.GetComponent<NxmlDynamicSlot>(diagnosticsOwner) is not null,
                "Captured advanced diagnostics did not enter its owned workspace slot.");
            EnsureVisible(capture);
            Require(shell.Renderer.Focus(capture, showIndicator: false), "The actual diagnostic capture button could not receive focus.");
            Activate("AdvancedWorkspace.Capture");
            Require(int.TryParse(nodesText.Content, NumberStyles.None, CultureInfo.InvariantCulture, out int nodeCount) && nodeCount > 0,
                "The actual read-only runtime capture did not project renderer scene facts.");
            language.SetLanguage("en");
            foreach (bool dark in new[] { false, true })
            {
                shell.Renderer.ColorScheme = new(dark);
                var scene = shell.Render(size);
                Require(Find("AdvancedWorkspace.Diagnostics") == diagnostics && shell.Tree.Parent(diagnostics) == diagnosticsOwner
                    && Find("AdvancedWorkspace.Capture") == capture && shell.Renderer.Focused == capture
                    && ReferenceEquals(captureInput, shell.Tree.GetComponent<XsrUiInput>(capture))
                    && Find("AdvancedWorkspace.Fact.nodes") == nodes && ReferenceEquals(nodesText, shell.Tree.GetComponent<XsrUiText>(nodes)),
                    "Advanced capture locale/theme projection replaced owned controls or captured facts.");
                FitsWidth(scene, capture, "The captured diagnostic action did not fit the constrained theme scene.");
            }
            Require(settingsWrites == 0 && !controller.SettingsWritePending,
                "Global settings validation unexpectedly submitted a fixture write.");
            Console.WriteLine("Dynamic settings global validation passed: actual SettingsPageController with controlled typed catalogue/effective fixtures, owned argument/hooks, lossless drafts/caret/focus/scroll/motion, protected unsaved proxy modes, read-only captured diagnostics, and light/dark 810x500 scenes; no production settings, secrets, network or filesystem writes.");
        }
        finally
        {
            controller.Dispose();
            shell.Renderer.TextLocalizer = previousLocalizer;
            shell.Renderer.ColorScheme = previousScheme;
            shell.Renderer.ReducedMotion = previousReducedMotion;
            if (shell.Tree.IsAlive(previousPage)) shell.Stage.Navigation.Replace(previousPage);
            if (shell.Tree.IsAlive(controller.Page)) shell.Tree.Destroy(controller.Page);
            if (previousFocus.IsAssigned && shell.Tree.IsAlive(previousFocus)) shell.Renderer.Focus(previousFocus, showIndicator: false);
        }

        async Task Select(string id)
        {
            Activate("SettingsNav." + id);
            Require(controller.SelectedSection == id, "The actual settings section action did not navigate to " + id + ".");
            await controller.PrepareVisibleStartupAsync(timeout.Token).ConfigureAwait(false);
            shell.Render(size);
        }
        XsrUiEntityId Find(string key)
        {
            XsrUiEntityId result = default;
            shell.Tree.Walk(controller.Page, entity => { if (shell.Tree.Name(entity) == key) result = entity; return true; });
            if (!result.IsAssigned) throw new InvalidOperationException("Missing controlled global settings node: " + key);
            return result;
        }
        XsrUiEntityId ActiveSections()
        {
            var pager = Find("SettingsPager");
            return shell.Tree.Children(pager)[shell.Tree.GetComponent<XsrUiPager>(pager)!.PageIndex];
        }
        XsrUiTextInput Input(XsrUiEntityId entity) => shell.Tree.GetComponent<XsrUiTextInput>(entity)
            ?? throw new InvalidOperationException("The controlled settings entity is not a declarative input.");
        void Activate(string key)
        {
            var entity = Find(key);
            EnsureVisible(entity);
            Require(shell.Renderer.Activate(entity), "The actual global settings control did not accept its renderer action: " + key);
            shell.Render(size);
        }
        void EnsureVisible(XsrUiEntityId entity)
        {
            // Search the controlled form from its beginning. Projection may cull a target
            // above the current viewport after a different command editor was exercised.
            var section = ActiveSections();
            for (var ancestor = entity; ancestor.IsAssigned; ancestor = shell.Tree.Parent(ancestor))
            {
                if (ancestor != section) continue;
                shell.Tree.GetComponent<XsrUiScroll>(section)!.OffsetY = 0;
                shell.Tree.MarkDirty(section, XsrUiDirtyKinds.Layout);
                break;
            }
            for (int pass = 0; pass < 128; pass++)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var scene = shell.Render(size);
                var target = scene.Nodes.Where(node => node.Entity == entity).Select(node => (XsrUiSceneNode?)node).FirstOrDefault();
                if (target is { } shown && IsVisible(shown)) return;
                XsrUiEntityId scrollEntity = default;
                for (var ancestor = shell.Tree.Parent(entity); ancestor.IsAssigned; ancestor = shell.Tree.Parent(ancestor))
                    if (shell.Tree.GetComponent<XsrUiScroll>(ancestor) is not null) { scrollEntity = ancestor; break; }
                Require(scrollEntity.IsAssigned, "The controlled settings target has no scroll owner.");
                var scrollNode = scene.Nodes.Single(node => node.Entity == scrollEntity);
                bool horizontal = shell.Tree.GetComponent<XsrUiStackPanel>(scrollEntity)?.Direction == XsrUiOrientation.Horizontal;
                double delta = target is { } hidden && (horizontal ? hidden.Rect.X < scrollNode.Rect.X : hidden.Rect.Y < scrollNode.Rect.Y) ? -120 : 120;
                Require(shell.Renderer.PointerScroll(new(scrollNode.Rect.X + Math.Min(20, scrollNode.Rect.Width / 2),
                    scrollNode.Rect.Y + Math.Min(20, scrollNode.Rect.Height / 2)), horizontal ? 0 : delta, horizontal ? delta : 0),
                    "The actual settings scroll owner did not accept a viewport gesture.");
            }
            throw new InvalidOperationException("The controlled settings target did not enter the viewport: " + shell.Tree.Name(entity));
        }
        static bool IsVisible(XsrUiSceneNode node) => node.Rect.Width > 0 && node.Rect.Height > 0
            && (node.ClipRect is null || node.ClipRect.Value.Contains(new(node.Rect.X + Math.Min(12, node.Rect.Width / 2),
                node.Rect.Y + Math.Min(12, node.Rect.Height / 2))));
        static void FitsWidth(XsrUiScene scene, XsrUiEntityId entity, string message)
        {
            var node = scene.Nodes.Single(item => item.Entity == entity);
            Require(IsVisible(node) && node.Rect.X >= 0 && node.Rect.X + node.Rect.Width <= 810.01, message);
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static void Close(double actual, double expected, string message) => Require(Math.Abs(actual - expected) < .001, message);
    }

    private sealed class SettingsGlobalValidationDispatchObserver : IXsrDispatchObserver
    {
        public void OnCompleted(XsrDispatchObservation observation) { }
    }
}
