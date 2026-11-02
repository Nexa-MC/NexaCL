using System.Globalization;
using Nexa.Desktop.Ui;
using Nexa.Services.Accounts;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.State;

namespace Nexa.Desktop;

internal static partial class Program
{
    private static void ValidateLaunchInstallHomeAndBedrock(XsrUiShell shell, XsrStateStore store,
        DesktopUiIntentSink intents, DesktopFeedbackService feedback, XsrUiEntityId homePage)
    {
        var previousPage = shell.Stage.Navigation.Current;
        var previousLocalizer = shell.Renderer.TextLocalizer;
        var previousScheme = shell.Renderer.ColorScheme;
        bool previousReducedMotion = shell.Renderer.ReducedMotion;
        var previousFocus = shell.Renderer.Focused;
        var pickerState = store.Resolve(LaunchPageState.AccountPickerKey);
        bool previousPicker = store.ReadAppliedValue(pickerState) is true;
        var language = new UiLocalizationCatalog();
        language.SetLanguage("zh-Hans");
        XsrUiEntityId pagerEntity = default;
        int previousPagerIndex = 0;
        double previousPagerPosition = 0;
        try
        {
            shell.Renderer.TextLocalizer = language.Translate;
            shell.Renderer.ReducedMotion = true;
            shell.Stage.Navigation.Replace(homePage);
            var size = new XsrUiSize(810, 500);
            var scene = shell.Render(size);
            var workspace = Find(homePage, "LaunchHomeWorkspace");
            var owner = shell.Tree.Parent(workspace);
            var rows = Find(homePage, "AccountRows");
            var rowScroll = shell.Tree.GetComponent<XsrUiScroll>(rows)
                ?? throw new InvalidOperationException("The controlled home roster has no declared scroll object.");
            Require(shell.Tree.GetComponent<NxmlDynamicSlot>(owner) is not null
                && shell.Tree.Name(owner) == "LaunchHomeWorkspaceSlot"
                && shell.Tree.GetComponent<NxmlDynamicSlot>(rows) is not null,
                "Launch home and account rows are not in owned external slots.");
            var profiles = store.ReadCollection<LaunchProfileView>(store.Resolve(AccountStateContract.ProfilesKey)).Items;
            Require(profiles.Count == 2, "The controlled home fixture must contain two account profiles.");
            var profile = profiles[0];
            string suffix = profile.Index.ToString(CultureInfo.InvariantCulture);
            var row = Find(homePage, "account-row:" + suffix);
            if (!scene.Nodes.Any(node => node.Entity == row && node.IsFocusable))
            {
                var accountSwitch = Find(homePage, "AccountSwitch");
                Require(shell.Renderer.Activate(accountSwitch), "The home account switch did not accept its renderer action.");
                scene = shell.Render(size);
            }
            Require(shell.Tree.Parent(row) == rows && shell.Renderer.Focus(row, showIndicator: false),
                "The controlled profile row could not receive focus in its owned roster.");
            var rowInput = shell.Tree.GetComponent<XsrUiInput>(row);
            var rowSelection = shell.Tree.GetComponent<XsrUiSelection>(row);
            var body = Find(homePage, "AccountBody");
            var bodyMotion = shell.Tree.GetComponent<XsrUiTransition>(body);
            Require(rowInput is not null && rowSelection is not null && bodyMotion is not null,
                "The account template did not establish input, selection and transition objects.");
            double rosterOffset = rowScroll!.OffsetY;
            Require(SceneNode(scene, homePage, "ProfileName:" + suffix).Text == profile.Username
                && SceneNode(scene, homePage, "ProfileDetail:" + suffix).Text == profile.Info,
                "Controlled profile name or provider metadata was reinterpreted.");
            language.SetLanguage("en");
            scene = shell.Render(size);
            Require(Find(homePage, "LaunchHomeWorkspace") == workspace && shell.Tree.Parent(workspace) == owner
                && Find(homePage, "account-row:" + suffix) == row && shell.Renderer.Focused == row
                && ReferenceEquals(rowInput, shell.Tree.GetComponent<XsrUiInput>(row))
                && ReferenceEquals(rowSelection, shell.Tree.GetComponent<XsrUiSelection>(row))
                && ReferenceEquals(rowScroll, shell.Tree.GetComponent<XsrUiScroll>(rows))
                && ReferenceEquals(bodyMotion, shell.Tree.GetComponent<XsrUiTransition>(body)),
                "Home locale projection replaced retained account ownership, focus, scroll or motion.");
            Close(rowScroll.OffsetY, rosterOffset, "Home locale projection changed the roster offset.");
            Require(SceneNode(scene, homePage, "ProfileName:" + suffix).Text == profile.Username
                && SceneNode(scene, homePage, "ProfileDetail:" + suffix).Text == profile.Info
                && !shell.Tree.GetComponent<XsrUiText>(Find(homePage, "ProfileName:" + suffix))!.Localize
                && !shell.Tree.GetComponent<XsrUiText>(Find(homePage, "ProfileDetail:" + suffix))!.Localize,
                "Home locale projection translated literal controlled account data.");

            pagerEntity = Find(homePage, "LaunchWidgetPager");
            var pager = shell.Tree.GetComponent<XsrUiPager>(pagerEntity)!;
            previousPagerIndex = pager.PageIndex;
            previousPagerPosition = pager.Position;
            var about = Find(homePage, "WidgetAboutDot");
            var trivia = Find(homePage, "WidgetTriviaDot");
            var echo = Find(homePage, "WidgetEchoDot");
            var aboutButton = Find(homePage, "WidgetAboutIndicator");
            var aboutInput = shell.Tree.GetComponent<XsrUiInput>(aboutButton);
            var triviaMessage = Find(homePage, "TriviaMessage");
            var triviaMotion = shell.Tree.GetComponent<XsrUiTransition>(triviaMessage);
            var launchProgress = Find(homePage, "LaunchButtonProgress");
            var progress = shell.Tree.GetComponent<XsrUiProgress>(launchProgress);
            Require(shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(aboutButton)) is not null
                && aboutInput is not null && triviaMotion is not null && progress is not null,
                "Widget and launch progress primitives were not established by external templates.");
            shell.Renderer.ReducedMotion = false;
            shell.Render(size);
            Require(shell.Renderer.MovePager(pagerEntity, 1), "The controlled widget pager could not select its trivia page.");
            shell.Renderer.SetPagerPresentationPosition(pagerEntity, .25);
            scene = shell.Render(size);
            Close(SceneNode(scene, homePage, "WidgetAboutDot").Rect.Height, 13.5, "Fractional about dot height changed.");
            Close(SceneNode(scene, homePage, "WidgetTriviaDot").Rect.Height, 8.5, "Fractional trivia dot height changed.");
            Close(SceneNode(scene, homePage, "WidgetEchoDot").Rect.Height, 6, "Inactive echo dot height changed.");
            Close(SceneNode(scene, homePage, "WidgetAboutIndicator").Rect.Height, 13.5, "Fractional widget hit geometry changed.");
            Require(shell.Tree.GetComponent<XsrUiVisualStyle>(about)!.Background.Alpha == 207
                && shell.Tree.GetComponent<XsrUiVisualStyle>(trivia)!.Background.Alpha == 112
                && shell.Tree.GetComponent<XsrUiVisualStyle>(echo)!.Background.Alpha == 64,
                "Fractional widget alpha projection changed.");
            shell.Renderer.SetPagerPresentationPosition(pagerEntity, 1.75);
            scene = shell.Render(size);
            Close(SceneNode(scene, homePage, "WidgetAboutDot").Rect.Height, 6, "Inactive about dot height changed.");
            Close(SceneNode(scene, homePage, "WidgetTriviaDot").Rect.Height, 8.5, "Fractional trailing trivia dot height changed.");
            Close(SceneNode(scene, homePage, "WidgetEchoDot").Rect.Height, 13.5, "Fractional echo dot height changed.");
            Close(SceneNode(scene, homePage, "WidgetIndicators").Rect.Height, 44, "Widget indicator extent changed.");
            Require(Find(homePage, "WidgetAboutDot") == about && Find(homePage, "WidgetTriviaDot") == trivia
                && Find(homePage, "WidgetEchoDot") == echo
                && ReferenceEquals(pager, shell.Tree.GetComponent<XsrUiPager>(pagerEntity))
                && ReferenceEquals(aboutInput, shell.Tree.GetComponent<XsrUiInput>(aboutButton))
                && ReferenceEquals(triviaMotion, shell.Tree.GetComponent<XsrUiTransition>(triviaMessage))
                && ReferenceEquals(progress, shell.Tree.GetComponent<XsrUiProgress>(launchProgress)),
                "Fractional widget reload replaced retained entities, pager, input, progress or transition objects.");
            XsrUiColor lightCard = default;
            foreach (bool dark in new[] { false, true })
            {
                shell.Renderer.ColorScheme = new(dark);
                scene = shell.Render(size);
                foreach (string key in new[] { "CardAccount", "CardVersion", "CardAbout" })
                {
                    var card = SceneNode(scene, homePage, key);
                    Require(card.Rect.Width > 0 && card.Rect.Height > 0 && card.Rect.X >= 0 && card.Rect.Y >= 0
                        && card.Rect.X + card.Rect.Width <= size.Width + .01 && card.Rect.Y + card.Rect.Height <= size.Height + .01,
                        "A home card did not fit the controlled 810 by 500 viewport in both themes.");
                }
                var cardColor = SceneNode(scene, homePage, "CardVersion").VisualStyle.Background;
                if (dark) Require(cardColor != lightCard, "The controlled dark home scene did not project its theme surface.");
                else lightCard = cardColor;
            }
            Console.WriteLine("Dynamic launch home validation passed: owned account roster, literal controlled fixtures, retained focus/scroll/progress/transitions, fractional widget dots, and light/dark 810x500 scenes; no game launch or account authentication.");

            int stores = 0, browsers = 0;
            foreach (bool? supportsStore in new bool?[] { true, false, null })
            {
                bool expectedStore = supportsStore ?? OperatingSystem.IsWindows();
                using var bedrock = new BedrockInstallPageController(shell, intents, store, feedback, () => stores++, uri =>
                {
                    Require(uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps
                        && uri.AbsoluteUri == "https://www.xbox.com/en-US/games/store/minecraft-for-windows/9NBLGGH2JHXJ",
                        "The Bedrock fixture received an unexpected product URI.");
                    browsers++;
                }, supportsStore);
                try
                {
                    shell.Stage.Navigation.Replace(bedrock.Page);
                    shell.Renderer.ReducedMotion = true;
                    language.SetLanguage("zh-Hans");
                    scene = shell.Render(size);
                    var bedrockWorkspace = Find(bedrock.Page, "BedrockInstallHost");
                    var bedrockOwner = shell.Tree.Parent(bedrockWorkspace);
                    var status = Find(bedrock.Page, "BedrockInstallStatus");
                    var native = Find(bedrock.Page, "BedrockInstallStore");
                    var website = Find(bedrock.Page, "BedrockInstallWebsite");
                    var nativeInput = shell.Tree.GetComponent<XsrUiInput>(native)!;
                    var websiteInput = shell.Tree.GetComponent<XsrUiInput>(website)!;
                    var statusText = shell.Tree.GetComponent<XsrUiText>(status)!;
                    var statusSemantic = shell.Tree.GetComponent<XsrUiSemantic>(status)!;
                    Require(shell.Tree.GetComponent<NxmlDynamicSlot>(bedrockOwner) is not null
                        && shell.Tree.Name(bedrockOwner) == "BedrockInstallWorkspaceSlot"
                        && nativeInput.Enabled == expectedStore && nativeInput.Clickable == expectedStore
                        && nativeInput.Focusable == expectedStore
                        && SceneNode(scene, bedrock.Page, "BedrockInstallStore").IsClickable == expectedStore,
                        "The Bedrock owned workspace or Windows platform gate changed.");
                    var description = SceneNode(scene, bedrock.Page, "BedrockInstallDescription");
                    Require(description.Text?.Contains("Microsoft Store", StringComparison.Ordinal) == true && description.Rect.Height > 0,
                        "The Bedrock fixture description lost its declarative viewport extent.");
                    int storesBefore = stores;
                    if (expectedStore)
                    {
                        Require(shell.Renderer.Focus(native, showIndicator: false) && shell.Renderer.Activate(native),
                            "The enabled Bedrock store action did not accept its renderer invocation.");
                        shell.Render(size);
                        Require(stores == storesBefore + 1, "The Bedrock fixture native effect was not invoked exactly once.");
                    }
                    else
                    {
                        Require(!shell.Renderer.Focus(native) && !shell.Renderer.Activate(native) && stores == storesBefore,
                            "An unsupported Bedrock store action bypassed its platform gate.");
                    }
                    int browsersBefore = browsers;
                    Require(shell.Renderer.Focus(website, showIndicator: false) && shell.Renderer.Activate(website),
                        "The Bedrock official website action did not accept its renderer invocation.");
                    scene = shell.Render(size);
                    Require(browsers == browsersBefore + 1 && Find(bedrock.Page, "BedrockInstallStatus") == status
                        && ReferenceEquals(statusText, shell.Tree.GetComponent<XsrUiText>(status))
                        && ReferenceEquals(statusSemantic, shell.Tree.GetComponent<XsrUiSemantic>(status))
                        && ReferenceEquals(nativeInput, shell.Tree.GetComponent<XsrUiInput>(native))
                        && ReferenceEquals(websiteInput, shell.Tree.GetComponent<XsrUiInput>(website)),
                        "The Bedrock handoff did not retain its status and command controls.");
                    var notifications = feedback.Snapshot().Notifications;
                    string feedbackCopy = notifications[notifications.Count - 1].Message;
                    Require(SceneNode(scene, bedrock.Page, "BedrockInstallStatus").Text == feedbackCopy,
                        "The controlled Bedrock handoff status did not reconcile its external copy.");
                    language.SetLanguage("en");
                    scene = shell.Render(size);
                    Require(Find(bedrock.Page, "BedrockInstallHost") == bedrockWorkspace
                        && shell.Tree.Parent(bedrockWorkspace) == bedrockOwner && shell.Renderer.Focused == website
                        && SceneNode(scene, bedrock.Page, "BedrockInstallStatus").Text == language.Translate(feedbackCopy)
                        && SceneNode(scene, bedrock.Page, "BedrockInstallStatus").Label == language.Translate(feedbackCopy)
                        && !statusText.Localize && !statusSemantic.Localize,
                        "Bedrock locale reload replaced focus/ownership or translated its status twice.");
                    foreach (bool dark in new[] { false, true })
                    {
                        shell.Renderer.ColorScheme = new(dark);
                        scene = shell.Render(size);
                        var action = SceneNode(scene, bedrock.Page, "BedrockInstallWebsite");
                        Require(action.IsClickable && action.Rect.Width > 0 && action.Rect.Height > 0
                            && action.Rect.X >= 0 && action.Rect.X + action.Rect.Width <= size.Width + .01,
                            "The Bedrock handoff action did not fit the constrained scene in both themes.");
                    }
                }
                finally { shell.Stage.Navigation.Replace(homePage); }
            }
            Require(stores == (OperatingSystem.IsWindows() ? 2 : 1) && browsers == 3,
                "Controlled Bedrock native/browser effect counts changed.");
            Console.WriteLine("Dynamic Bedrock validation passed: actual owned controllers, Windows gating, retained status/control/focus across locale and light/dark 810x500 scenes, with fixture native/browser effects only; no store/browser launch or installation.");
        }
        finally
        {
            store.Publish(pickerState, previousPicker);
            if (pagerEntity.IsAssigned && shell.Tree.IsAlive(pagerEntity))
            {
                shell.Renderer.RebasePagerPage(pagerEntity, previousPagerIndex);
                shell.Renderer.SetPagerPresentationPosition(pagerEntity, previousPagerPosition);
            }
            shell.Renderer.TextLocalizer = previousLocalizer;
            shell.Renderer.ColorScheme = previousScheme;
            shell.Renderer.ReducedMotion = previousReducedMotion;
            if (shell.Tree.IsAlive(previousPage)) shell.Stage.Navigation.Replace(previousPage);
            if (previousFocus.IsAssigned && shell.Tree.IsAlive(previousFocus)) shell.Renderer.Focus(previousFocus, showIndicator: false);
        }

        XsrUiEntityId Find(XsrUiEntityId root, string key)
        {
            XsrUiEntityId found = default;
            shell.Tree.Walk(root, entity => { if (shell.Tree.Name(entity) == key) found = entity; return true; });
            if (!found.IsAssigned) throw new InvalidOperationException("Missing launch-home/Bedrock validation node: " + key);
            return found;
        }
        XsrUiSceneNode SceneNode(XsrUiScene scene, XsrUiEntityId root, string key)
        {
            var entity = Find(root, key);
            var nodes = scene.Nodes.Where(node => node.Entity == entity).ToArray();
            if (nodes.Length != 1) throw new InvalidOperationException("Missing or ambiguous launch-home/Bedrock scene node: " + key);
            return nodes[0];
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static void Close(double actual, double expected, string message) => Require(Math.Abs(actual - expected) <= .001, message);
    }
}
