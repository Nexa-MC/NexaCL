using System.Globalization;
using Nexa.Services.Accounts;
using Nexa.Services.Composition;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Launch;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{

    private void ShowLaunch()
    {
        _boundDestination = XsrSemanticId.Parse("navigation.launch"); _ = _shell.Select(_boundDestination);
        ClearSubpageHistory();
        if (!_shell.Stage.Navigation.Current.Equals(_launchPage))
        {
            // Destination switches replace the page: the navigator's back stack is reserved
            // for hierarchical drill-in, not for moving between primary destinations.
            _shell.Stage.Navigation.Replace(_launchPage);
        }

    }

    /// <summary>
    /// Shows the first installation decision as a primary destination. Java and Bedrock are
    /// peer product choices here; their detail pages are pushed only after an explicit choice,
    /// so the title-bar back affordance never doubles as primary navigation.
    /// </summary>
    private void ShowInstallRoot()
    {
        if (_region.IsMainlandChina) return;
        _boundDestination = DownloadNavigationId; _ = _shell.Select(_boundDestination);
        ClearSubpageHistory();
        if (!_shell.Stage.Navigation.Current.Equals(_installPage))
        {
            _shell.Stage.Navigation.Replace(_installPage);
        }
    }

    private static bool TryGetJavaInstallSubpage(XsrSemanticId command, out JavaInstallSubpage subpage)
    {
        foreach (JavaInstallSubpage candidate in JavaInstallSubpages)
        {
            if (candidate.Command == command)
            {
                subpage = candidate;
                return true;
            }
        }

        subpage = null!;
        return false;
    }

    /// <summary>
    /// Selects one embedded configuration surface below the installation input. It does not
    /// push a product navigation page: switching Java/loader details is local configuration,
    /// not a new destination in the shell hierarchy.
    /// </summary>
    private void ShowJavaInstallSubpage(string pageKey)
    {
        if (pageKey == "JavaOptiFinePage" && !_region.IsMainlandChina) return;
        if (!_javaInstallEntities.TryGetValue("JavaInstallPager", out XsrUiEntityId pager)
            || !_javaInstallEntities.TryGetValue(pageKey, out XsrUiEntityId page)
            || !IsInstallEntityVisible(page))
        {
            return;
        }

        XsrUiEntityId[] pages = VisibleJavaInstallPages(pager);
        int index = Array.IndexOf(pages, page);
        if (index < 0)
        {
            return;
        }

        _activeJavaInstallPage = pageKey;
        RequestInstallCatalog();
        _ = _shell.Renderer.SelectPagerPage(pager, index);
        RefreshJavaInstallPresentation();
    }

    private sealed record JavaInstallSubpage(
        string PageKey,
        string TabKey,
        XsrSemanticId Command,
        string? Loader = null,
        string? RequiresLoader = null);
}
