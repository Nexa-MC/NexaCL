using Nexa.Services.Themes;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

/// <summary>Immutable presentation facts; Revision changes only when theme lifetime facts change.</summary>
internal sealed record DesktopThemeSettingsSnapshot(long Revision, ThemeCatalogueSnapshot Catalogue,
    ThemeCurrentSnapshot Current, string? PresentedSourceIdentity, bool IsPreview, bool Busy, string? Error,
    bool IsDark = false, string Platform = "unknown", IReadOnlySet<string>? Capabilities = null);

/// <summary>The composition layer coordinates style, resource and persistent-theme transactions.</summary>
internal interface IDesktopThemeSettingsSession
{
    DesktopThemeSettingsSnapshot Snapshot { get; }
    Task<XsrResult> RefreshAsync(CancellationToken token);
    Task<XsrResult> ImportAsync(string path, ThemeSourceKind kind, bool preview, CancellationToken token,
        Func<ThemeCandidate, CancellationToken, Task<bool>>? confirmDowngrade = null);
    Task<XsrResult> ActivateAsync(string sourceIdentity, bool preview, CancellationToken token);
    Task<XsrResult> EndPreviewAsync(CancellationToken token);
    Task<XsrResult> SaveOptionsAsync(ThemeOptionsSaveCommand command, CancellationToken token);
    Task<XsrResult> ResetOptionsAsync(ThemeOptionsResetCommand command, CancellationToken token);
    Task<XsrResult<ThemeOptionsExport>> ExportOptionsAsync(ThemeOptionsExportQuery query, CancellationToken token);
    Task<XsrResult> ImportOptionsAsync(ThemeOptionsImportCommand command, CancellationToken token);
    Task<XsrResult> GrantAsync(ThemeResourceGrantCommand command, CancellationToken token);
    Task<XsrResult> RevokeAsync(ThemeResourceRevokeCommand command, CancellationToken token);
    Task<XsrResult> RemoveAsync(ThemeRemoveCommand command, CancellationToken token);
    Task<XsrResult> WatchAsync(ThemeWatchCommand command, CancellationToken token);
    Task<XsrResult> OpenFolderAsync(string sourceIdentity, CancellationToken token);
}

/// <summary>Picker results represent explicit user consent; pasted paths never create grants.</summary>
internal sealed record DesktopThemePickerRequest(ThemeOptionKind Kind, ThemeResourceKind? ResourceKind, bool Package = false);
