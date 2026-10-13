using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{
    private NxmlDynamicContent? _installDynamic;
    private NxmlDynamicContent InstallDynamic => _installDynamic ??= new(_shell.Tree, _store);
    private readonly Dictionary<XsrUiEntityId, (string Template, Dictionary<string, string> Data)> _installViews = [];
    private XsrUiEntityId _installWorkspace;
    private static string InstallFlag(bool value) => value ? "true" : "false";
    private static string InstallNumber(double value) => value.ToString(CultureInfo.InvariantCulture);
    private static string InstallIdentity(string value) => value.Length is > 0 and <= 96
        && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '{' or '}')
            ? value : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string InstallCopy(string key) => _shell.Renderer.LocalizeText(LaunchInstallText.Raw(key));
    private string InstallFormat(string name, params (string Key, string Value)[] data) =>
        LaunchInstallText.Format(name, data.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal), _shell.Renderer.LocalizeText);

    private XsrUiEntityId AppendInstall(XsrUiEntityId slot, string template, Dictionary<string, string> data)
    {
        var root = InstallDynamic.Append(slot, DesktopMarkup.Dynamic(template, data, _shell.Renderer.LocalizeText));
        _installViews[root] = (template, data);
        return root;
    }

    private void ReloadInstall(XsrUiEntityId root, params (string Key, string Value)[] values)
    {
        var view = _installViews[root];
        var candidate = new Dictionary<string, string>(view.Data, StringComparer.Ordinal);
        bool changed = false;
        foreach (var (key, value) in values)
            if (candidate.GetValueOrDefault(key) != value) { candidate[key] = value; changed = true; }
        if (!changed) return;
        var fragment = DesktopMarkup.Dynamic(view.Template, candidate, _shell.Renderer.LocalizeText);
        InstallDynamic.Reload(root, fragment);
        _installViews[root] = (view.Template, candidate);
    }

    private void RemoveInstall(XsrUiEntityId root)
    {
        InstallDynamic.Remove(root);
        _installViews.Remove(root);
    }

    private Dictionary<string, string> JavaInstallWorkspaceData()
    {
        Dictionary<string, string> data = new()
        {
            ["inputLabel"] = InstallCopy("input.search"),
            ["startText"] = InstallCopy("install.start"),
            ["startLabel"] = InstallCopy("install.start-label"),
            ["startEnabled"] = "true",
        };
        foreach (var subpage in JavaInstallSubpages)
        {
            bool active = subpage.PageKey == "JavaMinecraftPage";
            data[subpage.PageKey + "_visible"] = InstallFlag(active);
            data[subpage.PageKey + "_statusVisible"] = "true";
            data[subpage.TabKey + "_selected"] = InstallFlag(active);
            data[subpage.TabKey + "_style"] = LaunchInstallText.Raw(active ? "style.tab-active" : "style.tab");
            data[subpage.TabKey + "_label"] = InstallFormat(active ? "TabCurrent" : "TabView", ("name", InstallCopy("tab." + subpage.TabKey)));
        }
        foreach (string key in JavaInstallLoaderKeys.Concat(["JavaFabricApiSelect", "JavaQslSelect"]))
        {
            data[key + "_selected"] = "false";
            if (key is "JavaFabricApiSelect" or "JavaQslSelect")
                data[key + "_label"] = InstallFormat("AddonAdd", ("addon", InstallCopy(key == "JavaFabricApiSelect" ? "addon.fabric-api" : "addon.qsl")));
            data[key + "_style"] = LaunchInstallText.Raw(key is "JavaFabricApiSelect" or "JavaQslSelect" ? "style.addon" : "style.choice");
        }
        return data;
    }

    private void ProjectInstallInputLabel() => ReloadInstall(_installWorkspace,
        ("inputLabel", InstallCopy(_installGameChosen ? "input.name" : "input.search")));

    private void ProjectInstallChoice(string key, bool selected)
    {
        if (!_javaInstallEntities.ContainsKey(key)) return;
        bool addon = key is "JavaFabricApiSelect" or "JavaQslSelect";
        ReloadInstall(_installWorkspace, (key + "_selected", InstallFlag(selected)),
            (key + "_style", LaunchInstallText.Raw(addon ? selected ? "style.addon-active" : "style.addon" : selected ? "style.choice-active" : "style.choice")));
    }

    private void ProjectInstallAddon(string addon, string key, bool selected)
    {
        ProjectInstallChoice(key, selected);
        ReloadInstall(_installWorkspace, (key + "_label", InstallFormat(selected ? "AddonRemove" : "AddonAdd",
            ("addon", InstallCopy(key == "JavaFabricApiSelect" ? "addon.fabric-api" : "addon.qsl")))));
    }

    private void ProjectInstallStatus(string pageKey, string message)
    {
        ReloadInstall(_catalogStatus[pageKey], ("message", message), ("label", InstallCopy("catalog.retry")),
            ("visible", InstallFlag(message.Length > 0)));
        ReloadInstall(_installWorkspace, (pageKey + "_statusVisible", InstallFlag(message.Length > 0)));
    }

    private string _installLocaleStamp = "";
    private bool RefreshInstallLocalization()
    {
        string stamp = DesktopResourceText.Language(_store) + "\0" + InstallCopy("install.start") + "\0" + InstallCopy("catalog.loading");
        if (_installLocaleStamp == stamp) return false;
        _installLocaleStamp = stamp;
        _catalogRevision = -1;
        _presentedJavaInstallPage = -1;
        _editPlanKey = null;
        Publish(LaunchPageState.JavaInstallTitleKey, InstallCopy(_editingInstall ? "edit.title" : "install.title"));
        if (!_editingInstall || _installEdit is null)
            ReloadInstall(_installWorkspace, ("startEnabled", InstallFlag(!_editingInstall)),
                ("startText", InstallCopy(_editingInstall ? "edit.save" : "install.start")),
                ("startLabel", InstallCopy(_editingInstall ? "edit.save-label" : "install.start-label")));
        RefreshJavaInstallPresentation();
        UpdateTitleBar();
        return true;
    }
}
