using System.Globalization;
using Nexa.Services.Themes;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private sealed record ThemeOptionReload(string Source, long Scope,
        IReadOnlyDictionary<string, ThemeOptionValue> Values, CancellationTokenSource Stop, Task Ready);
    private ThemeOptionReload? _themeOptionReload;
    private string? _themeOptionRangeGestureSource;
    private bool _themeOptionReloading;
    private (string Source, IReadOnlyDictionary<string, ThemeOptionValue> Values)? _themeOptionReloadSubmitted;
    private bool ThemeOptionEditorsBusy => ThemeSettingsBusy && !_themeOptionReloading;
    private (string Source, IReadOnlyDictionary<string, ThemeOptionValue> Values)? _themeOptionReloadAttempt;
    internal Func<CancellationToken, Task> WaitForThemeOptionReload { get; set; } =
        token => Task.Delay(TimeSpan.FromMilliseconds(180), token);

    private void ScheduleThemeOptionReload(string source)
    {
        if (!ThemeSettingsVisible || _themeSession is null || source != _themePresentedIdentity) return;
        var values = CompleteThemeOptionDrafts(source);
        if (values.Count == 0) { CancelThemeOptionReload(); _themeOptionReloadAttempt = null; return; }
        if (_themeOptionReload is { } pending && pending.Source == source && ThemeOptionValuesEqual(pending.Values, values)) return;
        if (_themeOptionReloadAttempt is { } attempted && attempted.Source == source && ThemeOptionValuesEqual(attempted.Values, values))
        { CancelThemeOptionReload(); return; }
        _themeOptionReloadAttempt = null;
        CancelThemeOptionReload();
        var stop = new CancellationTokenSource();
        var ready = WaitForThemeOptionReload(stop.Token);
        _themeOptionReload = new(source, _themeScope, values, stop, ready);
        WakeOnPlatformCompletion(ready);
    }

    private void UpdateThemeOptionReload()
    {
        if (_themePresentedIdentity is not { } source || _themeSession is not { } session) return;
        if (!_themeOptionReloading) _themeOptionReloadSubmitted = null;
        CaptureThemeDrafts();
        ScheduleThemeOptionReload(source);
        // A native range gesture owns its pressed state until release. Starting a theme
        // transaction here would disable the editor and interrupt an otherwise valid drag.
        if (_themeOptionEditors.Values.Any(editor => editor.Source == source && editor.Range.IsAssigned
            && _shell.Tree.GetComponent<XsrUiInput>(editor.Range)?.IsPressed == true))
        { _themeOptionRangeGestureSource = source; return; }
        bool releasedRange = _themeOptionRangeGestureSource == source;
        _themeOptionRangeGestureSource = null;
        if (_themeOptionReload is not { } pending || (!pending.Ready.IsCompletedSuccessfully && !releasedRange) || ThemeSettingsBusy) return;
        if (pending.Scope != _themeScope || pending.Source != source) { CancelThemeOptionReload(); return; }
        _themeOptionReload = null; pending.Stop.Cancel(); pending.Stop.Dispose();
        _themeOptionReloadAttempt = (source, pending.Values);
        _themeOptionReloadSubmitted = (source, pending.Values);
        long revision = ThemeOptionsRevision();
        _themeOptionReloading = true;
        StartThemeOperation(token => session.SaveOptionsAsync(new(source, pending.Values, revision), token),
            () => CompleteThemeOptionReload(source, pending.Values));
    }

    private Dictionary<string, ThemeOptionValue> CompleteThemeOptionDrafts(string source)
    {
        var state = _themeSession?.Snapshot.Current.Options.FirstOrDefault(item => item.SourceIdentity == source);
        Dictionary<string, ThemeOptionValue> values = new(StringComparer.Ordinal);
        foreach (var editor in _themeOptionEditors.Values)
        {
            if (editor.Source != source || !_themeDrafts.TryGetValue((source, editor.Definition.Id), out var draft)
                || !ThemeOptionDraftComplete(editor.Definition, draft)) continue;
            var effective = state?.Values.GetValueOrDefault(editor.Definition.Id) ?? editor.Definition.Default;
            if (!ThemeOptionValueEqual(draft, effective)) values[editor.Definition.Id] = draft;
        }
        return values;
    }

    private void CompleteThemeOptionReload(string source, IReadOnlyDictionary<string, ThemeOptionValue> submitted)
    {
        // Snapshot publication and task completion are separate boundaries. Capture
        // edits made after submission before advancing the editors' saved baseline.
        CaptureThemeDrafts(completingReload: true);
        foreach (var pair in submitted)
            if (_themeDrafts.TryGetValue((source, pair.Key), out var current) && ThemeOptionValueEqual(current, pair.Value))
                _themeDrafts.Remove((source, pair.Key));
        var state = _themeSession?.Snapshot.Current.Options.FirstOrDefault(item => item.SourceIdentity == source);
        foreach (var pair in _themeOptionEditors.Where(item => item.Value.Source == source).ToArray())
        {
            var editor = pair.Value;
            _themeOptionEditors[pair.Key] = editor with
            { Effective = state?.Values.GetValueOrDefault(editor.Definition.Id) ?? editor.Definition.Default };
        }
        _themeOptionReloadSubmitted = null;
    }

    private bool ThemeDraftDiffersFromSubmitted(ThemeOptionEditor editor, string draft, bool completingReload) =>
        (_themeOptionReloading || completingReload) && _themeOptionReloadSubmitted is { } submitted
        && submitted.Source == editor.Source && submitted.Values.TryGetValue(editor.Definition.Id, out var value)
        && draft != value.Value;

    private static bool ThemeOptionDraftComplete(ThemeOptionDefinition option, ThemeOptionValue value)
    {
        // This only suppresses unfinished editor drafts. Admission remains the service's authority.
        if (value.Kind != option.Kind || value.Value.Length > 4096 || value.Value.Contains('\0')) return false;
        if (option.Kind is ThemeOptionKind.Number or ThemeOptionKind.Range)
        {
            if (!double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                || !double.IsFinite(number) || number < option.Minimum || number > option.Maximum) return false;
            if (option.Step is { } step)
            {
                double quotient = (number - (option.Minimum ?? 0)) / step;
                if (!double.IsFinite(quotient) || Math.Abs(quotient - Math.Round(quotient)) > 1e-7) return false;
            }
        }
        return option.Kind switch
        {
            ThemeOptionKind.Boolean => value.Value is "true" or "false",
            ThemeOptionKind.Color => ValidThemeColor(value.Value),
            ThemeOptionKind.Select => option.Choices.Any(choice => choice.Value == value.Value),
            ThemeOptionKind.Files => value.Value.Length == 0 && value.Values is { Count: <= 256 },
            _ => true,
        };
    }

    private static bool ThemeOptionValueEqual(ThemeOptionValue left, ThemeOptionValue right) => left.Kind == right.Kind
        && left.Value == right.Value && (left.Values is null ? right.Values is null : right.Values is not null && left.Values.SequenceEqual(right.Values));

    private static bool ThemeOptionValuesEqual(IReadOnlyDictionary<string, ThemeOptionValue> left,
        Dictionary<string, ThemeOptionValue> right) => left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var other) && ThemeOptionValueEqual(pair.Value, other));

    private void CancelThemeOptionReload()
    {
        if (_themeOptionReload is not { } pending) return;
        _themeOptionReload = null; pending.Stop.Cancel(); pending.Stop.Dispose();
    }

    private void RetireThemeOptionReload()
    {
        CancelThemeOptionReload(); _themeOptionReloadAttempt = null; _themeOptionReloadSubmitted = null;
        _themeOptionRangeGestureSource = null; _themeOptionReloading = false;
    }
}
