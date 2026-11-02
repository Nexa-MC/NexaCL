using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{
    private static readonly Uri MinecraftLauncherStore = new("https://apps.microsoft.com/detail/9PGW18NPBZV5");
    private XsrUiEntityId _javaLauncherGuidePage;
    private XsrUiEntityId _javaLauncherStoreButton;
    internal Action<Uri>? OpenJavaLauncherStore { get; set; }

    private void InitializeJavaLauncherGuide()
    {
        if (!_region.IsMainlandChina) return;
        var guide = LoadStandalonePage("JavaLauncherGuidePage.nxml");
        _javaLauncherGuidePage = guide.Page;
        _javaLauncherStoreButton = guide.Entities["JavaLauncherStore"];
    }

    private bool HandleJavaLauncherGuideIntent(DesktopUiIntentEventArgs args)
    {
        if (args.Intent.Command.Value != "ui.install.java.store") return false;
        if (!_region.IsMainlandChina || _shell.Stage.Navigation.Current != _javaLauncherGuidePage
            || args.Intent.Source != _javaLauncherStoreButton
            || !_shell.Tree.IsAlive(_javaLauncherStoreButton)
            || _shell.Tree.GetComponent<XsrUiInput>(_javaLauncherStoreButton) is not { Enabled: true, Clickable: true }
            || !IsJavaLauncherGuideVisible()) return true;
        try
        {
            if (OpenJavaLauncherStore is null) throw new InvalidOperationException("The native browser action is not configured.");
            OpenJavaLauncherStore(MinecraftLauncherStore);
            _feedback.Info(InstallCopy("java-guide.store-opened"));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            _feedback.Error(InstallCopy("java-guide.store-failed"));
        }
        return true;
    }

    private bool IsJavaLauncherGuideVisible()
    {
        for (var node = _javaLauncherStoreButton; node.IsAssigned && _shell.Tree.IsAlive(node); node = _shell.Tree.Parent(node))
        {
            if (_shell.Tree.GetComponent<XsrUiElement>(node)?.IsVisible == false) return false;
            if (node == _javaLauncherGuidePage) return true;
        }
        return false;
    }
}
