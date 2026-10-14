using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Settings;
using Nexa.Services.Themes;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId ThemeAction = XsrSemanticId.Parse("ui.settings.theme.action");
    private IDesktopThemeSettingsSession? _themeSession;
    private XsrUiEntityId _themeRoot, _themeOptionsRoot, _themeIconRoot, _themeGrantRoot;
    private long _themePresentationRevision = -1, _themeScope, _themeSettingsRevision = -1;
    private string? _themePresentedIdentity, _themePresentationLanguage;
    private (XsrUiEntityId Entity, string Draft)? _themeFocusedDraft;
    private bool _themeRefreshRequested;
    private CancellationTokenSource? _themeStop;
    private Task<XsrResult>? _themeOperation;
    private string? _themeError;
    private readonly Dictionary<XsrUiEntityId, ThemeUiAction> _themeActions = [];
    private readonly Dictionary<string, XsrUiEntityId> _themeRows = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, XsrUiEntityId> _themeGrantRows = [];
    private readonly Dictionary<string, ThemeOptionEditor> _themeOptionEditors = new(StringComparer.Ordinal);
    private readonly Dictionary<XsrUiEntityId, string> _themeOptionInputs = [];
    private readonly Dictionary<(string Source, string Id), ThemeOptionValue> _themeDrafts = [];
    private Func<DesktopThemePickerRequest, CancellationToken, Task<IReadOnlyList<string>>>? _themePicker;
    private Func<CancellationToken, Task<string?>>? _themeOptionsImportPicker, _themeExportDirectoryPicker;
    private enum ThemeUiOperation
    {
        ImportPackage, ImportDirectory, Refresh, Activate, Preview, EndPreview, Folder, Remove, Watch,
        Save, Reset, Export, ImportOptions, OptionChoice, OptionIncrease, OptionDecrease, OptionRange, OptionPick, OptionRemoveFile, Revoke,
        IconDefault, IconHostDefault, IconPick
    }
    private sealed record ThemeUiAction(ThemeUiOperation Operation, string? Source = null, string? Option = null,
        string? Value = null, Guid? Grant = null);
    private sealed record ThemeOptionEditor(string Source, ThemeOptionDefinition Definition, XsrUiEntityId Root,
        XsrUiEntityId EditorRoot, XsrUiEntityId Input, XsrUiEntityId Range, string EditorTemplate, string Prefix, ThemeOptionValue Effective);

    internal IDesktopThemeSettingsSession? ThemeSession
    {
        get => _themeSession;
        set { RetireThemeScope(); _themeSession = value; _themeRefreshRequested = false; _themePresentationRevision = -1; }
    }

    internal void ConfigureThemePickers(Func<DesktopThemePickerRequest, CancellationToken, Task<IReadOnlyList<string>>> picker,
        Func<CancellationToken, Task<string?>> optionsImportPicker, Func<CancellationToken, Task<string?>> exportDirectoryPicker)
    {
        _themePicker = picker; _themeOptionsImportPicker = optionsImportPicker; _themeExportDirectoryPicker = exportDirectoryPicker;
        _themePresentationRevision = -1;
    }

    private bool ThemeSettingsVisible => !_disposed && _visible && _instanceDirectory is null && _selected == "appearance";
    private bool ThemeSettingsBusy => _themeOperation is not null || _themeSession?.Snapshot.Busy == true;
    private static string ThemeIdentityKey(string identity) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..20];
    private static string ThemeNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private void BuildThemeSettings()
    {
        if (_instanceDirectory is not null || _selected != "appearance") return;
        ResetThemeControls();
        _themeRoot = _dynamic.Append(_sections, DesktopMarkup.Dynamic("ThemeSettings.d.nxml", ThemeSettingsData()));
        _themeIconRoot = _dynamic.Append(_sections, DesktopMarkup.Dynamic("ThemeLauncherIcon.d.nxml", ThemeIconData()));
        _themeOptionsRoot = _dynamic.Append(_sections, DesktopMarkup.Dynamic("ThemeOptions.d.nxml", ThemeOptionsData()));
        _themeGrantRoot = _dynamic.Append(_sections, DesktopMarkup.Dynamic("ThemeGrants.d.nxml"));
        _themePresentationRevision = -1;
        ProjectThemeSettings();
    }

    private bool TryReloadThemeSettings()
    {
        if (_instanceDirectory is not null || _selected != "appearance" || !_shell.Tree.IsAlive(_themeRoot)) return false;
        CaptureThemeDrafts(); ProjectThemeSettings(); return true;
    }

    private void UpdateThemeSettings()
    {
        if (!ThemeSettingsVisible) return;
        if (_themeOperation is { IsCompleted: true } operation)
        {
            _themeOperation = null;
            if (_themeStop?.IsCancellationRequested == false)
            {
                _themeError = operation.IsCompletedSuccessfully ? operation.Result.Error?.Message
                    : operation.IsCanceled ? null : operation.Exception?.GetBaseException().Message;
                if (operation.IsCompletedSuccessfully && operation.Result.IsSuccess) _themeOperationCompleted?.Invoke();
            }
            _themeOperationCompleted = null;
            _themePresentationRevision = -1;
        }
        if (_themeSession is not null && !_themeRefreshRequested && _themeOperation is null)
        {
            _themeRefreshRequested = true;
            StartThemeOperation(token => _themeSession.RefreshAsync(token));
        }
        var snapshot = _themeSession?.Snapshot;
        string language = ThemeLanguage;
        var focused = _shell.Renderer.Focused;
        if (_themeOptionInputs.ContainsKey(focused) && _shell.Tree.IsAlive(focused))
        {
            string draft = _shell.Tree.GetComponent<XsrUiTextInput>(focused)!.ReadDraft();
            var inputState = (focused, draft);
            if (_themeFocusedDraft != inputState) { _themeFocusedDraft = inputState; _themePresentationRevision = -1; }
        }
        else _themeFocusedDraft = null;
        if (_themePresentationRevision != (snapshot?.Revision ?? 0) || _themePresentationLanguage != language || _themeSettingsRevision != _revision)
        {
            CaptureThemeDrafts(); ProjectThemeSettings();
        }
    }

    private Dictionary<string, string> ThemeSettingsData() => new()
    {
        ["ready"] = Flag(_themeSession is not null),
        ["busy"] = Flag(ThemeSettingsBusy),
        ["canImport"] = Flag(_themeSession is not null && _themePicker is not null && !ThemeSettingsBusy),
        ["preview"] = Flag(_themeSession?.Snapshot.IsPreview == true),
        ["error"] = _themeError ?? _themeSession?.Snapshot.Error ?? "",
        ["hasError"] = Flag(!string.IsNullOrEmpty(_themeError ?? _themeSession?.Snapshot.Error)),
    };

    private Dictionary<string, string> ThemeOptionsData()
    {
        var source = PresentedTheme();
        return new()
        {
            ["name"] = source is null ? "" : ThemeLocalized(source.Manifest.Name),
            ["ready"] = Flag(source is not null),
            ["enabled"] = Flag(source is not null && !ThemeSettingsBusy),
            ["canImport"] = Flag(source is not null && !ThemeSettingsBusy && _themeOptionsImportPicker is not null),
            ["canExport"] = Flag(source is not null && !ThemeSettingsBusy && _themeExportDirectoryPicker is not null),
            ["preview"] = Flag(_themeSession?.Snapshot.IsPreview == true),
            ["empty"] = Flag(source is not null && source.Manifest.Options.Count == 0),
        };
    }

    private Dictionary<string, string> ThemeIconData()
    {
        string mode = _values?.Values.FirstOrDefault(item => item.Key == "appearance.launcher-icon-mode")?.Value.Value ?? "theme";
        string value = _values?.Values.FirstOrDefault(item => item.Key == "appearance.launcher-icon-path")?.Value.Value ?? "";
        return new()
        {
            ["value"] = mode == "custom" ? value : "",
            ["theme"] = Flag(mode == "theme"),
            ["default"] = Flag(mode == "default"),
            ["enabled"] = Flag(!ThemeSettingsBusy),
            ["canPick"] = Flag(_themePicker is not null && _themeSession is not null && !ThemeSettingsBusy)
        };
    }

    private ThemeCatalogueEntry? PresentedTheme()
    {
        var snapshot = _themeSession?.Snapshot;
        string? source = snapshot?.PresentedSourceIdentity ?? snapshot?.Current.Selection?.SourceIdentity;
        return snapshot?.Catalogue.Themes.FirstOrDefault(item => item.SourceIdentity == source)
            ?? snapshot?.Catalogue.Themes.FirstOrDefault(item => item.SourceKind == ThemeSourceKind.BuiltIn);
    }

    private void ProjectThemeSettings()
    {
        if (!_shell.Tree.IsAlive(_themeRoot)) return;
        _themeActions.Clear();
        _dynamic.Reload(_themeRoot, DesktopMarkup.Dynamic("ThemeSettings.d.nxml", ThemeSettingsData()));
        _dynamic.Reload(_themeIconRoot, DesktopMarkup.Dynamic("ThemeLauncherIcon.d.nxml", ThemeIconData()));
        _dynamic.Reload(_themeOptionsRoot, DesktopMarkup.Dynamic("ThemeOptions.d.nxml", ThemeOptionsData()));
        BindThemeAction(_themeRoot, "Theme.ImportPackage", new(ThemeUiOperation.ImportPackage));
        BindThemeAction(_themeRoot, "Theme.ImportDirectory", new(ThemeUiOperation.ImportDirectory));
        BindThemeAction(_themeRoot, "Theme.Refresh", new(ThemeUiOperation.Refresh));
        BindThemeAction(_themeRoot, "Theme.EndPreview", new(ThemeUiOperation.EndPreview));
        BindThemeAction(_themeIconRoot, "Theme.IconDefault", new(ThemeUiOperation.IconDefault));
        BindThemeAction(_themeIconRoot, "Theme.IconHostDefault", new(ThemeUiOperation.IconHostDefault));
        BindThemeAction(_themeIconRoot, "Theme.IconPick", new(ThemeUiOperation.IconPick));
        var snapshot = _themeSession?.Snapshot;
        var source = PresentedTheme();
        _themePresentedIdentity = source?.SourceIdentity;
        ProjectThemeRows(snapshot);
        ProjectThemeOptions(source, snapshot);
        ProjectThemeGrants(source, snapshot);
        if (source is not null)
        {
            BindThemeAction(_themeOptionsRoot, "Theme.OptionsSave", new(ThemeUiOperation.Save, source.SourceIdentity));
            BindThemeAction(_themeOptionsRoot, "Theme.OptionsReset", new(ThemeUiOperation.Reset, source.SourceIdentity));
            BindThemeAction(_themeOptionsRoot, "Theme.OptionsExport", new(ThemeUiOperation.Export, source.SourceIdentity));
            BindThemeAction(_themeOptionsRoot, "Theme.OptionsImport", new(ThemeUiOperation.ImportOptions, source.SourceIdentity));
        }
        _themePresentationRevision = snapshot?.Revision ?? 0; _themePresentationLanguage = ThemeLanguage;
        _themeSettingsRevision = _revision;
    }

    private void BindThemeAction(XsrUiEntityId root, string key, ThemeUiAction action)
    {
        var nodes = CatalogNodes(root);
        if (nodes.TryGetValue(key, out var node)) _themeActions[node] = action;
    }

    private void ProjectThemeRows(DesktopThemeSettingsSnapshot? snapshot)
    {
        var entries = snapshot?.Catalogue.Themes ?? [];
        var wanted = entries.Select(item => item.SourceIdentity).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _themeRows.Keys.Where(key => !wanted.Contains(key)).ToArray())
        { _dynamic.Remove(_themeRows[stale]); _themeRows.Remove(stale); }
        var slot = CatalogNodes(_themeRoot)["Theme.Rows"];
        List<XsrUiEntityId> order = [];
        foreach (var entry in entries)
        {
            string prefix = "Theme.Entry." + ThemeIdentityKey(entry.SourceIdentity);
            bool selected = _themePresentedIdentity == entry.SourceIdentity;
            var data = new Dictionary<string, string>
            {
                ["key"] = prefix,
                ["name"] = ThemeLocalized(entry.Manifest.Name),
                ["version"] = entry.Manifest.Version,
                ["useKey"] = prefix + ".Use",
                ["previewKey"] = prefix + ".Preview",
                ["folderKey"] = prefix + ".Folder",
                ["removeKey"] = prefix + ".Remove",
                ["watchKey"] = prefix + ".Watch",
                ["selected"] = Flag(selected),
                ["enabled"] = Flag(!ThemeSettingsBusy),
                ["removable"] = Flag(entry.IsInstalled && entry.SourceKind != ThemeSourceKind.BuiltIn && !ThemeSettingsBusy),
                ["directory"] = Flag(entry.SourceKind == ThemeSourceKind.Directory),
                ["external"] = Flag(entry.SourceKind != ThemeSourceKind.BuiltIn),
                ["watching"] = Flag(_themeWatching.Contains(entry.SourceIdentity)),
            };
            var fragment = DesktopMarkup.Dynamic("ThemeEntry.d.nxml", data);
            if (_themeRows.TryGetValue(entry.SourceIdentity, out var root) && _shell.Tree.IsAlive(root)) _dynamic.Reload(root, fragment);
            else _themeRows[entry.SourceIdentity] = root = _dynamic.Append(slot, fragment);
            order.Add(root);
            BindThemeAction(root, prefix + ".Use", new(ThemeUiOperation.Activate, entry.SourceIdentity));
            BindThemeAction(root, prefix + ".Preview", new(ThemeUiOperation.Preview, entry.SourceIdentity));
            BindThemeAction(root, prefix + ".Folder", new(ThemeUiOperation.Folder, entry.SourceIdentity));
            BindThemeAction(root, prefix + ".Remove", new(ThemeUiOperation.Remove, entry.SourceIdentity));
            BindThemeAction(root, prefix + ".Watch", new(ThemeUiOperation.Watch, entry.SourceIdentity));
        }
        _dynamic.Reorder(slot, order);
    }

    private string ThemeLanguage => UiLocalizationCatalog.ResolveLanguage(
        _values?.Values.FirstOrDefault(item => item.Key == "general.language")?.Value.Value ?? "auto", CultureInfo.CurrentUICulture.Name);

    private string ThemeLocalized(IReadOnlyDictionary<string, string> values)
    {
        string locale = ThemeLanguage;
        if (values.TryGetValue(locale, out var exact)) return exact;
        string[] aliases = locale switch
        {
            "zh-Hans" => ["zh-CN", "zh-SG"],
            "zh-Hant" => ["zh-TW", "zh-HK", "zh-MO"],
            _ => ["en-US", "en-GB"],
        };
        foreach (string alias in aliases) if (values.TryGetValue(alias, out var compatible)) return compatible;
        string language = locale.Split('-')[0];
        if (values.TryGetValue(language, out var abbreviated)) return abbreviated;
        foreach (var pair in values) if (pair.Key.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase)) return pair.Value;
        if (values.TryGetValue("en", out var english)) return english;
        foreach (var pair in values) if (pair.Key.StartsWith("en-", StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return values.Values.FirstOrDefault() ?? "";
    }

    private void ResetThemeControls()
    {
        CaptureThemeDrafts();
        _themeRoot = _themeOptionsRoot = _themeIconRoot = _themeGrantRoot = default;
        _themeActions.Clear(); _themeRows.Clear(); _themeGrantRows.Clear(); _themeOptionEditors.Clear();
        _themeOptionInputs.Clear(); _themeFocusedDraft = null;
        _themePresentationRevision = -1; _themePresentedIdentity = null;
    }

    private void RetireThemeScope()
    {
        CaptureThemeDrafts();
        _themeScope++;
        _themeStop?.Cancel(); _themeStop?.Dispose(); _themeStop = null;
        if (_themeOperation is { } retired) ObserveTransfer(retired);
        _themeOperation = null;
        _themeOperationCompleted = null;
        _themeActions.Clear(); _themePresentationRevision = -1;
        if (_themeSession?.Snapshot.IsPreview == true) ObserveTransfer(_themeSession.EndPreviewAsync(CancellationToken.None));
    }
}
