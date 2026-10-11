using Nexa.Desktop.Ui;
using Nexa.Services.Settings;
using Nexa.UI.Next;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void EnableInstanceDevelopmentMode(LaunchPageFixture fixture, string instance) =>
        AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("instance.development-mode", SettingsLayer.Instance,
            new(SettingsOverrideMode.Custom, "true"), instance)).IsSuccess);

    private static void InstanceDevelopmentModeKeepsAdvancedPagesScoped()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        string root = Path.Combine(fixture.TemporaryDirectory, "minecraft", "versions");
        string first = Path.Combine(root, "first"), second = Path.Combine(root, "second");
        foreach (string path in new[] { first, second })
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, Path.GetFileName(path) + ".json"),
                """{"id":"test","_minecraftVersion":"1.20.1","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.0"}]}""");
        }
        string instance = first;
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback, () => instance);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        void PumpUntil(Func<bool> done) => AssertTrue(SpinWait.SpinUntil(() =>
        { fixture.Shell.Render(new(1100, 900)); return done(); }, TimeSpan.FromSeconds(10)));
        XsrUiEntityId Find(string key)
        {
            XsrUiEntityId result = default;
            fixture.Shell.Tree.Walk(settings.Page, entity => { if (fixture.Shell.Tree.Name(entity) == key) result = entity; return true; });
            return result;
        }
        string[] advanced = ["modpack", "contentgraph", "recovery", "files", "diagnostics"];
        void AssertAdvanced(bool visible)
        { foreach (string page in advanced) AssertEqual(visible, Find("SettingsNav." + page).IsAssigned); }
        PumpUntil(() => Find("SettingsNav.mods").IsAssigned && Find("SettingsOption.instance.development-mode.true").IsAssigned);
        AssertAdvanced(false);
        foreach (string page in new[] { "game", "mods", "resourcepacks", "saves", "screenshots", "servers", "trash" })
            AssertTrue(Find("SettingsNav." + page).IsAssigned);
        AssertFalse(Find("SettingsInherit.instance.development-mode").IsAssigned);
        Emit(fixture.Intents, "ui.settings.choice", Find("SettingsOption.instance.development-mode.true"));
        PumpUntil(() => Find("SettingsNav.modpack").IsAssigned); AssertAdvanced(true);
        Emit(fixture.Intents, "ui.settings.section", Find("SettingsNav.recovery"));
        PumpUntil(() => settings.SelectedSection == "recovery");
        AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("instance.development-mode", SettingsLayer.Instance,
            new(SettingsOverrideMode.Custom, "false"), first)).IsSuccess);
        PumpUntil(() => settings.SelectedSection == "overview" && !Find("SettingsNav.recovery").IsAssigned);
        AssertAdvanced(false);
        EnableInstanceDevelopmentMode(fixture, first); PumpUntil(() => Find("SettingsNav.modpack").IsAssigned);
        instance = second;
        PumpUntil(() => Find("SettingsNav.mods").IsAssigned && !Find("SettingsNav.modpack").IsAssigned);
        AssertAdvanced(false);
        instance = first; PumpUntil(() => Find("SettingsNav.modpack").IsAssigned); AssertAdvanced(true);
        AssertEqual("false", fixture.Foundation.Host.SettingsPolicy.Read(new(first)).Value!.Values
            .Single(item => item.Key == "recovery.keep-history").Value.Value);
    }
}
