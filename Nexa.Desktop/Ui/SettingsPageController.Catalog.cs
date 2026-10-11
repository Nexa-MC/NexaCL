using System.Globalization;
using Nexa.Services.Settings;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private readonly Dictionary<XsrUiEntityId, (string Template, Dictionary<string, string> Data)> _catalogViews = [];
    private readonly Dictionary<XsrUiEntityId, XsrUiEntityId> _inheritRows = [], _autoModeViews = [];
    private static string Flag(bool value) => value ? "true" : "false";

    private XsrUiEntityId AppendCatalog(XsrUiEntityId slot, string template, Dictionary<string, string> data)
    {
        var root = _dynamic.Append(slot, DesktopMarkup.Dynamic(template, data));
        _catalogViews[root] = (template, data);
        return root;
    }

    private void ReloadCatalog(XsrUiEntityId root, params (string Key, string Value)[] values)
    {
        var view = _catalogViews[root];
        bool changed = false;
        foreach (var (key, value) in values)
            if (view.Data.GetValueOrDefault(key) != value) { view.Data[key] = value; changed = true; }
        if (changed) _dynamic.Reload(root, DesktopMarkup.Dynamic(view.Template, view.Data));
    }

    private Dictionary<string, XsrUiEntityId> CatalogNodes(XsrUiEntityId root)
    {
        Dictionary<string, XsrUiEntityId> nodes = [];
        _shell.Tree.Walk(root, entity => { nodes[_shell.Tree.Name(entity)] = entity; return true; });
        return nodes;
    }

    private void BuildRow(XsrUiEntityId parent, SettingsCatalogEntry entry)
    {
        string key = entry.SettingKey ?? entry.Id;
        bool enabled = entry.Availability == SettingsCapabilityAvailability.Available && entry.Definition is not null;
        bool required = key == "diagnostics.telemetry" && TelemetryRequired;
        bool inherit = enabled && !required && _instanceDirectory is not null && key != "instance.development-mode";
        bool multiline = key is "game.pre-launch" or "game.post-exit" or "game.environment" or "game.classpath-head" or "game.jvm" or "game.arguments";
        string label = key == "diagnostics.telemetry" ? "诊断信息 · 用户体验改进计划" : DisplayLabel(entry.Label);
        string? hint = SettingHint(key);
        var row = AppendCatalog(parent, "SettingsCatalogRow.d.nxml", new()
        {
            ["row"] = "SettingsRow." + entry.Id,
            ["labelKey"] = "SettingsLabel." + entry.Id,
            ["label"] = label,
            ["hint"] = hint ?? "",
            ["hasHint"] = Flag(hint is not null),
            ["labelStyle"] = "settings-catalog-labels" + (key == "diagnostics.telemetry" ? " settings-catalog-labels-wide" : "")
                + (multiline ? " settings-catalog-labels-top" : ""),
            ["inheritKey"] = "SettingsInherit." + key,
            ["inheritVisible"] = Flag(inherit),
            ["inheritLabel"] = "继承",
            ["controls"] = "SettingsControlSlot",
        });
        var nodes = CatalogNodes(row);
        var controls = nodes["SettingsControlSlot"];
        if (required) { _dynamic.Append(controls, DesktopMarkup.Dynamic("SettingsCatalogRequired.d.nxml")); return; }
        if (!enabled)
        {
            string reason = entry.Availability switch
            {
                SettingsCapabilityAvailability.PlatformUnsupported => "此平台不支持",
                SettingsCapabilityAvailability.DependencyMissing => "缺少依赖",
                SettingsCapabilityAvailability.TemporarilyUnavailable => "暂不可用",
                _ => "尚未可用",
            };
            _dynamic.Append(controls, DesktopMarkup.Dynamic("SettingsCatalogUnavailable.d.nxml", new Dictionary<string, string>
            { ["reason"] = _shell.Renderer.LocalizeText(reason), ["accessible"] = _shell.Renderer.LocalizeText(label) + ", " + _shell.Renderer.LocalizeText(reason) }));
            return;
        }
        if (inherit)
        {
            var button = AppendCatalog(nodes["SettingsInheritSlot"], "SettingsCatalogInherit.d.nxml", new()
            { ["key"] = "SettingsInherit." + key, ["inheritLabel"] = "继承" });
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
            ["placeholder"] = key switch { "java.runtime" => "自动选择，或输入 Java 可执行文件路径", "game.memory" => "自动分配，或输入 MiB", _ => entry.Label },
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
            _shell.Tree.SetComponent(thumb, new XsrUiTransition());
            _shell.Tree.SetComponent(track, new XsrUiSegmentedTrack(thumb));
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
