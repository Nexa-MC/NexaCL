using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private string _modCategory = "all";
    private readonly Dictionary<XsrUiEntityId, string> _modCategoryOptions = [];
    private XsrUiEntityId _modCategoryTrack;

    private static bool MatchesModCategory(InstanceContentEntry item, string category) => category switch
    {
        "enabled" => item.Enabled == true,
        "disabled" => item.Enabled == false,
        "updates" => item.UpdateAvailable == true,
        "problems" => item.PackageProblem.Length > 0,
        "unchecked" => item.Enabled is not null && (item.PackageReadable is null || item.UpdateAvailable is null),
        _ => true
    };

    private XsrUiEntityId _modCategoryRoot;
    private Dictionary<string, string> ModCategoryProjection()
    {
        Dictionary<string, string> data = new(StringComparer.Ordinal);
        foreach (var key in new[] { "all", "enabled", "disabled", "updates", "problems", "unchecked" })
        { data[key + "Selected"] = ContentFlag(key == _modCategory); data[key + "Style"] = key == _modCategory ? "settings-content-category settings-content-category-selected" : "settings-content-category"; }
        return data;
    }
    private void BuildModCategories(XsrUiEntityId parent)
    {
        _modCategoryOptions.Clear();
        _modCategoryRoot = AppendContent(parent, "SettingsContentModCategories.d.nxml", ModCategoryProjection());
        var nodes = ContentNodes(_modCategoryRoot); _modCategoryTrack = nodes["ManagementModCategoryTrack"];
        foreach (var key in new[] { "all", "enabled", "disabled", "updates", "problems", "unchecked" })
        {
            var option = nodes["ManagementModCategory." + key]; _modCategoryOptions[option] = key;
            RegisterContentAction(option, () => { _modCategory = key; UpdateModCategories(); ApplyContentFilter(); _shell.Tree.GetComponent<XsrUiScroll>(_sections)!.OffsetY = 0; UpdateContentWindow(); });
        }
    }
    private void UpdateModCategories()
    {
        ReloadContent(_modCategoryRoot, ModCategoryProjection().Select(pair => (pair.Key, pair.Value)).ToArray());
        var selected = _modCategoryOptions.First(pair => pair.Value == _modCategory).Key;
        _shell.Tree.GetComponent<XsrUiSegmentedTrack>(_modCategoryTrack)!.Selected = selected;
    }
}
