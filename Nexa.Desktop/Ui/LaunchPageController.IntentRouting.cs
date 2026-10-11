using System.Globalization;
using Nexa.Pxml;
using Nexa.Services.Accounts;
using Nexa.Services.Composition;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;

using Nexa.Services.Minecraft.Launch;

using Nexa.UI.Next;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{

    private void OnIntentEmitted(object? sender, DesktopUiIntentEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (HandleProcessIntent(e)) return;
        if (HandleInstallCatalogIntent(e)) return;
        XsrSemanticId command = e.Intent.Command;
        if (command == LaunchRoute)
        {
            ShowLaunch();
            PublishProfileFacts();
            _ = QueueRefresh();
        }
        else if (command == InstallRoute)
        {
            ShowInstallRoot();
        }
        else if (command == InstallJavaCommand)
        {
            OpenSubpage(_javaInstallPage, e.Intent.Source);
            RequestInstallCatalog();
            RefreshJavaInstallPresentation();
        }
        else if (command == InstallBedrockCommand)
        {
            OpenSubpage(_bedrockInstallPage, e.Intent.Source);
        }
        else if (TryGetJavaInstallSubpage(command, out JavaInstallSubpage subpage))
        {
            ShowJavaInstallSubpage(subpage.PageKey);
        }
        else if (command == InstallVersion1211Command)
        {
            SelectInstallVersion("1.21.1", "JavaVersion1211");
        }
        else if (command == InstallVersion1206Command)
        {
            SelectInstallVersion("1.20.6", "JavaVersion1206");
        }
        else if (command == InstallVersion1201Command)
        {
            SelectInstallVersion("1.20.1", "JavaVersion1201");
        }
        else if (command == InstallLoaderFabricCommand)
        {
            SelectInstallLoader("Fabric", "JavaLoaderFabric");
        }
        else if (command == InstallLoaderVanillaCommand)
        {
            SelectInstallLoader("原版 Minecraft", string.Empty);
        }
        else if (command == InstallLoaderForgeCommand)
        {
            SelectInstallLoader("Forge", "JavaLoaderForge");
        }
        else if (command == InstallLoaderNeoForgeCommand)
        {
            SelectInstallLoader("NeoForge", "JavaLoaderNeoForge");
        }
        else if (command == InstallLoaderQuiltCommand)
        {
            SelectInstallLoader("Quilt", "JavaLoaderQuilt");
        }
        else if (command == InstallLoaderOptiFineCommand)
        {
            SelectInstallLoader("OptiFine", "JavaLoaderOptiFine");
        }
        else if (command == InstallLoaderCleanroomCommand)
        {
            SelectInstallLoader("Cleanroom", "JavaLoaderCleanroom");
        }
        else if (command == InstallLoaderLiteLoaderCommand)
        {
            SelectInstallLoader("LiteLoader", "JavaLoaderLiteLoader");
        }
        else if (command == InstallLoaderLegacyFabricCommand)
        {
            SelectInstallLoader("Legacy Fabric", "JavaLoaderLegacyFabric");
        }
        else if (command == InstallLoaderLabyModCommand)
        {
            SelectInstallLoader("LabyMod", "JavaLoaderLabyMod");
        }
        else if (command == InstallFabricApiAddonCommand)
        {
            ToggleInstallAddon("Fabric API", "JavaFabricApiSelect");
        }
        else if (command == InstallQslAddonCommand)
        {
            ToggleInstallAddon("QSL", "JavaQslSelect");
        }
        else if (command == InstallStartCommand)
        {
            NotifyInstallUnavailable();
        }
        else if (command == LaunchPrimaryCommand)
        {
            string instanceId = ReadCell(LaunchPageState.SelectedInstanceKey);
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                NavigateToDownload();
            }
            else
            {
                _launchRequest = StartLaunchAsync(instanceId, e.Intent.Source);
            }
        }
        else if (command == LaunchCancelCommand)
        {
            _ = CancelLaunchAsync();
        }
        else if (command == LaunchInstancesCommand)
        {
            OpenSubpage(_versionListPage, e.Intent.Source);
        }
        else if (command == LaunchSettingsCommand)
        {
            OpenBoundSubpage(_versionSettingsPage, e.Intent.Source, "版本设置页面未绑定。");
        }
        else if (command == LaunchModifyCommand)
        {
            OpenInstallEditor(e.Intent.Source);
        }
        else if (command == AccountWardrobeCommand)
        {
            OpenBoundSubpage(_wardrobePage, e.Intent.Source, "更衣橱页面未绑定。");
        }
        else if (command.Value == "ui.launch.restore")
        {
            if (LaunchBusy) ShowLaunchingPage(e.Intent.Source);
        }
        else if (command == PageBackCommand)
        {
            if (_shell.Stage.Navigation.Pop())
            {
                UpdateTitleBar();
                if (_returnFocus.TryPop(out XsrUiEntityId focus))
                    _shell.Renderer.Focus(focus, IsKeyboardIntent(e.Intent.Source));
            }
        }
        else if (command == WidgetAboutCommand || command == WidgetTriviaCommand || command == WidgetEchoCommand)
        {
            XsrUiEntityId pager = _pageEntities["LaunchWidgetPager"];
            _ = _shell.Renderer.SelectPagerPage(pager, command == WidgetTriviaCommand ? 1 : command == WidgetEchoCommand ? 2 : 0);
        }
        else if (command == WidgetHintCommand)
        {
            AdvanceHint();
        }
        else if (command == AccountDeleteCommand)
        {
            XsrUiEntityId row = e.Intent.Source;
            while (row.IsAssigned && !_accountRowIndexes.ContainsKey(row)) row = _shell.Tree.Parent(row);
            if (_accountRowIndexes.TryGetValue(row, out int index)) _ = RemoveAccountAsync(index, _accountRosterRevision);
        }
        else if (command == AccountSelectCommand)
        {
            if (_accountRowIndexes.TryGetValue(e.Intent.Source, out int index))
            {
                _accountKeyboardFocus = IsKeyboardIntent(e.Intent.Source);
                _ = SelectAccountAsync(index, _accountRosterRevision);
            }
        }
        else if (command == AccountSwitchCommand)
        {
            _accountKeyboardFocus = IsKeyboardIntent(e.Intent.Source);
            Publish(LaunchPageState.AccountPickerKey, true);
        }
        else if (command == AccountDismissCommand)
        {
            _accountKeyboardFocus = IsKeyboardIntent(e.Intent.Source);
            Publish(LaunchPageState.AccountPickerKey, false);
        }
        else if (IsDestinationCommand(command) || command.Value.StartsWith("ui.navigation.", StringComparison.Ordinal)
            && command != XsrUiShellIds.NavigationExpand && command != XsrUiShellIds.NavigationSelect)
            OpenBoundDestination(command);
    }

    private bool IsDestinationCommand(XsrSemanticId command) =>
        _shell.NavigationItems.Any(item => item.Command == command);

    private void AdvanceHint(bool automatic = false)
    {
        lock (_hintGate)
        {
            if (_disposed || automatic && !_hintTimerRunning) return;
            _hintIndex = (_hintIndex + Random.Shared.Next(1, LaunchWidgetHints.BuiltIn.Count)) % LaunchWidgetHints.BuiltIn.Count;
            Publish(LaunchPageState.WidgetHintKey, LaunchWidgetHints.BuiltIn[_hintIndex]);
        }
    }

    private async Task RemoveAccountAsync(int index, long revision)
    {
        if (!_foundationCommands.TryResolve(FoundationRouteIds.AccountRemoveProfile, out XsrCommandId route)) return;
        XsrResult result = await _foundationCommands.Dispatch(route, new AccountRemoveProfileCommand(index, revision),
            cancellationToken: _lifetimeCancellation.Token).Completion.ConfigureAwait(false);
        if (!_disposed && !result.IsSuccess) _feedback.Error($"删除档案失败：{result.Error?.Message}");
    }

    private async Task SelectAccountAsync(int index, long revision)
    {
        if (!_foundationCommands.TryResolve(FoundationRouteIds.AccountSelectProfile, out XsrCommandId route))
        {
            _feedback.Error("账户切换命令未注册。");
            return;
        }

        XsrResult result = await _foundationCommands.Dispatch(route,
            new AccountSelectProfileCommand(index, revision), cancellationToken: _lifetimeCancellation.Token)
            .Completion.ConfigureAwait(false);
        if (_disposed) return;
        if (result.IsSuccess) Publish(LaunchPageState.AccountPickerKey, false);
        else _feedback.Error($"切换档案失败：{result.Error?.Message}");
    }
}
