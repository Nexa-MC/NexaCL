using System.Text;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private void RegisterContentAction(XsrUiEntityId entity, Action action)
    {
        long generation = _contentScopeGeneration;
        _managementActions[entity] = () => { if (_contentScopeGeneration == generation && ContentActionCurrent(entity)) action(); };
        if (!_contentActions.Contains(entity)) _contentActions.Add(entity);
    }

    private void BuildContentRowActions(Dictionary<string, XsrUiEntityId> nodes, InstanceContentEntry item)
    {
        string key = ContentFileKey(item.Name);
        RegisterContentAction(nodes["ManagementContentDetails." + key], () => OpenContentDetail(item));
        RegisterContentAction(nodes["ManagementContentToggle." + key], () => ToggleMod(item));
        if (OnlineFile(item) is { } query && _onlineList.TryGetValue(query, out var online) && online.UpdateVersion is { } update)
            RegisterContentAction(nodes["ManagementContentUpdate." + key], () => UpdateOnlinePack(query, update));
        RegisterContentAction(nodes["ManagementContentRemove." + key], () => RemoveContent(item));
    }

    private void OpenContentDetail(InstanceContentEntry item)
    {
        item = LinkedContent(item);
        _contentReturnOffset = _shell.Tree.GetComponent<XsrUiScroll>(_sections)!.OffsetY;
        _contentDetail = item;
        BuildSections(true);
        _shell.Tree.GetComponent<XsrUiScroll>(_sections)!.OffsetY = 0;
    }

    private readonly Dictionary<string, XsrUiEntityId> _contentDetailFacts = new(StringComparer.Ordinal);
    private void BuildContentDetail(InstanceContentEntry item)
    {
        _contentWorkspace = AppendContent(_sections, "SettingsContentDetail.d.nxml", ContentDetailProjection(item));
        var nodes = ContentNodes(_contentWorkspace);
        BindContentAction(nodes, ContentKey("back"), () => { _contentDetail = null; _scrollPositions[_selected] = _contentReturnOffset; BuildSections(true); });
        BindContentDetailToggle(nodes, item);
        BindContentAction(nodes, ContentKey("remove"), () => { if (_contentDetail is { } current) RemoveContent(current); });
        string? directory = _management?.Pages.FirstOrDefault(page => page.Id == _selected)?.Directory;
        BindContentAction(nodes, "ContentDetail.Folder", () => { if (directory is not null) OpenContentDirectory(item.IsDirectory ? System.IO.Path.Combine(directory, item.Name) : directory); });
        BindContentImage(nodes["ContentDetail.Icon"], item);
        ApplyContentFormatting(nodes["ContentDetail.Title"], _selected == "resourcepacks" ? ResourcePackTitle(item) : item.DisplayName.Length > 0 ? item.DisplayName : item.Name);
        ApplyContentFormatting(nodes["ContentDetail.Description"], item.Description);
        _contentDetailFacts.Clear(); ProjectContentDetailFacts(nodes["ContentDetail.Facts"], item);
        if (_selected == "saves") BuildWorldDetails(nodes["ContentDetail.World"], item);
        BuildOnlineContent(nodes["ContentDetail.Online"], item); BuildContentIntegrity(nodes["ContentDetail.Integrity"], item);
    }

    private Dictionary<string, string> ContentDetailProjection(InstanceContentEntry item) => new()
    {
        ["enabled"] = ContentFlag(_managementWrite is null),
        ["canToggle"] = ContentFlag(item.Enabled is not null),
        ["active"] = ContentFlag(item.Enabled == true),
        ["toggleLabel"] = ContentTerm(item.Enabled == false ? "enable" : "disable"),
        ["inactive"] = ContentFlag(item.Enabled == false),
        ["toggleTrack"] = "ManagementModToggle." + ContentFileKey(item.Name),
        ["toggleTrue"] = "ManagementModToggle." + ContentFileKey(item.Name) + ".true",
        ["toggleFalse"] = "ManagementModToggle." + ContentFileKey(item.Name) + ".false",
        ["activeStyle"] = item.Enabled == true ? "settings-content-binary-option settings-content-category-selected" : "settings-content-binary-option",
        ["inactiveStyle"] = item.Enabled == false ? "settings-content-binary-option settings-content-category-selected" : "settings-content-binary-option",
        ["icon"] = ContentIcon(),
        ["title"] = StripContentFormatting(_selected == "resourcepacks" ? ResourcePackTitle(item) : item.DisplayName.Length > 0 ? item.DisplayName : item.Name),
        ["subtitle"] = ContentFormat("DetailSubtitle", ("page", _shell.Renderer.LocalizeText(Pages.First(page => page.Id == _selected).Label)), ("state", item.Enabled is { } active ? ContentTerm(active ? "enabledSuffix" : "disabledDetailSuffix") : "")),
        ["description"] = StripContentFormatting(item.Description),
        ["hasDescription"] = ContentFlag(item.Description.Length > 0),
        ["folderLabel"] = ContentTerm(item.IsDirectory ? "openContentFolder" : "openContainingFolder"),
        ["hasDirectory"] = ContentFlag(_management?.Pages.FirstOrDefault(page => page.Id == _selected)?.Directory is not null)
    };

    private void ReloadContentDetail()
    {
        if (_contentDetail is not { } item) return;
        ReloadContent(_contentWorkspace, ContentDetailProjection(item).Select(pair => (pair.Key, pair.Value)).ToArray());
        var nodes = ContentNodes(_contentWorkspace); BindContentImage(nodes["ContentDetail.Icon"], item);
        ApplyContentFormatting(nodes["ContentDetail.Title"], _selected == "resourcepacks" ? ResourcePackTitle(item) : item.DisplayName.Length > 0 ? item.DisplayName : item.Name);
        ApplyContentFormatting(nodes["ContentDetail.Description"], item.Description); BindContentDetailToggle(nodes, item);
        ProjectContentDetailFacts(nodes["ContentDetail.Facts"], item);
        if (_selected == "saves") BuildWorldDetails(nodes["ContentDetail.World"], item);
        BuildOnlineContent(nodes["ContentDetail.Online"], item); BuildContentIntegrity(nodes["ContentDetail.Integrity"], item);
    }
    private void ProjectContentDetailFacts(XsrUiEntityId slot, InstanceContentEntry item)
    {
        Dictionary<string, string> facts = new(StringComparer.Ordinal);
        if (_selected == "mods")
        {
            if (item.PackageProblem.Length > 0) facts["package"] = item.PackageProblem;
            facts["update"] = item.UpdateAvailable == true ? ContentFormat("UpdateVersion", ("version", item.UpdateVersion)) : ContentTerm(item.UpdateAvailable == false ? "noUpdates" : "unchecked");
        }
        facts["filename"] = item.Name; if (item.Version.Length > 0) facts[_selected == "resourcepacks" ? "packFormat" : "version"] = item.Version;
        facts["size"] = item.Size is { } bytes ? FormatContentSize(bytes) : ContentTerm("folder");
        facts["modified"] = new DateTime(item.ModifiedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.CurrentCulture);
        if (_management?.Pages.FirstOrDefault(page => page.Id == _selected)?.Directory is { } directory) facts["directory"] = directory;
        foreach (var old in _contentDetailFacts.Where(pair => !facts.ContainsKey(pair.Key)).ToArray()) { RemoveContentView(old.Value); _contentDetailFacts.Remove(old.Key); }
        List<XsrUiEntityId> order = [];
        foreach (var (key, value) in facts)
        {
            if (!_contentDetailFacts.TryGetValue(key, out var root))
            {
                var text = AppendContentFact(slot, key, value); root = _shell.Tree.Parent(text); _contentDetailFacts[key] = root;
            }
            else ReloadContent(root, ("value", value), ("label", ContentTerm("fact." + key)));
            order.Add(root);
        }
        _dynamic.Reorder(slot, order);
    }
    private void BindContentDetailToggle(Dictionary<string, XsrUiEntityId> nodes, InstanceContentEntry item)
    {
        string key = ContentFileKey(item.Name);
        var enabled = nodes["ManagementModToggle." + key + ".true"]; var disabled = nodes["ManagementModToggle." + key + ".false"];
        _shell.Tree.GetComponent<XsrUiSegmentedTrack>(nodes["ManagementModToggle." + key])!.Selected = item.Enabled == true ? enabled : disabled;
        RegisterContentAction(enabled, () => { if (item.Enabled == false) ToggleMod(item); }); RegisterContentAction(disabled, () => { if (item.Enabled == true) ToggleMod(item); });
    }

    private string FormatContentSize(long bytes) => bytes >= 1024 * 1024
        ? ContentFormat("SizeMb", ("value", (bytes / (1024d * 1024)).ToString("N1", System.Globalization.CultureInfo.CurrentCulture))) : ContentFormat("SizeKb", ("value", (bytes / 1024d).ToString("N1", System.Globalization.CultureInfo.CurrentCulture)));

    private static string ResourcePackTitle(InstanceContentEntry item) =>
        !item.IsDirectory && item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? item.Name[..^4] : item.Name;

    private static bool IsContentFormatCode(char code) => "0123456789abcdefklmnor".Contains(char.ToLowerInvariant(code));

    private static string StripContentFormatting(string value)
    {
        if (!value.Contains('§')) return value;
        var text = new StringBuilder();
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '§' && index + 1 < value.Length && IsContentFormatCode(value[index + 1])) { index++; continue; }
            text.Append(value[index]);
        }
        return text.ToString();
    }

    // Legacy Minecraft formatting is presentation only; raw control codes never become labels.
    private void ApplyContentFormatting(XsrUiEntityId entity, string value)
    {
        XsrUiColor foreground = _shell.Tree.GetComponent<XsrUiVisualStyle>(entity)!.Foreground;
        XsrUiColor color = foreground;
        bool bold = false, italic = false, underline = false, strike = false;
        var plain = new StringBuilder();
        List<XsrUiTextRun> runs = [];
        int start = 0;
        void Flush()
        {
            if (plain.Length > start) runs.Add(new(start, plain.Length - start, color, bold, italic, underline, strike));
            start = plain.Length;
        }
        for (int index = 0; index < value.Length && index < 4096; index++)
        {
            char character = value[index];
            if (character != '§' || index + 1 >= value.Length || !IsContentFormatCode(value[index + 1])) { plain.Append(character); continue; }
            Flush();
            char code = char.ToLowerInvariant(value[++index]);
            int digit = "0123456789abcdef".IndexOf(code);
            if (digit >= 0)
            {
                int bright = (digit & 8) != 0 ? 85 : 0;
                color = new((byte)(((digit & 4) != 0 ? 170 : 0) + bright), (byte)(((digit & 2) != 0 ? 170 : 0) + bright), (byte)(((digit & 1) != 0 ? 170 : 0) + bright));
                if (digit == 6) color = new(255, 170, 0);
                bold = italic = underline = strike = false;
            }
            else if (code == 'l') bold = true;
            else if (code == 'o') italic = true;
            else if (code == 'n') underline = true;
            else if (code == 'm') strike = true;
            else if (code == 'r') { color = foreground; bold = italic = underline = strike = false; }
        }
        Flush();
        var text = _shell.Tree.GetComponent<XsrUiText>(entity)!;
        text.Content = plain.ToString(); text.Localize = false; text.Runs = runs.AsReadOnly();
        _shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
    }
}
