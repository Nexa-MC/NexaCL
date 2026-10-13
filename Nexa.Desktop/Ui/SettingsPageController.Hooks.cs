using System.Globalization;
using System.Text;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId HookAdd = XsrSemanticId.Parse("ui.settings.hook.add");
    private static readonly XsrSemanticId HookRemove = XsrSemanticId.Parse("ui.settings.hook.remove");
    private static readonly XsrSemanticId HookPage = XsrSemanticId.Parse("ui.settings.hook.page");
    private const int HookLinesPerPage = 32;
    private sealed record HookLine(string Text, string Separator);
    private sealed record HookDraftSnapshot(string Command, string Committed, int Page);
    private sealed record HookInheritanceAttempt(string Key, string? Instance, Task<XsrResult> Completion);
    private sealed class HookEditor(SettingsCatalogEntry entry, XsrUiEntityId rows)
    {
        public SettingsCatalogEntry Entry { get; } = entry;
        public XsrUiEntityId Rows { get; } = rows;
        public List<HookLine> Lines { get; set; } = [new("", "")];
        public List<(XsrUiEntityId Entity, int Index, string Original, string Presented)> Inputs { get; } = [];
        public string Committed { get; set; } = "";
        public Dictionary<int, XsrUiEntityId> RowViews { get; } = [];
        public XsrUiEntityId ActionsView { get; set; }
        public bool Initialized { get; set; }
        public int Page { get; set; }
    }
    private readonly Dictionary<XsrUiEntityId, HookEditor> _hookEditors = [];
    private readonly Dictionary<XsrUiEntityId, (HookEditor Editor, int Index)> _hookActions = [];
    private HookInheritanceAttempt? _hookInheritance;
    internal bool SettingsWritePending => _writing is not null;

    private void BuildHookEditor(XsrUiEntityId parent, SettingsCatalogEntry entry)
    {
        var root = _dynamic.Append(parent, DesktopMarkup.Dynamic("SettingsHook.d.nxml", new Dictionary<string, string>
        { ["panel"] = "SettingsHook." + entry.SettingKey, ["rows"] = "SettingsHookRows." + entry.SettingKey, ["apply"] = "SettingsEdit." + entry.SettingKey }));
        var nodes = CatalogNodes(root);
        var rows = nodes["SettingsHookRows." + entry.SettingKey]; var apply = nodes["SettingsEdit." + entry.SettingKey];
        var editor = new HookEditor(entry, rows);
        _hookEditors[apply] = editor;
        _editors[apply] = new(entry, default, apply, default);
        BuildHookRows(editor);
    }

    private static List<HookLine> SplitHookLines(string command)
    {
        List<HookLine> lines = [];
        int start = 0;
        for (int index = 0; index < command.Length; index++)
        {
            if (command[index] is not ('\r' or '\n')) continue;
            int textEnd = index;
            if (command[index] == '\r' && index + 1 < command.Length && command[index + 1] == '\n') index++;
            lines.Add(new(command[start..textEnd], command[textEnd..(index + 1)]));
            start = index + 1;
        }
        lines.Add(new(command[start..], ""));
        return lines;
    }

    private void CaptureHookLines(HookEditor editor)
    {
        foreach (var input in editor.Inputs)
        {
            string text = _shell.Tree.GetComponent<XsrUiTextInput>(input.Entity)!.ReadDraft();
            // Keep any existing non-display control verbatim unless the user edits this line.
            editor.Lines[input.Index] = editor.Lines[input.Index] with { Text = text == input.Presented ? input.Original : text };
        }
    }

    private string ReadHookDraft(HookEditor editor)
    {
        CaptureHookLines(editor);
        StringBuilder command = new();
        foreach (var line in editor.Lines) command.Append(line.Text).Append(line.Separator);
        return command.ToString();
    }

    private Dictionary<string, HookDraftSnapshot> CaptureHookDrafts(bool navigating)
        => navigating ? [] : _hookEditors.Values.ToDictionary(editor => editor.Entry.Id,
            editor => new HookDraftSnapshot(ReadHookDraft(editor), editor.Committed, editor.Page));

    private void RestoreHookDrafts(IReadOnlyDictionary<string, HookDraftSnapshot> drafts)
    {
        foreach (var editor in _hookEditors.Values)
            if (drafts.TryGetValue(editor.Entry.Id, out var draft))
            {
                editor.Lines = SplitHookLines(draft.Command); editor.Committed = draft.Committed;
                editor.Page = draft.Page; editor.Initialized = true; BuildHookRows(editor);
            }
    }

    private void UpdateHookEditors()
    {
        foreach (var editor in _hookEditors.Values)
        {
            string? raw = _values?.Values.FirstOrDefault(value => value.Key == editor.Entry.SettingKey)?.Value.Value;
            if (raw is null) continue;
            if (!editor.Initialized || raw != editor.Committed && ReadHookDraft(editor) == editor.Committed)
            {
                editor.Lines = SplitHookLines(raw); editor.Initialized = true; BuildHookRows(editor);
            }
            editor.Committed = raw;
        }
    }

    private void TrackHookInheritance(string key, Task<XsrResult> completion)
    {
        if (_hookEditors.Values.Any(editor => editor.Entry.SettingKey == key))
            _hookInheritance = new(key, _instance, completion);
    }

    private void UpdateHookInheritance()
    {
        if (_hookInheritance is not { Completion.IsCompleted: true } attempt) return;
        _hookInheritance = null;
        if (!attempt.Completion.IsCompletedSuccessfully || !attempt.Completion.Result.IsSuccess
            || attempt.Instance != _instance) return;
        foreach (var editor in _hookEditors.Values.Where(editor => editor.Entry.SettingKey == attempt.Key))
            editor.Initialized = false;
        // The write may finish after an effective-value query already completed. Replace
        // the draft now and refresh the committed value even when that revision was seen.
        UpdateHookEditors();
        _revision = -1;
    }

    private void BuildHookRows(HookEditor editor)
    {
        foreach (var key in _hookActions.Where(pair => pair.Value.Editor == editor).Select(pair => pair.Key).ToArray()) _hookActions.Remove(key);
        var previousInputs = editor.Inputs.ToDictionary(input => input.Index);
        editor.Inputs.Clear();
        editor.Page = Math.Clamp(editor.Page, 0, (editor.Lines.Count - 1) / HookLinesPerPage);
        int start = editor.Page * HookLinesPerPage;
        int last = Math.Min(editor.Lines.Count, start + HookLinesPerPage);
        foreach (int index in editor.RowViews.Keys.Where(index => index < start || index >= last).ToArray())
        { _dynamic.Remove(editor.RowViews[index]); editor.RowViews.Remove(index); }
        for (int index = start; index < Math.Min(editor.Lines.Count, start + HookLinesPerPage); index++)
        {
            string inputKey = "SettingsHookLine." + editor.Entry.SettingKey + "." + index;
            string removeKey = "SettingsHookRemove." + editor.Entry.SettingKey + "." + index;
            var fragment = DesktopMarkup.Dynamic("SettingsCustomHookRow.d.nxml", new Dictionary<string, string>
            {
                ["input"] = inputKey,
                ["remove"] = removeKey,
                ["index"] = (index + 1).ToString(CultureInfo.InvariantCulture),
            }, _shell.Renderer.LocalizeText);
            if (editor.RowViews.TryGetValue(index, out var row)) _dynamic.Reload(row, fragment);
            else editor.RowViews[index] = row = _dynamic.Append(editor.Rows, fragment);
            var nodes = CatalogNodes(row); var input = nodes[inputKey]; var remove = nodes[removeKey];
            if (!previousInputs.TryGetValue(index, out var previous) || previous.Entity != input || previous.Original != editor.Lines[index].Text)
                _shell.Renderer.SetTextInputValue(input, editor.Lines[index].Text);
            editor.Inputs.Add((input, index, editor.Lines[index].Text, _shell.Tree.GetComponent<XsrUiTextInput>(input)!.ReadDraft()));
            _hookActions[remove] = (editor, index);
        }
        string addKey = "SettingsHookAdd." + editor.Entry.SettingKey;
        string previousKey = "SettingsHookPrevious." + editor.Entry.SettingKey;
        string nextKey = "SettingsHookNext." + editor.Entry.SettingKey;
        var actionsFragment = DesktopMarkup.Dynamic("SettingsCustomHookActions.d.nxml", new Dictionary<string, string>
        {
            ["add"] = addKey,
            ["previous"] = previousKey,
            ["next"] = nextKey,
            ["pages"] = Flag(editor.Lines.Count > HookLinesPerPage),
            ["page"] = (editor.Page + 1).ToString(CultureInfo.InvariantCulture),
            ["total"] = ((editor.Lines.Count - 1) / HookLinesPerPage + 1).ToString(CultureInfo.InvariantCulture),
        }, _shell.Renderer.LocalizeText);
        if (_shell.Tree.IsAlive(editor.ActionsView)) _dynamic.Reload(editor.ActionsView, actionsFragment);
        else editor.ActionsView = _dynamic.Append(editor.Rows, actionsFragment);
        _dynamic.Reorder(editor.Rows, Enumerable.Range(start, last - start).Select(index => editor.RowViews[index]).Append(editor.ActionsView).ToArray());
        var actionNodes = CatalogNodes(editor.ActionsView);
        _hookActions[actionNodes[addKey]] = (editor, -1);
        if (editor.Lines.Count > HookLinesPerPage)
        { _hookActions[actionNodes[previousKey]] = (editor, -1); _hookActions[actionNodes[nextKey]] = (editor, 1); }
        _shell.Tree.MarkDirty(editor.Rows, XsrUiDirtyKinds.Layout);
    }

    private static bool IsHookIntent(XsrSemanticId command) => command == HookAdd || command == HookRemove || command == HookPage;

    private void HandleHookIntent(DesktopUiIntent intent)
    {
        if (!_hookActions.TryGetValue(intent.Source, out var action)) return;
        var editor = action.Editor; CaptureHookLines(editor);
        if (intent.Command == HookPage) editor.Page += action.Index;
        else if (intent.Command == HookAdd)
        {
            string separator = editor.Lines.FirstOrDefault(line => line.Separator.Length > 0)?.Separator ?? "\n";
            editor.Lines[^1] = editor.Lines[^1] with { Separator = separator };
            editor.Lines.Add(new("", "")); editor.Page = (editor.Lines.Count - 1) / HookLinesPerPage;
        }
        else if (action.Index >= 0 && action.Index < editor.Lines.Count)
        {
            editor.Lines.RemoveAt(action.Index);
            if (editor.Lines.Count == 0) editor.Lines.Add(new("", ""));
            editor.Lines[^1] = editor.Lines[^1] with { Separator = "" };
        }
        editor.Initialized = true; BuildHookRows(editor);
        var focus = intent.Command == HookAdd ? editor.Inputs[^1].Entity : editor.Inputs[0].Entity;
        _shell.Renderer.Focus(focus);
    }
}
