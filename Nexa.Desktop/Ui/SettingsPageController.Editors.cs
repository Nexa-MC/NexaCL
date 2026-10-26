using System.Globalization;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

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
        string key = "SettingsSelector." + entry.SettingKey;
        var root = AppendCatalog(parent, "SettingsCatalogSelector.d.nxml", new()
        { ["key"] = key, ["thumb"] = "SettingsSelectorThumb." + entry.SettingKey, ["label"] = DisplayLabel(entry.Label) });
        var track = CatalogNodes(root)[key];
        Dictionary<XsrUiEntityId, string> options = [];
        string[] values = entry.SettingKey == "general.region" ? ["auto", "follow-language", "zh-CN", "zh-TW", "en-US"]
            : entry.Definition!.Kind == SettingsValueKind.Boolean ? entry.InvertBoolean ? ["false", "true"] : ["true", "false"]
            : entry.Definition.Choices.Split('|');
        foreach (string value in values)
        {
            string label = value switch { "true" => CatalogCopy("catalog.copy.8da97ddda990e7c4"), "false" => CatalogCopy("catalog.copy.3fd47edce45b3603"), "fullscreen" => CatalogCopy("catalog.copy.22ea3f47fee80536"), "windowed" => CatalogCopy("catalog.copy.9efe01f647d67d91"), "auto" => CatalogCopy("catalog.copy.217cfe7db1e3d10a"), "zh-Hans" => CatalogCopy("catalog.copy.9937e365aaf3a085"), "zh-Hant" => CatalogCopy("catalog.copy.1c95ab9b144ea2f5"), "en" => CatalogCopy("catalog.english"), _ => value };
            if (entry.InvertBoolean) label = value == "true" ? CatalogCopy("catalog.copy.3fd47edce45b3603") : CatalogCopy("catalog.copy.8da97ddda990e7c4");
            if (entry.SettingKey == "general.region") label = value switch
            { "auto" => CatalogCopy("catalog.copy.480e05a21db2241e"), "follow-language" => CatalogCopy("catalog.copy.3d13868593ae4eeb"), "zh-CN" => CatalogCopy("catalog.copy.51536645bb38a422"), "zh-TW" => CatalogCopy("catalog.copy.c20756588291ca63"), "en-US" => CatalogCopy("catalog.copy.58857e5318f3090b"), _ => value };
            if (entry.SettingKey is "network.game-source" or "network.resource-source") label = value switch
            { "official-first" => CatalogCopy("catalog.copy.db913a6827ebd709"), "mirrors-first" => CatalogCopy("catalog.copy.294e0dedc919b615"), "official-only" => CatalogCopy("catalog.copy.07724494618cd221"), "follow-request" => CatalogCopy("catalog.copy.f26386f253b63842"), _ => value };
            if (entry.SettingKey == "general.startup-page") label = value switch
            { "launch" => CatalogCopy("catalog.copy.56410fc65314dfb5"), "install" => CatalogCopy("catalog.copy.e8f88f51ccb085ea"), "resources" => CatalogCopy("catalog.copy.daa6d02a55bc3de9"), "settings" => CatalogCopy("catalog.copy.df3d58c7d84b85f2"), "java" => CatalogCopy("catalog.java"), "storage" => CatalogCopy("catalog.copy.a3434acddb75d8fb"), "about" => CatalogCopy("catalog.copy.52d25a9e30ba94f1"), "tasks" => CatalogCopy("catalog.copy.5253040db8643c85"), _ => value };
            if (entry.SettingKey == "network.ip-stack") label = value switch
            { "auto" => CatalogCopy("catalog.copy.2b1a4c391d4c74a1"), "ipv4" => CatalogCopy("catalog.copy.3d7d10b1c631978c"), "ipv6" => CatalogCopy("catalog.copy.f0ef39242684353e"), _ => value };
            if (entry.SettingKey == "game.gpu-preference") label = value switch
            { "auto" => CatalogCopy("catalog.copy.217cfe7db1e3d10a"), "secondary" => CatalogCopy("catalog.copy.08d99de9033a468b"), _ => value };
            if (entry.SettingKey == "game.renderer") label = value switch
            { "auto" => CatalogCopy("catalog.copy.217cfe7db1e3d10a"), "mesa-software" => CatalogCopy("catalog.copy.d52065e3b606194f"), _ => value };
            if (entry.SettingKey == "game.process-quality") label = CatalogCopy("catalog.scheduling." + value);
            if (entry.SettingKey == "game.process-priority") label = value switch
            { "normal" => CatalogCopy("catalog.copy.296de0e31f8c22d9"), "below-normal" => CatalogCopy("catalog.copy.552f8f8b1402dc6d"), "above-normal" => CatalogCopy("catalog.copy.dfbad24e7f4a9cb8"), "high" => CatalogCopy("catalog.copy.b1c27820fec23edb"), "real-time" => CatalogCopy("catalog.copy.c69ec2ffb0db8ca4"), _ => value };
            if (entry.SettingKey == "game.launcher-visibility") label = value switch
            { "keep" => CatalogCopy("catalog.copy.1d5d4fd4747e8a2a"), "minimize" => CatalogCopy("catalog.copy.ac29e57a46f41c30"), "hide" => CatalogCopy("catalog.copy.b6fc09bdfd4b658e"), "hide-and-close" => CatalogCopy("catalog.copy.ffe0b3cee830cabc"), _ => value };
            if (entry.SettingKey == "game.default-isolation") label = value switch
            { "none" => CatalogCopy("catalog.copy.3e3040bf2ce21dff"), "loaders" => CatalogCopy("catalog.copy.2825e15e15252b12"), "non-release" => CatalogCopy("catalog.copy.cfdfd2a8cc340d0a"), "loaders-or-non-release" => CatalogCopy("catalog.copy.59c50d3852272e44"), "all" => CatalogCopy("catalog.copy.25efb3d5b616c16a"), _ => value };
            if (entry.SettingKey == "updates.channel") label = value switch
            { "build" => CatalogCopy("catalog.copy.8aefb4ba27753972"), "stable" => CatalogCopy("catalog.copy.93a48223a516e074"), "alpha" => CatalogCopy("catalog.alpha"), "beta" => CatalogCopy("catalog.beta"), "ci" => CatalogCopy("catalog.ci"), _ => value };
            if (entry.SettingKey == "diagnostics.log-level") label = value switch
            { "auto" => CatalogCopy("catalog.copy.7eb336e42cb5076b"), "0" => CatalogCopy("catalog.copy.1c677c4a263bb9c4"), "1" => CatalogCopy("catalog.copy.a8b7a4480407ac8a"), "2" => CatalogCopy("catalog.copy.e7028601e7da793d"), "3" => CatalogCopy("catalog.copy.a3ba53fadbaed9d7"), "4" => CatalogCopy("catalog.copy.f58a2874a36ed33d"), _ => value };
            if (entry.SettingKey == "appearance.theme-mode") label = value switch
            { "2" => CatalogCopy("catalog.copy.217cfe7db1e3d10a"), "0" => CatalogCopy("catalog.copy.aa0819dfc4d8d782"), "1" => CatalogCopy("catalog.copy.a6b75d0680322a61"), _ => value };
            if (entry.SettingKey == "appearance.accent") label = value switch
            { "blue" => CatalogCopy("catalog.copy.67035e6b7f82acdb"), "purple" => CatalogCopy("catalog.copy.2ed9c6fabac2013a"), "green" => CatalogCopy("catalog.copy.30c83dc25b12c32c"), "orange" => CatalogCopy("catalog.copy.aa1e899dd35771b0"), _ => value };
            if (entry.SettingKey == "java.vendor") label = value switch
            { "" => CatalogCopy("catalog.copy.7eb336e42cb5076b"), "EclipseTemurin" => CatalogCopy("catalog.vendor.temurin"), "IbmSemeru" => CatalogCopy("catalog.vendor.semeru"), "GraalVmCommunity" => CatalogCopy("catalog.vendor.graalvm"), "OpenJdk" => CatalogCopy("catalog.vendor.openjdk"), "TencentKona" => CatalogCopy("catalog.copy.57ec7620cca45f66"), "Dragonwell" => CatalogCopy("catalog.copy.99b1486ff2b222d3"), "Unknown" => CatalogCopy("catalog.copy.d2909f1647e7c891"), _ => value };
            bool nativeName = entry.SettingKey == "general.language" && value != "auto";
            double optionWidth = CatalogCaptionWidth("option", nativeName ? label : _shell.Renderer.LocalizeText(label));
            var option = CatalogOption(track, "SettingsOption." + entry.SettingKey + "." + value, label, !nativeName, optionWidth);
            options[option] = value;
        }
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
                        var custom = CatalogOption(selector.Track, "SettingsOption.general.region." + raw, name, false, CatalogCaptionWidth("option", name));
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
            foreach (var option in selector.Options.Keys)
            {
                var text = _shell.Tree.GetComponent<XsrUiText>(option)!;
                double desired = CatalogCaptionWidth("option", text.Localize ? _shell.Renderer.LocalizeText(text.Content) : text.Content);
                ReloadCatalog(option, ("width", desired.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
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
                ["label"] = ProductPresentationText.Format("IndexedCaption", _shell.Renderer.LocalizeText,
                    ("label", _shell.Renderer.LocalizeText(editor.Entry.Label)), ("index", (i + 1).ToString(CultureInfo.CurrentCulture))),
                ["removeLabel"] = ProductPresentationText.Format("IndexedCaption", _shell.Renderer.LocalizeText,
                    ("label", _shell.Renderer.LocalizeText(CatalogCopy("catalog.copy.50a1b81a61ad3dd7"))), ("index", (i + 1).ToString(CultureInfo.CurrentCulture))),
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
