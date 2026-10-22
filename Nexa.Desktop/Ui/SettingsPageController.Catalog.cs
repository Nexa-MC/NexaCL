using System.Globalization;
using Nexa.Services.Settings;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private readonly Dictionary<XsrUiEntityId, (string Template, Dictionary<string, string> Data)> _catalogViews = [];
    private readonly Dictionary<XsrUiEntityId, XsrUiEntityId> _inheritRows = [], _autoModeViews = [];
    private static string Flag(bool value) => value ? "true" : "false";
    private static string CatalogCopy(string key) => ProductPresentationText.Raw(key);
    private XsrUiEntityId AppendSettingGroup(IEnumerable<SettingsCatalogEntry> entries, string label)
    {
        var rows = entries.ToArray();
        var body = AppendSettingsForm(_sections, "SettingsGroup." + rows[0].Id, DisplayLabel(label));
        for (int i = 0; i < rows.Length; i++)
        {
            BuildRow(body, rows[i]);
            if (i + 1 < rows.Length) _dynamic.Append(body, DesktopMarkup.Dynamic("SettingsCatalogDivider.d.nxml"));
        }
        return body;
    }
    private static readonly Lazy<Dictionary<string, Nexa.Xsr.Renderer.Nxml.NxmlIrNode>> CatalogMetrics = new(() =>
        DesktopMarkup.Load("SettingsCatalogMetrics.nxml").Root.Children.ToDictionary(node => node.Key!, StringComparer.Ordinal));

    private static double CatalogCaptionWidth(string kind, string caption)
    {
        var metric = CatalogMetrics.Value[kind];
        return Math.Max(metric.MinWidth!.Value,
            caption.Sum(character => character > 127 ? metric.Height!.Value : metric.Width!.Value) + metric.Padding.Left);
    }

    private XsrUiEntityId AppendCatalog(XsrUiEntityId slot, string template, Dictionary<string, string> data)
    {
        var root = _dynamic.Append(slot, DesktopMarkup.Dynamic(template, data));
        _catalogViews[root] = (template, data);
        return root;
    }

    private void ReloadCatalog(XsrUiEntityId root, params (string Key, string Value)[] values)
    {
        var view = _catalogViews[root];
        var candidate = new Dictionary<string, string>(view.Data, StringComparer.Ordinal);
        bool changed = false;
        foreach (var (key, value) in values)
            if (candidate.GetValueOrDefault(key) != value) { candidate[key] = value; changed = true; }
        if (changed)
        {
            _dynamic.Reload(root, DesktopMarkup.Dynamic(view.Template, candidate));
            _catalogViews[root] = (view.Template, candidate);
        }
    }

    private Dictionary<string, XsrUiEntityId> CatalogNodes(XsrUiEntityId root)
    {
        Dictionary<string, XsrUiEntityId> nodes = [];
        _shell.Tree.Walk(root, entity => { nodes[_shell.Tree.Name(entity)] = entity; return true; });
        return nodes;
    }

    private bool IsEditableSetting(SettingsCatalogEntry entry) => entry.Kind == SettingsCatalogEntryKind.Setting
        && entry.Availability == SettingsCapabilityAvailability.Available && entry.Definition is not null
        && !(entry.SettingKey == "diagnostics.telemetry" && TelemetryRequired);

    private void BuildRow(XsrUiEntityId parent, SettingsCatalogEntry entry)
    {
        if (!IsEditableSetting(entry)) return;
        string key = entry.SettingKey ?? entry.Id;
        bool inherit = _instanceDirectory is not null && key != "instance.development-mode";
        bool multiline = key is "game.pre-launch" or "game.post-exit" or "game.environment" or "game.classpath-head" or "game.jvm" or "game.arguments";
        string label = key == "diagnostics.telemetry" ? CatalogCopy("catalog.copy.dd29c4ef967a202e") : DisplayLabel(entry.Label);
        var row = AppendCatalog(parent, "SettingsCatalogRow.d.nxml", new()
        {
            ["row"] = "SettingsRow." + entry.Id,
            ["labelKey"] = "SettingsLabel." + entry.Id,
            ["label"] = label,
            ["labelStyle"] = "settings-catalog-labels" + (multiline ? " settings-catalog-labels-top" : ""),
            ["inheritKey"] = "SettingsInherit." + key,
            ["inheritVisible"] = Flag(inherit),
            ["inheritLabel"] = CatalogCopy("catalog.copy.49f1e2b9d18b55b5"),
            ["controls"] = "SettingsControlSlot",
        });
        var nodes = CatalogNodes(row);
        var controls = nodes["SettingsControlSlot"];
        if (inherit)
        {
            var button = AppendCatalog(nodes["SettingsInheritSlot"], "SettingsCatalogInherit.d.nxml", new()
            { ["key"] = "SettingsInherit." + key, ["inheritLabel"] = CatalogCopy("catalog.copy.49f1e2b9d18b55b5") });
            _inheritButtons[button] = key; _inheritRows[button] = button;
        }
        if (key is "game.pre-launch" or "game.post-exit" or "game.environment" or "game.classpath-head")
        { BuildHookEditor(controls, entry); return; }
        if (key is "game.jvm" or "game.arguments") { BuildArgumentEditor(controls, entry); return; }
        var definition = entry.Definition!;
        if (definition.Kind is SettingsValueKind.Enum or SettingsValueKind.Boolean || key == "general.region")
        { BuildShiftSelector(controls, entry); return; }
        var inputRoot = AppendCatalog(controls, "SettingsCatalogInput.d.nxml", new()
        {
            ["input"] = "SettingsInput." + key,
            ["label"] = entry.Label,
            ["placeholder"] = key switch { "java.runtime" => CatalogCopy("catalog.copy.d8d98ef47e23d67c"), "game.memory" => CatalogCopy("catalog.copy.17fc89da68477c26"), _ => entry.Label },
            ["maximum"] = key == "game.wrapper" ? "32768" : "2048",
            ["apply"] = "SettingsEdit." + key,
            ["mode"] = "SettingsMode." + key,
            ["automatic"] = Flag(definition.SupportsAuto),
            ["auto"] = "SettingsAuto." + key,
            ["manual"] = "SettingsManual." + key,
            ["autoSelected"] = "false",
            ["manualSelected"] = "false",
        });
        nodes = CatalogNodes(inputRoot);
        var apply = nodes["SettingsEdit." + key];
        var editor = new Editor(entry, nodes["SettingsInput." + key], apply, default);
        _editors[apply] = editor;
        if (definition.SupportsAuto)
        {
            var track = nodes["SettingsMode." + key]; var thumb = nodes["SettingsModeThumb"];
            var automatic = nodes["SettingsAuto." + key]; var manual = nodes["SettingsManual." + key];
            _choices[automatic] = (editor, "auto"); _choices[manual] = (editor, "__manual__");
            _autoModes[track] = (editor, automatic, manual); _autoModeViews[track] = inputRoot;
        }
    }

    private XsrUiEntityId CatalogOption(XsrUiEntityId track, string key, string label, bool localize, double width)
        => AppendCatalog(track, "SettingsCatalogOption.d.nxml", new()
        {
            ["key"] = key,
            ["label"] = label,
            ["localize"] = Flag(localize),
            ["selected"] = "false",
            ["width"] = width.ToString(CultureInfo.InvariantCulture),
            ["style"] = "settings-catalog-option",
        });
}
