using Nexa.Services.Settings;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId ArgumentAdd = XsrSemanticId.Parse("ui.settings.argument.add");
    private static readonly XsrSemanticId ArgumentRemove = XsrSemanticId.Parse("ui.settings.argument.remove");
    private sealed record ShiftSelector(Editor Editor, XsrUiEntityId Track, Dictionary<XsrUiEntityId, string> Options);
    private sealed class ArgumentEditor(SettingsCatalogEntry entry, XsrUiEntityId rows)
    {
        public SettingsCatalogEntry Entry { get; } = entry;
        public XsrUiEntityId Rows { get; } = rows;
        public List<XsrUiEntityId> Inputs { get; } = [];
        public bool Initialized { get; set; }
    }
    private readonly Dictionary<XsrUiEntityId, ShiftSelector> _selectors = [];
    private readonly Dictionary<XsrUiEntityId, ArgumentEditor> _argumentEditors = [];
    private readonly Dictionary<XsrUiEntityId, (ArgumentEditor Editor, int Index)> _argumentActions = [];

    private void BuildShiftSelector(XsrUiEntityId parent, SettingsCatalogEntry entry)
    {
        var track = AppendCatalog(parent, "SettingsCatalogSelector.d.nxml", new()
        { ["key"] = "SettingsSelector." + entry.SettingKey, ["label"] = DisplayLabel(entry.Label), ["width"] = "0" });
        var thumb = _dynamic.Append(track, DesktopMarkup.Dynamic("SettingsCatalogThumb.d.nxml", new Dictionary<string, string>
        { ["key"] = "SettingsSelectorThumb." + entry.SettingKey }));
        _shell.Tree.SetComponent(thumb, new XsrUiTransition());
        _shell.Tree.SetComponent(track, new XsrUiSegmentedTrack(thumb));
        _shell.Tree.SetComponent(track, new XsrUiScrollGesture());
        Dictionary<XsrUiEntityId, string> options = [];
        string[] values = entry.SettingKey == "general.region" ? ["auto", "follow-language", "zh-CN", "zh-TW", "en-US"]
            : entry.Definition!.Kind == SettingsValueKind.Boolean ? entry.InvertBoolean ? ["false", "true"] : ["true", "false"]
            : entry.Definition.Choices.Split('|');
        double width = 0;
        foreach (string value in values)
        {
            string label = value switch { "true" => "开启", "false" => "关闭", "fullscreen" => "全屏", "windowed" => "窗口", "auto" => "跟随系统", "zh-Hans" => "简体中文", "zh-Hant" => "繁體中文", "en" => "English", _ => value };
            if (entry.InvertBoolean) label = value == "true" ? "关闭" : "开启";
            if (entry.SettingKey == "general.region") label = value switch
            { "auto" => "系统区域", "follow-language" => "界面语言", "zh-CN" => "中国大陆", "zh-TW" => "中国台湾", "en-US" => "美国", _ => value };
            if (entry.SettingKey is "network.game-source" or "network.resource-source") label = value switch
            { "official-first" => "官方优先", "mirrors-first" => "镜像优先", "official-only" => "仅官方", "follow-request" => "跟随页面", _ => value };
            if (entry.SettingKey == "general.startup-page") label = value switch
            { "launch" => "启动", "install" => "安装", "resources" => "资源", "settings" => "设置", "java" => "Java", "storage" => "存储", "about" => "关于", "tasks" => "任务", _ => value };
            if (entry.SettingKey == "network.ip-stack") label = value switch
            { "auto" => "系统顺序", "ipv4" => "IPv4 优先", "ipv6" => "IPv6 优先", _ => value };
            if (entry.SettingKey == "game.gpu-preference") label = value switch
            { "auto" => "跟随系统", "secondary" => "次级 GPU（Mesa）", _ => value };
            if (entry.SettingKey == "game.renderer") label = value switch
            { "auto" => "跟随系统", "mesa-software" => "Mesa 软件渲染", _ => value };
            if (entry.SettingKey == "game.process-priority") label = value switch
            { "normal" => "正常", "below-normal" => "较低", "above-normal" => "较高", "high" => "高", "real-time" => "实时", _ => value };
            if (entry.SettingKey == "game.launcher-visibility") label = value switch
            { "keep" => "保持显示", "minimize" => "最小化", "hide" => "隐藏后恢复", "hide-and-close" => "游戏退出后关闭", _ => value };
            if (entry.SettingKey == "game.default-isolation") label = value switch
            { "none" => "不隔离", "loaders" => "加载器版本", "non-release" => "非正式版", "loaders-or-non-release" => "加载器与非正式版", "all" => "全部版本", _ => value };
            if (entry.SettingKey == "updates.channel") label = value switch
            { "build" => "跟随当前版本", "stable" => "正式版", "alpha" => "Alpha", "beta" => "Beta", "ci" => "CI", _ => value };
            if (entry.SettingKey == "diagnostics.log-level") label = value switch
            { "auto" => "自动", "0" => "仅错误", "1" => "警告", "2" => "信息", "3" => "调试", "4" => "详细跟踪", _ => value };
            if (entry.SettingKey == "appearance.theme-mode") label = value switch
            { "2" => "跟随系统", "0" => "浅色", "1" => "深色", _ => value };
            if (entry.SettingKey == "appearance.accent") label = value switch
            { "blue" => "蓝色", "purple" => "紫色", "green" => "绿色", "orange" => "橙色", _ => value };
            if (entry.SettingKey == "java.vendor") label = value switch
            { "" => "自动", "EclipseTemurin" => "Temurin", "IbmSemeru" => "IBM Semeru", "GraalVmCommunity" => "GraalVM", "OpenJdk" => "OpenJDK", "TencentKona" => "腾讯 Kona", "Dragonwell" => "龙井 Java", "Unknown" => "其他", _ => value };
            bool nativeName = entry.SettingKey == "general.language" && value != "auto";
            double optionWidth = Math.Max(48, (nativeName ? label : _shell.Renderer.LocalizeText(label)).Sum(character => character > 127 ? 12 : 7) + 24);
            var option = CatalogOption(track, "SettingsOption." + entry.SettingKey + "." + value, label, !nativeName, optionWidth);
            options[option] = value; width += optionWidth;
        }
        ReloadCatalog(track, ("width", width.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var editor = new Editor(entry, default, options.Keys.First(), default);
        _editors[editor.Button] = editor;
        _selectors[editor.Button] = new(editor, track, options);
    }

    private void UpdateShiftSelectors()
    {
        foreach (var selector in _selectors.Values)
        {
            string raw = _values?.Values.FirstOrDefault(item => item.Key == selector.Editor.Entry.SettingKey)?.Value.Value ?? selector.Options.Values.First();
            if (selector.Editor.Entry.SettingKey == "general.region")
            {
                if (raw == "ui-language") raw = "follow-language";
                if (!selector.Options.Values.Contains(raw, StringComparer.Ordinal))
                {
                    // Existing custom cultures stay reachable; the form never substitutes a preset.
                    try
                    {
                        string name = System.Globalization.CultureInfo.GetCultureInfo(raw).NativeName;
                        var custom = CatalogOption(selector.Track, "SettingsOption.general.region." + raw, name, false, 48);
                        selector.Options[custom] = raw;
                        _shell.Tree.MarkDirty(selector.Track, XsrUiDirtyKinds.Layout);
                    }
                    catch (System.Globalization.CultureNotFoundException) { }
                }
            }
            foreach (var option in selector.Options)
            {
                bool selected = option.Value == raw;
                _choices[option.Key] = (selector.Editor, option.Value);
                ReloadCatalog(option.Key, ("selected", Flag(selected)),
                    ("style", selected ? "settings-catalog-option settings-catalog-option-selected" : "settings-catalog-option"));
                if (selected) _shell.Tree.GetComponent<XsrUiSegmentedTrack>(selector.Track)!.Selected = option.Key;
            }
            double width = 0;
            foreach (var option in selector.Options.Keys)
            {
                var text = _shell.Tree.GetComponent<XsrUiText>(option)!;
                double desired = Math.Max(48, (text.Localize ? _shell.Renderer.LocalizeText(text.Content) : text.Content).Sum(character => character > 127 ? 12 : 7) + 24);
                ReloadCatalog(option, ("width", desired.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                width += desired;
            }
            ReloadCatalog(selector.Track, ("width", width.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private void BuildArgumentEditor(XsrUiEntityId parent, SettingsCatalogEntry entry)
    {
        var root = _dynamic.Append(parent, DesktopMarkup.Dynamic("SettingsArguments.d.nxml", new Dictionary<string, string>
        {
            ["panel"] = "SettingsArguments." + entry.SettingKey,
            ["rows"] = "SettingsArgumentRows." + entry.SettingKey,
            ["apply"] = "SettingsEdit." + entry.SettingKey,
            ["add"] = "SettingsArgumentAdd." + entry.SettingKey,
        }));
        var nodes = CatalogNodes(root);
        var rows = nodes["SettingsArgumentRows." + entry.SettingKey];
        var apply = nodes["SettingsEdit." + entry.SettingKey]; var add = nodes["SettingsArgumentAdd." + entry.SettingKey];
        var editor = new ArgumentEditor(entry, rows);
        _argumentEditors[apply] = editor;
        _argumentActions[add] = (editor, -1);
        _editors[apply] = new(entry, default, apply, default);
        BuildArgumentRows(editor, [""]);
    }

    private string[] ReadArgumentDrafts(ArgumentEditor editor) => editor.Inputs
        .Select(input => _shell.Tree.GetComponent<XsrUiTextInput>(input)!.ReadDraft()).ToArray();

    private void BuildArgumentRows(ArgumentEditor editor, IReadOnlyList<string> values)
    {
        foreach (var key in _argumentActions.Where(pair => pair.Value.Editor == editor && pair.Value.Index >= 0).Select(pair => pair.Key).ToArray()) _argumentActions.Remove(key);
        _dynamic.Clear(editor.Rows);
        editor.Inputs.Clear();
        for (int i = 0; i < Math.Max(1, values.Count); i++)
        {
            string inputKey = "SettingsArgument." + editor.Entry.SettingKey + "." + i;
            string removeKey = "SettingsArgumentRemove." + editor.Entry.SettingKey + "." + i;
            var row = _dynamic.Append(editor.Rows, DesktopMarkup.Dynamic("SettingsArgumentRow.d.nxml", new Dictionary<string, string>
            {
                ["input"] = inputKey,
                ["remove"] = removeKey,
                ["label"] = _shell.Renderer.LocalizeText(editor.Entry.Label) + " " + (i + 1),
                ["removeLabel"] = _shell.Renderer.LocalizeText("删除参数") + " " + (i + 1),
            }));
            var nodes = CatalogNodes(row); var input = nodes[inputKey]; var remove = nodes[removeKey];
            _shell.Renderer.SetTextInputValue(input, i < values.Count ? values[i] : "");
            editor.Inputs.Add(input);
            _argumentActions[remove] = (editor, i);
        }
        _shell.Tree.MarkDirty(editor.Rows, XsrUiDirtyKinds.Layout);
    }

    private void UpdateArgumentEditors()
    {
        foreach (var editor in _argumentEditors.Values)
        {
            if (editor.Initialized || _values is null) continue;
            var value = _values.Values.First(item => item.Key == editor.Entry.SettingKey);
            BuildArgumentRows(editor, value.ArgumentRows);
            editor.Initialized = true;
        }
    }

    private void HandleArgumentIntent(DesktopUiIntent intent)
    {
        if (!_argumentActions.TryGetValue(intent.Source, out var action)) return;
        var values = ReadArgumentDrafts(action.Editor).ToList();
        if (intent.Command == ArgumentAdd) values.Add("");
        else if (action.Index >= 0 && action.Index < values.Count) values.RemoveAt(action.Index);
        BuildArgumentRows(action.Editor, values);
        action.Editor.Initialized = true;
        _shell.Renderer.Focus(action.Editor.Inputs[intent.Command == ArgumentAdd ? action.Editor.Inputs.Count - 1 : Math.Min(action.Index, action.Editor.Inputs.Count - 1)]);
    }
}
