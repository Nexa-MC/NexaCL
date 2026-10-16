using System.Text;
using Nexa.Services.Settings;
using Nexa.Services.Themes;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private Action? _themeOperationCompleted;
    private readonly HashSet<string> _themeWatching = new(StringComparer.Ordinal);

    private void HandleThemeIntent(DesktopUiIntent intent)
    {
        if (intent.Command != ThemeAction || !ThemeSettingsVisible || !_shell.Tree.IsAlive(intent.Source)
            || !_themeActions.TryGetValue(intent.Source, out var action) || _themeSession is null) return;
        bool optionDraft = action.Operation is ThemeUiOperation.OptionChoice or ThemeUiOperation.OptionIncrease or ThemeUiOperation.OptionDecrease
            or ThemeUiOperation.OptionRange or ThemeUiOperation.OptionRemoveFile;
        if (optionDraft ? ThemeOptionEditorsBusy : ThemeSettingsBusy) return;
        var input = _shell.Tree.GetComponent<Nexa.Xsr.Renderer.XsrUiInput>(intent.Source);
        if (input?.Enabled == false || !ThemeActionAccessible(intent.Source)) return;
        var session = _themeSession;
        string? source = action.Source;
        if (source is not null && !session.Snapshot.Catalogue.Themes.Any(item => item.SourceIdentity == source)
            && action.Operation != ThemeUiOperation.Revoke) return;
        if (optionDraft) { ChangeThemeOption(action, intent.Source); return; }
        if (action.Operation == ThemeUiOperation.OptionPick) { PickThemeOption(action); return; }
        CaptureThemeDrafts();
        switch (action.Operation)
        {
            case ThemeUiOperation.ImportPackage when _themePicker is not null:
            case ThemeUiOperation.ImportDirectory when _themePicker is not null:
                StartThemeOperation(async token =>
                {
                    bool directory = action.Operation == ThemeUiOperation.ImportDirectory;
                    var paths = await _themePicker(new(directory ? ThemeOptionKind.Directory : ThemeOptionKind.File, null, Package: !directory), token).ConfigureAwait(false);
                    return paths.Count == 0 ? XsrResult.Success() : await session.ImportAsync(paths[0], directory ? ThemeSourceKind.Directory : ThemeSourceKind.Package, false, token).ConfigureAwait(false);
                });
                break;
            case ThemeUiOperation.Refresh:
                StartThemeOperation(session.RefreshAsync); break;
            case ThemeUiOperation.Activate when source is not null:
                StartThemeOperation(token => session.ActivateAsync(source, false, token)); break;
            case ThemeUiOperation.Preview when source is not null:
                StartThemeOperation(token => session.ActivateAsync(source, true, token)); break;
            case ThemeUiOperation.EndPreview:
                StartThemeOperation(session.EndPreviewAsync); break;
            case ThemeUiOperation.Folder when source is not null:
                StartThemeOperation(token => session.OpenFolderAsync(source, token)); break;
            case ThemeUiOperation.Remove when source is not null:
                StartThemeOperation(token => session.RemoveAsync(new(source), token), () => ClearThemeDrafts(source)); break;
            case ThemeUiOperation.Watch when source is not null:
                bool enabled = !_themeWatching.Contains(source);
                StartThemeOperation(token => session.WatchAsync(new(source, enabled), token), () =>
                { if (enabled) _themeWatching.Add(source); else _themeWatching.Remove(source); }); break;
            case ThemeUiOperation.Save when source == _themePresentedIdentity && source is not null:
                var values = ThemeOptionValues(source);
                StartThemeOperation(token => session.SaveOptionsAsync(new(source, values, ThemeOptionsRevision()), token), () => ClearThemeDrafts(source)); break;
            case ThemeUiOperation.Reset when source == _themePresentedIdentity && source is not null:
                StartThemeOperation(token => session.ResetOptionsAsync(new(source, ThemeOptionsRevision()), token), () => ClearThemeDrafts(source)); break;
            case ThemeUiOperation.Export when source is not null && _themeExportDirectoryPicker is not null:
                StartThemeOperation(token => ExportThemeOptionsAsync(source, token)); break;
            case ThemeUiOperation.ImportOptions when source is not null && _themeOptionsImportPicker is not null:
                StartThemeOperation(token => ImportThemeOptionsAsync(source, token), () => ClearThemeDrafts(source)); break;
            case ThemeUiOperation.Revoke when action.Grant is { } grant:
                if (!session.Snapshot.Current.Grants.Any(item => item.GrantId == grant && item.SourceIdentity == source)) return;
                StartThemeOperation(token => session.RevokeAsync(new(grant), token)); break;
            case ThemeUiOperation.IconDefault:
                StartThemeOperation(token => SaveThemeIconAsync("theme", null, token)); break;
            case ThemeUiOperation.IconHostDefault:
                StartThemeOperation(token => SaveThemeIconAsync("default", null, token)); break;
            case ThemeUiOperation.IconPick when _themePicker is not null:
                StartThemeOperation(async token =>
                {
                    var paths = await _themePicker(new(ThemeOptionKind.File, ThemeResourceKind.Icon), token).ConfigureAwait(false);
                    return paths.Count == 0 ? XsrResult.Success() : await SaveThemeIconAsync("custom", paths[0], token).ConfigureAwait(false);
                }); break;
        }
    }

    private bool ThemeActionAccessible(Nexa.Xsr.Renderer.XsrUiEntityId entity)
    {
        for (var current = entity; current.IsAssigned && _shell.Tree.IsAlive(current); current = _shell.Tree.Parent(current))
        {
            if (_shell.Tree.GetComponent<Nexa.Xsr.Renderer.XsrUiElement>(current)?.IsVisible == false) return false;
            if (current == _sections) return true;
        }
        return false;
    }

    private void StartThemeOperation(Func<CancellationToken, Task<XsrResult>> operation, Action? completed = null)
    {
        if (!ThemeSettingsVisible || _themeOperation is not null) return;
        _themeStop?.Dispose(); _themeStop = new(); _themeError = null;
        _themeOperationCompleted = completed;
        try { _themeOperation = operation(_themeStop.Token); }
        catch (Exception exception) { _themeOperation = Task.FromException<XsrResult>(exception); }
        WakeOnPlatformCompletion(_themeOperation);
        _themePresentationRevision = -1;
    }

    private void ClearThemeDrafts(string source)
    {
        foreach (var key in _themeDrafts.Keys.Where(item => item.Source == source).ToArray()) _themeDrafts.Remove(key);
        var state = _themeSession?.Snapshot.Current.Options.FirstOrDefault(item => item.SourceIdentity == source);
        foreach (var pair in _themeOptionEditors.Where(item => item.Value.Source == source).ToArray())
        {
            var editor = pair.Value;
            var effective = state?.Values.GetValueOrDefault(editor.Definition.Id) ?? editor.Definition.Default;
            _themeOptionEditors[pair.Key] = editor with { Effective = effective };
            if (editor.Input.IsAssigned && _shell.Tree.IsAlive(editor.Input)) _shell.Renderer.SetTextInputValue(editor.Input, effective.Value);
        }
    }

    private void PickThemeOption(ThemeUiAction action)
    {
        if (_themePicker is null || _themeSession is null || action.Source != _themePresentedIdentity || action.Option is null
            || !_themeOptionEditors.TryGetValue(action.Option, out var editor) || editor.Source != action.Source) return;
        var session = _themeSession;
        string source = editor.Source;
        long scope = _themeScope;
        IReadOnlyList<string>? selected = null;
        StartThemeOperation(async token =>
        {
            var paths = await _themePicker(new(editor.Definition.Kind, editor.Definition.ResourceKind), token).ConfigureAwait(false);
            if (paths.Count == 0) return XsrResult.Success();
            foreach (string path in paths)
            {
                token.ThrowIfCancellationRequested();
                var admitted = await session.GrantAsync(new(source, editor.Definition.ResourceKind ?? ThemeResourceKind.Image,
                    editor.Definition.Kind == ThemeOptionKind.Directory ? ThemeGrantScope.Directory : ThemeGrantScope.File, path), token).ConfigureAwait(false);
                if (!admitted.IsSuccess) return admitted;
            }
            selected = paths; return XsrResult.Success();
        }, () =>
        {
            if (scope != _themeScope || _themePresentedIdentity != source || selected is null) return;
            _themeDrafts[(source, editor.Definition.Id)] = editor.Definition.Kind == ThemeOptionKind.Files
                ? new(editor.Definition.Kind, "", selected.Distinct(StringComparer.Ordinal).ToArray())
                : new(editor.Definition.Kind, selected[0]);
            ScheduleThemeOptionReload(source);
        });
    }

    private async Task<XsrResult> SaveThemeIconAsync(string mode, string? path, CancellationToken token)
    {
        if (!_commands.TryResolve(SettingsPolicyContract.SetCommand, out var route)) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
        if (path is not null)
        {
            var saved = await _commands.Dispatch(route,
                new SettingsMutation("appearance.launcher-icon-path", SettingsLayer.Global, new(SettingsOverrideMode.Custom, path)), cancellationToken: token).Completion.ConfigureAwait(false);
            if (!saved.IsSuccess) return saved;
        }
        return await _commands.Dispatch(route,
            new SettingsMutation("appearance.launcher-icon-mode", SettingsLayer.Global, new(SettingsOverrideMode.Custom, mode)), cancellationToken: token).Completion.ConfigureAwait(false);
    }

    private async Task<XsrResult> ImportThemeOptionsAsync(string source, CancellationToken token)
    {
        var session = _themeSession!;
        long revision = ThemeOptionsRevision();
        string? path = await _themeOptionsImportPicker!(token).ConfigureAwait(false);
        if (path is null) return XsrResult.Success();
        token.ThrowIfCancellationRequested();
        const int limit = 128 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (stream.Length > limit) throw new IOException("Theme option import exceeds its 128 KiB budget.");
        byte[] bytes = new byte[limit + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        if (count > limit) throw new IOException("Theme option import exceeds its 128 KiB budget.");
        int offset = count >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        string json = new UTF8Encoding(false, true).GetString(bytes, offset, count - offset);
        return await session.ImportOptionsAsync(new(source, json, revision), token).ConfigureAwait(false);
    }

    private async Task<XsrResult> ExportThemeOptionsAsync(string source, CancellationToken token)
    {
        var session = _themeSession!;
        string? directory = await _themeExportDirectoryPicker!(token).ConfigureAwait(false);
        if (directory is null) return XsrResult.Success();
        var exported = await session.ExportOptionsAsync(new(source), token).ConfigureAwait(false);
        if (!exported.IsSuccess) return XsrResult.Failure(exported.Error!);
        token.ThrowIfCancellationRequested();
        string path = Path.Combine(directory, "theme-options-" + ThemeIdentityKey(source) + ".json");
        await File.WriteAllTextAsync(path, exported.Value.Json, Encoding.UTF8, token).ConfigureAwait(false);
        return XsrResult.Success();
    }
}
