using Nexa.Desktop.Ui;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ProxyProtocolSelectionRoundTripsPacAndKeepsDirectModeCompact()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        Pump(); Click("ui.settings.section", "SettingsNav.network");
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiSelection>(Find("SettingsProxyMode.1"))!.IsSelected);
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiElement>(Find("SettingsProxyManual"))!.IsVisible);
        Click("ui.settings.proxy.mode", "SettingsProxyMode.2");
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiElement>(Find("SettingsProxyManual"))!.IsVisible);
        fixture.Shell.Renderer.SetTextInputValue(Find("SettingsInput.network.proxy-address"), "http://localhost:7897");
        Click("ui.settings.proxy.protocol", "SettingsProxyProtocol.socks5");
        AssertEqual("socks5://localhost:7897", Draft());
        Click("ui.settings.proxy.protocol", "SettingsProxyProtocol.socks4");
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiElement>(Find("SettingsProxyRow.network.proxy-password"))!.IsVisible);
        Click("ui.settings.proxy.protocol", "SettingsProxyProtocol.pac");
        fixture.Shell.Renderer.SetTextInputValue(Find("SettingsInput.network.proxy-address"), "https://example.test/proxy.pac?v=2");
        Click("ui.settings.proxy.apply", "SettingsProxyApply");
        AssertTrue(SpinWait.SpinUntil(() =>
        {
            Pump(); return !settings.SettingsWritePending && fixture.Foundation.Host.SettingsPolicy.Read(new()).Value!.Values
                .Single(item => item.Key == "network.proxy-address").Value.Value == "pac+https://example.test/proxy.pac?v=2";
        }, TimeSpan.FromSeconds(5)));
        AssertTrue(SpinWait.SpinUntil(() =>
        {
            Click("ui.settings.proxy.reload", "SettingsProxyReload");
            return Draft() == "https://example.test/proxy.pac?v=2" && fixture.Shell.Tree.GetComponent<XsrUiSelection>(Find("SettingsProxyProtocol.pac"))!.IsSelected;
        }, TimeSpan.FromSeconds(5)));
        Click("ui.settings.proxy.mode", "SettingsProxyMode.0");
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiElement>(Find("SettingsProxyManual"))!.IsVisible);

        // Idle network frames must not keep reposting native scene commits at dispatcher priority.
        for (int frame = 0; frame < 12; frame++) Pump();
        int invalidations = 0;
        fixture.Shell.Tree.RenderInvalidated += OnInvalidated;
        for (int frame = 0; frame < 4; frame++) Pump();
        fixture.Shell.Tree.RenderInvalidated -= OnInvalidated;
        AssertEqual(0, invalidations);
        void OnInvalidated(object? sender, EventArgs args) => invalidations++;

        string Draft() => fixture.Shell.Tree.GetComponent<XsrUiTextInput>(Find("SettingsInput.network.proxy-address"))!.ReadDraft();
        void Pump() => fixture.Shell.Render(new(1100, 900));
        void Click(string command, string key) { Emit(fixture.Intents, command, Find(key)); Pump(); }
        XsrUiEntityId Find(string key)
        {
            XsrUiEntityId found = default;
            fixture.Shell.Tree.Walk(settings.Page, item => { if (fixture.Shell.Tree.Name(item) == key) found = item; return true; });
            AssertTrue(found.IsAssigned); return found;
        }
    }
}
