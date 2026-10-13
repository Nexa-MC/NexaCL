using Nexa.Desktop.Ui;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void LaunchInstallBedrockWorkspaceRetainsControlsStatusAndFocusAcrossProjection()
    {
        int requests = 0;
        bool fail = true;
        using var fixture = new BedrockInstallFixture(() =>
        {
            requests++;
            if (fail) throw new IOException("private-native-failure");
        }, _ => throw new InvalidOperationException("Unexpected browser request."), supportsStore: true);
        var shell = fixture.Shell;
        var scene = fixture.Render();
        var workspace = FindByKey(shell, scene, "BedrockInstallHost").Entity;
        var slot = shell.Tree.Parent(workspace);
        var status = FindByKey(shell, scene, "BedrockInstallStatus").Entity;
        var store = FindByKey(shell, scene, "BedrockInstallStore").Entity;
        var website = FindByKey(shell, scene, "BedrockInstallWebsite").Entity;
        var statusText = shell.Tree.GetComponent<XsrUiText>(status)!;
        var statusSemantic = shell.Tree.GetComponent<XsrUiSemantic>(status)!;
        var storeInput = shell.Tree.GetComponent<XsrUiInput>(store)!;
        AssertEqual("BedrockInstallWorkspaceSlot", shell.Tree.Name(slot));
        AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(slot) is not null);
        AssertEqual(19d, FindByKey(shell, scene, "BedrockInstallTitle").VisualStyle.FontSize);
        AssertEqual(600d, FindByKey(shell, scene, "BedrockInstallTitle").VisualStyle.FontWeight);
        AssertEqual(10d, FindByKey(shell, scene, "BedrockInstallStore").VisualStyle.CornerRadius);
        AssertTrue(shell.Renderer.Focus(store, showIndicator: false));
        AssertTrue(shell.Renderer.Activate(store));
        scene = fixture.Render();
        AssertEqual("无法打开 Microsoft Store。请重试，或使用官方产品页。", FindByKey(shell, scene, "BedrockInstallStatus").Text);
        AssertEqual(status, FindByKey(shell, scene, "BedrockInstallStatus").Entity);
        AssertEqual(store, shell.Renderer.Focused);
        AssertTrue(ReferenceEquals(storeInput, shell.Tree.GetComponent<XsrUiInput>(store)));

        const string success = "已请求打开 Microsoft Store。请在商店中完成获取或安装。";
        const string firstTranslation = "translated <status> {data status} {state secret}";
        const string secondTranslation = "changed <status> {data status} {state secret}";
        bool changedLanguage = false;
        shell.Renderer.TextLocalizer = source => source == success
            ? changedLanguage ? secondTranslation : firstTranslation
            : source == firstTranslation || source == secondTranslation ? "Unexpected second translation" : source;
        fail = false;
        AssertTrue(shell.Renderer.Activate(store));
        scene = fixture.Render();
        AssertEqual(firstTranslation, FindByKey(shell, scene, "BedrockInstallStatus").Text);
        AssertEqual(firstTranslation, FindByKey(shell, scene, "BedrockInstallStatus").Label);
        AssertFalse(statusText.Localize);
        AssertFalse(statusSemantic.Localize);
        var notifications = fixture.Feedback.Snapshot().Notifications;
        AssertEqual(success, notifications[notifications.Count - 1].Message);
        changedLanguage = true;
        scene = fixture.Render();
        AssertEqual(secondTranslation, FindByKey(shell, scene, "BedrockInstallStatus").Text);
        AssertEqual(secondTranslation, FindByKey(shell, scene, "BedrockInstallStatus").Label);
        AssertEqual(workspace, FindByKey(shell, scene, "BedrockInstallHost").Entity);
        AssertEqual(status, FindByKey(shell, scene, "BedrockInstallStatus").Entity);
        AssertEqual(store, FindByKey(shell, scene, "BedrockInstallStore").Entity);
        AssertEqual(website, FindByKey(shell, scene, "BedrockInstallWebsite").Entity);
        AssertEqual(slot, shell.Tree.Parent(workspace));
        AssertEqual(store, shell.Renderer.Focused);
        AssertTrue(ReferenceEquals(statusText, shell.Tree.GetComponent<XsrUiText>(status)));
        AssertTrue(ReferenceEquals(statusSemantic, shell.Tree.GetComponent<XsrUiSemantic>(status)));
        AssertTrue(ReferenceEquals(storeInput, shell.Tree.GetComponent<XsrUiInput>(store)));
        AssertEqual(2, requests);
        AssertEqual(2, fixture.Feedback.Snapshot().Notifications.Count);
    }

    private static void LaunchInstallBedrockUnsupportedReloadRetainsDisabledStore()
    {
        int stores = 0, browsers = 0;
        using var fixture = new BedrockInstallFixture(() => stores++, _ => browsers++, supportsStore: false);
        var shell = fixture.Shell;
        var scene = fixture.Render();
        var store = FindByKey(shell, scene, "BedrockInstallStore").Entity;
        var website = FindByKey(shell, scene, "BedrockInstallWebsite").Entity;
        var status = FindByKey(shell, scene, "BedrockInstallStatus").Entity;
        var input = shell.Tree.GetComponent<XsrUiInput>(store)!;
        AssertFalse(input.Enabled); AssertFalse(input.Clickable); AssertFalse(input.Focusable);
        AssertFalse(shell.Renderer.Focus(store));
        AssertTrue(shell.Renderer.Focus(website, showIndicator: false));
        AssertTrue(shell.Renderer.Activate(website));
        var catalog = new UiLocalizationCatalog(); catalog.SetLanguage("en");
        shell.Renderer.TextLocalizer = catalog.Translate;
        scene = fixture.Render();
        AssertEqual("The official product page was requested. Installing Minecraft for Windows requires Windows.",
            FindByKey(shell, scene, "BedrockInstallStatus").Text);
        AssertEqual(FindByKey(shell, scene, "BedrockInstallStatus").Text, FindByKey(shell, scene, "BedrockInstallStatus").Label);
        AssertEqual(status, FindByKey(shell, scene, "BedrockInstallStatus").Entity);
        AssertEqual(website, FindByKey(shell, scene, "BedrockInstallWebsite").Entity);
        AssertEqual(website, shell.Renderer.Focused);
        AssertTrue(ReferenceEquals(input, shell.Tree.GetComponent<XsrUiInput>(store)));
        AssertFalse(input.Enabled); AssertFalse(input.Clickable); AssertFalse(input.Focusable);
        Emit(fixture.Intents, "ui.bedrock.install.store", store);
        AssertEqual(0, stores); AssertEqual(1, browsers);
        AssertEqual(1, fixture.Feedback.Snapshot().Notifications.Count);
    }

    private static void LaunchInstallBedrockPlaceholderLoadsCompleteOwnedWorkspace()
    {
        var state = new XsrStateStoreBuilder().Build();
        var shell = new XsrUiShell(state);
        shell.Renderer.ReducedMotion = true;
        var page = BedrockInstallPageController.LoadPlaceholder(shell, state);
        shell.Stage.Navigation.Replace(page);
        var scene = shell.Render(new(900, 650));
        var workspace = FindByKey(shell, scene, "BedrockInstallHost").Entity;
        AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(workspace)) is not null);
        AssertEqual("安装 Minecraft for Windows", FindByKey(shell, scene, "BedrockInstallTitle").Text);
        var description = FindByKey(shell, scene, "BedrockInstallDescription");
        AssertTrue(description.Text!.Contains("Microsoft Store", StringComparison.Ordinal));
        AssertTrue(description.Rect.Height > 0);
        AssertEqual(OperatingSystem.IsWindows(), FindByKey(shell, scene, "BedrockInstallStore").IsClickable);
        AssertTrue(FindByKey(shell, scene, "BedrockInstallWebsite").IsClickable);
        AssertEqual(OperatingSystem.IsWindows()
            ? "打开 Microsoft Store，继续获取或安装游戏。"
            : "当前系统不支持 Minecraft for Windows 安装。可查看官方产品页。",
            FindByKey(shell, scene, "BedrockInstallStatus").Text);
        shell.Tree.Destroy(page);
        AssertFalse(shell.Tree.IsAlive(workspace));
    }
}
