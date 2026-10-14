using System.Buffers;
using System.Globalization;
using Nexa.Services.Themes;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly SearchValues<char> ThemeHexDigits = SearchValues.Create("0123456789abcdefABCDEF");
    private void ProjectThemeOptions(ThemeCatalogueEntry? source, DesktopThemeSettingsSnapshot? snapshot)
    {
        var slot = CatalogNodes(_themeOptionsRoot)["Theme.OptionRows"];
        var definitions = source?.Manifest.Options ?? [];
        var wanted = definitions.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _themeOptionEditors.Where(pair => pair.Value.Source != source?.SourceIdentity
            || !wanted.Contains(pair.Key) || definitions.First(item => item.Id == pair.Key).Kind != pair.Value.Definition.Kind).Select(pair => pair.Key).ToArray())
        {
            _themeOptionInputs.Remove(_themeOptionEditors[stale].Input);
            _dynamic.Remove(_themeOptionEditors[stale].Root); _themeOptionEditors.Remove(stale);
        }
        if (source is null) return;
        var state = snapshot?.Current.Options.FirstOrDefault(item => item.SourceIdentity == source.SourceIdentity);
        Dictionary<string, ThemeOptionValue> values = new(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            ThemeOptionValue effective = state?.Values.GetValueOrDefault(definition.Id) ?? definition.Default;
            if (_themeOptionEditors.TryGetValue(definition.Id, out var existing) && !_themeDrafts.ContainsKey((source.SourceIdentity, definition.Id)))
            {
                if (existing.Input.IsAssigned && _shell.Tree.IsAlive(existing.Input))
                    _shell.Renderer.SetTextInputValue(existing.Input, effective.Value);
            }
            values[definition.Id] = _themeDrafts.GetValueOrDefault((source.SourceIdentity, definition.Id)) ?? effective;
        }
        List<XsrUiEntityId> order = [];
        string? previousVisibleGroup = null;
        foreach (var definition in definitions)
        {
            string prefix = "Theme.Option." + ThemeIdentityKey(source.SourceIdentity + "/" + definition.Id);
            ThemeOptionValue effective = state?.Values.GetValueOrDefault(definition.Id) ?? definition.Default;
            ThemeOptionValue value = values[definition.Id];
            bool visible = ThemeOptionVisible(definition.VisibleWhen, values);
            var data = ThemeOptionData(definition, prefix, value, visible);
            data["hasGroup"] = Flag(visible && !string.IsNullOrEmpty(definition.Group) && definition.Group != previousVisibleGroup);
            if (visible) previousVisibleGroup = definition.Group;
            var row = DesktopMarkup.Dynamic("ThemeOption.d.nxml", data);
            if (_themeOptionEditors.TryGetValue(definition.Id, out var editor))
            {
                _dynamic.Reload(editor.Root, row);
                _dynamic.Reload(editor.EditorRoot, DesktopMarkup.Dynamic(editor.EditorTemplate, data));
                editor = editor with { Definition = definition, Effective = effective };
                _themeOptionEditors[definition.Id] = editor;
            }
            else
            {
                var root = _dynamic.Append(slot, row);
                string template = definition.Kind switch
                {
                    ThemeOptionKind.Boolean => "ThemeOptionBoolean.d.nxml",
                    ThemeOptionKind.Number => "ThemeOptionNumber.d.nxml",
                    ThemeOptionKind.Range => "ThemeOptionRange.d.nxml",
                    ThemeOptionKind.Color => "ThemeOptionColor.d.nxml",
                    ThemeOptionKind.Select => "ThemeOptionSelect.d.nxml",
                    ThemeOptionKind.File or ThemeOptionKind.Files or ThemeOptionKind.Directory => "ThemeOptionFiles.d.nxml",
                    _ => "ThemeOptionText.d.nxml",
                };
                var editorRoot = _dynamic.Append(CatalogNodes(root)[prefix + ".Editor"], DesktopMarkup.Dynamic(template, data));
                var nodes = CatalogNodes(editorRoot);
                var input = nodes.GetValueOrDefault(prefix + ".Input");
                var range = nodes.GetValueOrDefault(prefix + ".Slider");
                editor = new(source.SourceIdentity, definition, root, editorRoot, input, range, template, prefix, effective);
                _themeOptionEditors[definition.Id] = editor;
                if (input.IsAssigned) _themeOptionInputs[input] = definition.Id;
                if (input.IsAssigned) _shell.Renderer.SetTextInputValue(input, value.Value);
            }
            order.Add(editor.Root);
            if (editor.Range.IsAssigned) _shell.Renderer.SetRangeValue(editor.Range, double.Parse(data["rangeValue"], CultureInfo.InvariantCulture));
            BindThemeAction(editor.EditorRoot, prefix + ".True", new(ThemeUiOperation.OptionChoice, source.SourceIdentity, definition.Id, "true"));
            BindThemeAction(editor.EditorRoot, prefix + ".False", new(ThemeUiOperation.OptionChoice, source.SourceIdentity, definition.Id, "false"));
            BindThemeAction(editor.EditorRoot, prefix + ".Increase", new(ThemeUiOperation.OptionIncrease, source.SourceIdentity, definition.Id));
            BindThemeAction(editor.EditorRoot, prefix + ".Decrease", new(ThemeUiOperation.OptionDecrease, source.SourceIdentity, definition.Id));
            BindThemeAction(editor.EditorRoot, prefix + ".Slider", new(ThemeUiOperation.OptionRange, source.SourceIdentity, definition.Id));
            BindThemeAction(editor.EditorRoot, prefix + ".Pick", new(ThemeUiOperation.OptionPick, source.SourceIdentity, definition.Id));
            if (definition.Kind == ThemeOptionKind.Select) ProjectThemeChoices(editor, value);
            if (definition.Kind is ThemeOptionKind.File or ThemeOptionKind.Files or ThemeOptionKind.Directory) ProjectThemeFiles(editor, value);
        }
        _dynamic.Reorder(slot, order);
    }

    private bool ThemeOptionVisible(string? expression, IReadOnlyDictionary<string, ThemeOptionValue> values)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;
        // An unfinished numeric draft (for example "-") is valid editor state. Until it
        // forms a finite number, conditional visibility continues using the saved value.
        var inputs = values.ToDictionary(pair => pair.Key, pair =>
            pair.Value.Kind is ThemeOptionKind.Number or ThemeOptionKind.Range
                && (!double.TryParse(pair.Value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                ? _themeOptionEditors.GetValueOrDefault(pair.Key)?.Effective ?? pair.Value : pair.Value, StringComparer.Ordinal);
        return ThemeOptionCondition.Evaluate(expression, inputs);
    }

    private Dictionary<string, string> ThemeOptionData(ThemeOptionDefinition definition, string prefix, ThemeOptionValue value, bool visible) => new()
    {
        ["key"] = prefix,
        ["label"] = ThemeLocalized(definition.Label),
        ["description"] = ThemeLocalized(definition.Description),
        ["editorKey"] = prefix + ".Editor",
        ["inputKey"] = prefix + ".Input",
        ["swatchKey"] = prefix + ".Swatch",
        ["trueKey"] = prefix + ".True",
        ["falseKey"] = prefix + ".False",
        ["increaseKey"] = prefix + ".Increase",
        ["decreaseKey"] = prefix + ".Decrease",
        ["sliderKey"] = prefix + ".Slider",
        ["choicesKey"] = prefix + ".Choices",
        ["filesKey"] = prefix + ".Files",
        ["pickKey"] = prefix + ".Pick",
        ["group"] = definition.Group ?? "",
        ["hasGroup"] = Flag(!string.IsNullOrEmpty(definition.Group)),
        ["visible"] = Flag(visible),
        ["enabled"] = Flag(!ThemeSettingsBusy),
        ["canPick"] = Flag(_themePicker is not null && !ThemeSettingsBusy),
        ["true"] = Flag(value.Value == "true"),
        ["false"] = Flag(value.Value != "true"),
        ["color"] = ValidThemeColor(value.Value) ? value.Value : "#808080",
        ["minimum"] = definition.Minimum is { } minimum ? ThemeNumber(minimum) : "0",
        ["maximum"] = definition.Maximum is { } maximum ? ThemeNumber(maximum) : "100",
        ["step"] = ThemeNumber(ThemeOptionStep(definition)),
        ["bounded"] = Flag(definition.Minimum is not null && definition.Maximum is not null),
        ["slider"] = Flag(definition.Kind == ThemeOptionKind.Range),
        ["rangeValue"] = ThemeNumber(ThemeRangeValue(definition, value)),
    };

    private static double ThemeRangeValue(ThemeOptionDefinition definition, ThemeOptionValue value)
    {
        double current = double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
            ? number : double.TryParse(definition.Default.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double initial) && double.IsFinite(initial) ? initial : 0;
        return Math.Clamp(current, definition.Minimum ?? 0, definition.Maximum ?? 100);
    }

    private static double ThemeOptionStep(ThemeOptionDefinition definition) => definition.Step
        ?? (definition.Kind == ThemeOptionKind.Range && definition.Minimum is { } minimum && definition.Maximum is { } maximum
            ? Math.Min(1, (maximum - minimum) / 100) : 1);

    private static bool ValidThemeColor(string value) => value.Length is 7 or 9 && value[0] == '#'
        && !value.AsSpan(1).ContainsAnyExcept(ThemeHexDigits);

    private void ProjectThemeChoices(ThemeOptionEditor editor, ThemeOptionValue value)
    {
        var slot = CatalogNodes(editor.EditorRoot)[editor.Prefix + ".Choices"];
        var existing = _shell.Tree.Children(slot).ToDictionary(entity => _shell.Tree.Name(entity), StringComparer.Ordinal);
        List<XsrUiEntityId> order = [];
        foreach (var choice in editor.Definition.Choices)
        {
            string key = editor.Prefix + ".Choice." + ThemeIdentityKey(choice.Value);
            var fragment = DesktopMarkup.Dynamic("ThemeOptionChoice.d.nxml", new Dictionary<string, string>
            {
                ["key"] = key,
                ["label"] = ThemeLocalized(choice.Label),
                ["selected"] = Flag(choice.Value == value.Value),
                ["enabled"] = Flag(!ThemeSettingsBusy),
            });
            if (existing.Remove(key, out var entity)) _dynamic.Reload(entity, fragment);
            else entity = _dynamic.Append(slot, fragment);
            order.Add(entity); _themeActions[entity] = new(ThemeUiOperation.OptionChoice, editor.Source, editor.Definition.Id, choice.Value);
        }
        foreach (var stale in existing.Values) _dynamic.Remove(stale);
        _dynamic.Reorder(slot, order);
    }

    private void ProjectThemeFiles(ThemeOptionEditor editor, ThemeOptionValue value)
    {
        var slot = CatalogNodes(editor.EditorRoot)[editor.Prefix + ".Files"];
        IReadOnlyList<string> paths = value.Kind == ThemeOptionKind.Files ? value.Values ?? [] : value.Value.Length == 0 ? [] : [value.Value];
        var existing = _shell.Tree.Children(slot).ToDictionary(entity => _shell.Tree.Name(entity), StringComparer.Ordinal);
        List<XsrUiEntityId> order = [];
        foreach (string path in paths)
        {
            string key = editor.Prefix + ".File." + ThemeIdentityKey(path);
            var fragment = DesktopMarkup.Dynamic("ThemeOptionFile.d.nxml", new Dictionary<string, string>
            { ["key"] = key, ["removeKey"] = key + ".Remove", ["path"] = path, ["enabled"] = Flag(!ThemeSettingsBusy) });
            if (existing.Remove(key, out var entity)) _dynamic.Reload(entity, fragment);
            else entity = _dynamic.Append(slot, fragment);
            order.Add(entity);
            BindThemeAction(entity, key + ".Remove", new(ThemeUiOperation.OptionRemoveFile, editor.Source, editor.Definition.Id, path));
        }
        foreach (var stale in existing.Values) _dynamic.Remove(stale);
        _dynamic.Reorder(slot, order);
    }

    private void ProjectThemeGrants(ThemeCatalogueEntry? source, DesktopThemeSettingsSnapshot? snapshot)
    {
        var grants = snapshot?.Current.Grants.Where(item => item.SourceIdentity == source?.SourceIdentity).ToArray() ?? [];
        var wanted = grants.Select(item => item.GrantId).ToHashSet();
        foreach (var stale in _themeGrantRows.Keys.Where(key => !wanted.Contains(key)).ToArray())
        { _dynamic.Remove(_themeGrantRows[stale]); _themeGrantRows.Remove(stale); }
        var slot = CatalogNodes(_themeGrantRoot)["Theme.GrantRows"];
        List<XsrUiEntityId> order = [];
        foreach (var grant in grants)
        {
            string key = "Theme.Grant." + grant.GrantId.ToString("N");
            var fragment = DesktopMarkup.Dynamic("ThemeGrant.d.nxml", new Dictionary<string, string>
            {
                ["key"] = key,
                ["pathKey"] = key + ".Path",
                ["revokeKey"] = key + ".Revoke",
                ["path"] = grant.CanonicalPath,
                ["kind"] = grant.Kind.ToString(),
                ["enabled"] = Flag(!ThemeSettingsBusy)
            });
            if (_themeGrantRows.TryGetValue(grant.GrantId, out var entity)) _dynamic.Reload(entity, fragment);
            else _themeGrantRows[grant.GrantId] = entity = _dynamic.Append(slot, fragment);
            order.Add(entity);
            BindThemeAction(entity, key + ".Revoke", new(ThemeUiOperation.Revoke, grant.SourceIdentity, Grant: grant.GrantId));
        }
        _dynamic.Reorder(slot, order);
    }

    private void CaptureThemeDrafts()
    {
        foreach (var editor in _themeOptionEditors.Values)
        {
            if (!editor.Input.IsAssigned || !_shell.Tree.IsAlive(editor.Input)) continue;
            var input = _shell.Tree.GetComponent<XsrUiTextInput>(editor.Input);
            if (input is null) continue;
            string draft = input.ReadDraft();
            var key = (editor.Source, editor.Definition.Id);
            if (draft != editor.Effective.Value) _themeDrafts[key] = new(editor.Definition.Kind, draft);
            else _themeDrafts.Remove(key);
        }
    }

    private Dictionary<string, ThemeOptionValue> ThemeOptionValues(string source)
    {
        var entry = _themeSession?.Snapshot.Catalogue.Themes.FirstOrDefault(item => item.SourceIdentity == source);
        var state = _themeSession?.Snapshot.Current.Options.FirstOrDefault(item => item.SourceIdentity == source);
        return entry?.Manifest.Options.ToDictionary(definition => definition.Id,
            definition => _themeDrafts.GetValueOrDefault((source, definition.Id)) ?? state?.Values.GetValueOrDefault(definition.Id) ?? definition.Default,
            StringComparer.Ordinal) ?? new Dictionary<string, ThemeOptionValue>();
    }

    private long ThemeOptionsRevision() => _themeSession?.Snapshot.Current.Revision ?? 0;

    private void ChangeThemeOption(ThemeUiAction action, XsrUiEntityId sourceEntity = default)
    {
        if (action.Source != _themePresentedIdentity || action.Option is null || !_themeOptionEditors.TryGetValue(action.Option, out var editor)
            || editor.Source != action.Source) return;
        CaptureThemeDrafts();
        var value = ThemeOptionValues(editor.Source)[editor.Definition.Id];
        if (action.Operation == ThemeUiOperation.OptionRange && sourceEntity == editor.Range
            && _shell.Renderer.GetRangeValue(editor.Range) is { } rangeValue)
            value = new(editor.Definition.Kind, ThemeNumber(rangeValue));
        else if (action.Operation == ThemeUiOperation.OptionChoice && action.Value is { } selected)
            value = new(editor.Definition.Kind, selected);
        else if (action.Operation is ThemeUiOperation.OptionIncrease or ThemeUiOperation.OptionDecrease)
        {
            if (!double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double current) || !double.IsFinite(current)) return;
            double step = ThemeOptionStep(editor.Definition);
            double minimum = editor.Definition.Minimum ?? 0;
            double index = (current - minimum) / step;
            double next = minimum + (action.Operation == ThemeUiOperation.OptionIncrease ? Math.Floor(index + 1e-9) + 1 : Math.Ceiling(index - 1e-9) - 1) * step;
            next = Math.Clamp(next, editor.Definition.Minimum ?? -1e12, editor.Definition.Maximum ?? 1e12);
            value = new(editor.Definition.Kind, ThemeNumber(next));
        }
        else if (action.Operation == ThemeUiOperation.OptionRemoveFile)
            value = editor.Definition.Kind == ThemeOptionKind.Files
                ? new(ThemeOptionKind.Files, "", (value.Values ?? []).Where(path => path != action.Value).ToArray())
                : new(editor.Definition.Kind, "");
        else return;
        _themeDrafts[(editor.Source, editor.Definition.Id)] = value;
        if (editor.Input.IsAssigned) _shell.Renderer.SetTextInputValue(editor.Input, value.Value);
        ProjectThemeSettings();
    }
}
