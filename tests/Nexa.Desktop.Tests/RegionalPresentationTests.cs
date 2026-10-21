using Nexa.Desktop.Ui;
using Nexa.Services;
using Nexa.Services.Resources;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void MainlandBaseInstallControlsExplainAndRejectUnavailableActions()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), regionalPolicy: new("CN"));
        Emit(fixture.Intents, "ui.install.java");
        var scene = fixture.Shell.Render(new(1280, 800));
        var start = FindByKey(fixture.Shell, scene, "JavaInstallStart");
        AssertFalse(start.IsEnabled);
        AssertEqual("不提供本体下载", start.Text);
        AssertEqual(RegionalPolicy.MinecraftDownloadUnavailable, start.Label);
        Emit(fixture.Intents, "ui.install.start");
        fixture.Shell.Render(new(1280, 800));
        AssertTrue(fixture.Feedback.Snapshot().Notifications.Any(notice => notice.Message == RegionalPolicy.MinecraftDownloadUnavailable));
        AssertEqual(0, fixture.Foundation.Host.Tasks.ReadEntries().Count);
        var catalog = new UiLocalizationCatalog();
        fixture.Shell.Renderer.TextLocalizer = catalog.Translate;
        foreach (var (language, caption) in new[] { ("en", "Game download unavailable"), ("zh-Hant", "不提供本體下載") })
        {
            catalog.SetLanguage(language);
            scene = fixture.Shell.Render(new(1280, 800));
            start = FindByKey(fixture.Shell, scene, "JavaInstallStart");
            AssertFalse(start.IsEnabled);
            AssertEqual(caption, start.Text);
            AssertEqual(catalog.Translate(RegionalPolicy.MinecraftDownloadUnavailable), start.Label);
            Emit(fixture.Intents, "ui.install.start");
            fixture.Shell.Render(new(1280, 800));
            AssertTrue(fixture.Feedback.Snapshot().Notifications.Any(notice => notice.Message == start.Label));
            AssertEqual(0, fixture.Foundation.Host.Tasks.ReadEntries().Count);
        }
    }

    private static void RegionalSettingsHideMainlandFacilitiesAndRestoreSavedPreferences()
    {
        foreach (string country in new[] { "US", "HK", "MO", "TW", "" })
        {
            using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
            Apply("network.game-source", "mirrors-first");
            Apply("network.resource-source", "mirrors-first");
            Apply("network.provider-mirror", "false");
            // Interface language and formatting culture cannot enable mainland facilities.
            Apply("general.language", "zh-Hans");
            Apply("general.region", "zh-CN");
            using (var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
                fixture.Foundation.Commands, fixture.Store, fixture.Feedback, regionalPolicy: new(country)))
            {
                fixture.Shell.Renderer.ReducedMotion = true;
                fixture.Shell.Stage.Navigation.Replace(settings.Page);
                fixture.Shell.Render(new(1000, 800));
                Navigate("network");
                AssertFalse(RegionalFind(fixture.Shell, settings.Page, "SettingsSelector.network.game-source").IsAssigned);
                AssertFalse(RegionalFind(fixture.Shell, settings.Page, "SettingsSelector.network.resource-source").IsAssigned);
                AssertFalse(RegionalFind(fixture.Shell, settings.Page, "SettingsSelector.network.provider-mirror").IsAssigned);
                AssertFalse(RegionalVisible(fixture.Shell, RegionalFind(fixture.Shell, settings.Page, "NetworkMirrorResolution")));
                AssertTrue(RegionalFind(fixture.Shell, settings.Page, "NetworkProbe.Action").IsAssigned);
                Navigate("about");
                AssertFalse(RegionalVisible(fixture.Shell, RegionalFind(fixture.Shell, settings.Page, "AboutMainland")));
                AssertTrue(RegionalFind(fixture.Shell, settings.Page, "AboutThanks").IsAssigned);

                void Navigate(string section)
                {
                    Emit(fixture.Intents, "ui.settings.section", RegionalFind(fixture.Shell, settings.Page, "SettingsNav." + section));
                    fixture.Shell.Render(new(1000, 800));
                    AssertEqual(section, settings.SelectedSection);
                }
            }
            using (var mainland = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
                fixture.Foundation.Commands, fixture.Store, fixture.Feedback, regionalPolicy: new("CN")))
            {
                fixture.Shell.Stage.Navigation.Replace(mainland.Page);
                fixture.Shell.Render(new(1000, 800));
                Emit(fixture.Intents, "ui.settings.section", RegionalFind(fixture.Shell, mainland.Page, "SettingsNav.network"));
                AssertTrue(SpinWait.SpinUntil(() =>
                {
                    fixture.Shell.Render(new(1000, 800));
                    var mirror = RegionalFind(fixture.Shell, mainland.Page, "SettingsOption.network.resource-source.mirrors-first");
                    return mirror.IsAssigned && fixture.Shell.Tree.GetComponent<XsrUiSelection>(mirror)?.IsSelected == true;
                }, TimeSpan.FromSeconds(5)));
                AssertTrue(RegionalFind(fixture.Shell, mainland.Page, "SettingsSelector.network.game-source").IsAssigned);
                var mirrorProvider = RegionalFind(fixture.Shell, mainland.Page, "SettingsOption.network.provider-mirror.false");
                AssertTrue(mirrorProvider.IsAssigned);
                AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiSelection>(mirrorProvider)!.IsSelected);
                AssertTrue(RegionalVisible(fixture.Shell, RegionalFind(fixture.Shell, mainland.Page, "NetworkMirrorResolution")));
                Emit(fixture.Intents, "ui.settings.section", RegionalFind(fixture.Shell, mainland.Page, "SettingsNav.about"));
                fixture.Shell.Render(new(1000, 800));
                AssertTrue(RegionalVisible(fixture.Shell, RegionalFind(fixture.Shell, mainland.Page, "AboutMainland")));
            }
            AssertEqual("mirrors-first", Read("network.game-source"));
            AssertEqual("mirrors-first", Read("network.resource-source"));
            AssertEqual("false", Read("network.provider-mirror"));

            string? Read(string key) => fixture.Foundation.Host.SettingsPolicy.Read(new()).Value!.Values.Single(value => value.Key == key).Value.Value;
            void Apply(string key, string value) => AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new(key, SettingsLayer.Global,
                new(SettingsOverrideMode.Custom, value))).IsSuccess);
        }
    }

    private static void RegionalResourcesHideMirrorChoicesAndRejectSyntheticIntents()
    {
        foreach (string country in new[] { "CN", "US", "HK", "MO", "TW", "" })
        {
            using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
            var queries = new XsrQueryRouterBuilder();
            int searches = 0;
            queries.Register<ResourceSearchQuery, ResourceSearchResult>(ResourceCatalogContract.Search, (_, _) =>
            {
                searches++;
                return ValueTask.FromResult(XsrResult.Success(new ResourceSearchResult([], 0, 0)));
            });
            // A stale/global mirror preference cannot override the independent country policy.
            queries.Register<ResourceNetworkPolicyQuery, ResourceNetworkPolicySnapshot>(ResourceCatalogContract.NetworkPolicy, (_, _) =>
                ValueTask.FromResult(XsrResult.Success(new ResourceNetworkPolicySnapshot("mirrors-first", true, null))));
            using var page = new ResourcesPageController(fixture.Shell, fixture.Intents,
                queries.Build(new NoopDispatchObserver()), fixture.Store, _ => { }, regionalPolicy: new(country));
            fixture.Shell.Renderer.ReducedMotion = true;
            fixture.Shell.Stage.Navigation.Replace(page.Page);
            AssertTrue(SpinWait.SpinUntil(() => { fixture.Shell.Render(new(1000, 800)); return searches == 1; }, TimeSpan.FromSeconds(5)));
            bool mainland = new RegionalPolicy(country).IsMainlandChina;
            AssertEqual(mainland, RegionalVisible(fixture.Shell, page.Find("ResourceNetwork")));
            var selected = fixture.Shell.Tree.GetComponent<XsrUiSegmentedTrack>(page.Find("ResourceNetwork"))!.Selected;
            AssertEqual(page.Find(mainland ? "ResourceNetwork.0" : "ResourceNetwork.1"), selected);
            int previous = searches;
            Emit(fixture.Intents, "ui.resources.action", page.Find("ResourceNetwork.0"));
            fixture.Shell.Render(new(1000, 800));
            AssertEqual(previous, searches);
            AssertEqual(selected, fixture.Shell.Tree.GetComponent<XsrUiSegmentedTrack>(page.Find("ResourceNetwork"))!.Selected);
        }
    }

    private static void RegionalAutomaticOptiFineTabsStayHiddenAndRejectIntents()
    {
        foreach (string country in new[] { "CN", "US", "HK", "MO", "TW", "" })
        {
            using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), regionalPolicy: new(country));
            fixture.Shell.Renderer.ReducedMotion = true;
            Emit(fixture.Intents, "ui.navigation.download");
            Emit(fixture.Intents, "ui.install.java");
            fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
            var scene = fixture.Shell.Render(new(1280, 800));
            AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "CatalogRow:game:1.20.6").Entity));
            fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
            fixture.Shell.Render(new(1280, 800));
            Emit(fixture.Intents, "ui.install.loader.forge");
            fixture.Shell.Render(new(1280, 800));
            bool mainland = new RegionalPolicy(country).IsMainlandChina;
            AssertEqual(mainland, IsVisible(fixture.Shell, "JavaOptiFineTab"));
            AssertTrue(IsVisible(fixture.Shell, "JavaForgeTab"));
            if (mainland) continue;
            var pager = fixture.Shell.Tree.GetComponent<XsrUiPager>(FindEntity(fixture.Shell, "JavaInstallPager"))!;
            int before = pager.PageIndex;
            Emit(fixture.Intents, "ui.install.page.optifine", FindEntity(fixture.Shell, "JavaOptiFineTab"));
            Emit(fixture.Intents, "ui.install.loader.optifine", FindEntity(fixture.Shell, "JavaLoaderOptiFine"));
            fixture.Shell.Render(new(1280, 800));
            AssertEqual(before, pager.PageIndex);
            AssertFalse(IsVisible(fixture.Shell, "JavaOptiFineTab"));
            AssertFalse(IsVisible(fixture.Shell, "JavaOptiFinePage"));
            AssertTrue(IsVisible(fixture.Shell, "JavaForgeTab"));
        }
    }

    private static bool RegionalVisible(XsrUiShell shell, XsrUiEntityId entity)
    {
        AssertTrue(entity.IsAssigned);
        // Literal default visibility does not require an XsrUiElement override component.
        return shell.Tree.GetComponent<XsrUiElement>(entity)?.IsVisible ?? true;
    }

    private static XsrUiEntityId RegionalFind(XsrUiShell shell, XsrUiEntityId root, string name)
    {
        XsrUiEntityId result = default;
        shell.Tree.Walk(root, entity => { if (shell.Tree.Name(entity) == name) result = entity; return true; });
        return result;
    }
}
