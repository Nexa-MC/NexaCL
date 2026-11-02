using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private readonly Dictionary<XsrUiEntityId, (Editor Editor, XsrUiEntityId Auto, XsrUiEntityId Manual)> _autoModes = [];

    private void UpdateAutomaticModes()
    {
        foreach (var (track, mode) in _autoModes)
        {
            var value = _values?.Values.FirstOrDefault(item => item.Key == mode.Editor.Entry.SettingKey)?.Value;
            bool auto = value?.Mode == SettingsOverrideMode.Auto || mode.Editor.Entry.SettingKey == "java.runtime" && string.IsNullOrWhiteSpace(value?.Value);
            var selected = auto ? mode.Auto : mode.Manual;
            _shell.Tree.GetComponent<XsrUiSegmentedTrack>(track)!.Selected = selected;
            ReloadCatalog(_autoModeViews[track], ("autoSelected", Flag(auto)), ("manualSelected", Flag(!auto)));
        }
    }

    private XsrUiEntityId AppendSettingsForm(XsrUiEntityId parent, string name, string? label = null)
    {
        var root = _dynamic.Append(parent, DesktopMarkup.Dynamic("SettingsForm.d.nxml", new Dictionary<string, string>
        {
            ["key"] = name,
            ["label"] = label ?? "",
            ["labeled"] = Flag(label is not null),
            ["surface"] = name + ".Surface",
            ["body"] = name + ".Body",
        }));
        return CatalogNodes(root)[name + ".Body"];
    }

    private static string InstanceSettingGroup(SettingsCatalogEntry entry) => entry.SettingKey switch
    {
        "game.gamemode" or "game.cpu-policy" or "game.cpu-quota-percent" or "game.process-memory-limit-mib" or "game.io-read-limit-mib" or "game.io-write-limit-mib" => CatalogCopy("catalog.resource.quota"),
        "game.memory" or "java.runtime" or "java.vendor" or "java.auto-install" => CatalogCopy("catalog.copy.2720b4221e5e4ca3"),
        "game.server" => CatalogCopy("catalog.copy.2388cbc6a486af9f"),
        "game.jvm" or "game.arguments" => CatalogCopy("catalog.copy.a42c8d6a892a2bb0"),
        _ => CatalogCopy("catalog.copy.8cdf1da0d4c7e55e"),
    };
    // Legacy callers in the remaining diagnostics slices; the developer inspector no longer uses this factory.
}
