using Nexa.Services.Settings;
using Nexa.Services.Themes;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class DesktopThemeSession
{
    public Task<XsrResult> RefreshAsync(CancellationToken token) => ExclusiveAsync(async (_, cancellation) =>
    { await RefreshSnapshotAsync(cancellation).ConfigureAwait(false); return XsrResult.Success(); }, token);

    public Task<XsrResult> ImportAsync(string path, ThemeSourceKind kind, bool preview, CancellationToken token) =>
        ExclusiveAsync(async (request, cancellation) =>
        {
            if (_safeMode) return XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("themes.safe_mode"), "Safe mode uses the built-in theme."));
            var candidate = await QueryAsync<ThemePrepareQuery, ThemeCandidate>(ThemesContract.Prepare, new(path, kind), cancellation).ConfigureAwait(false);
            if (!candidate.IsSuccess) return XsrResult.Failure(candidate.Error!);
            return await ActivateCandidateCoreAsync(candidate.Value!, preview, request, cancellation).ConfigureAwait(false);
        }, token, true);

    public Task<XsrResult> ActivateAsync(string sourceIdentity, bool preview, CancellationToken token) =>
        ExclusiveAsync(async (request, cancellation) =>
        {
            await RefreshSnapshotAsync(cancellation).ConfigureAwait(false);
            var entry = Snapshot.Catalogue.Themes.FirstOrDefault(theme => theme.SourceIdentity == sourceIdentity);
            if (entry is null) return XsrResult.Failure(XsrRuntimeErrors.TargetNotFound("The theme is no longer installed."));
            return await ActivateSourceCoreAsync(entry, preview, request, cancellation).ConfigureAwait(false);
        }, token, true);

    public Task<XsrResult> EndPreviewAsync(CancellationToken token) => ExclusiveAsync(async (request, cancellation) =>
    {
        if (_active is null || ReferenceEquals(_active, _committed)) return XsrResult.Success();
        var committed = _committed;
        var entry = Snapshot.Catalogue.Themes.FirstOrDefault(theme => theme.SourceIdentity == committed?.Candidate.SourceIdentity)
            ?? Snapshot.Catalogue.Themes.First(theme => theme.SourceKind == ThemeSourceKind.BuiltIn);
        // Reprepare from the immutable committed source and current saved options/environment.
        return await ActivateSourceCoreAsync(entry, false, request, cancellation, committedSource: true).ConfigureAwait(false);
    }, token, true);

    public Task<XsrResult> SaveOptionsAsync(ThemeOptionsSaveCommand command, CancellationToken token) =>
        PrepareOptionsAsync(command.SourceIdentity, command.ExpectedRevision, command.Values, null, token);

    public Task<XsrResult> ResetOptionsAsync(ThemeOptionsResetCommand command, CancellationToken token)
    {
        var source = Snapshot.Catalogue.Themes.FirstOrDefault(theme => theme.SourceIdentity == command.SourceIdentity);
        return source is null ? Task.FromResult(XsrResult.Failure(XsrRuntimeErrors.TargetNotFound("The theme is no longer installed.")))
            : PrepareOptionsAsync(command.SourceIdentity, command.ExpectedRevision,
                source.Manifest.Options.ToDictionary(option => option.Id, option => option.Default, StringComparer.Ordinal), null, token);
    }

    public Task<XsrResult> ImportOptionsAsync(ThemeOptionsImportCommand command, CancellationToken token) =>
        PrepareOptionsAsync(command.SourceIdentity, command.ExpectedRevision, null, command.Json, token);

    private Task<XsrResult> PrepareOptionsAsync(string identity, long revision,
        IReadOnlyDictionary<string, ThemeOptionValue>? overrides, string? document, CancellationToken token) =>
        ExclusiveAsync((request, cancellation) => PrepareOptionsCoreAsync(identity, revision, overrides, document, request, cancellation), token, true);

    private async Task<XsrResult> PrepareOptionsCoreAsync(string identity, long revision,
        IReadOnlyDictionary<string, ThemeOptionValue>? overrides, string? document, long request, CancellationToken token)
    {
        if (_safeMode) return XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("themes.safe_mode"), "Safe mode uses the built-in defaults."));
        await RefreshSnapshotAsync(token).ConfigureAwait(false);
        var source = Snapshot.Catalogue.Themes.FirstOrDefault(theme => theme.SourceIdentity == identity);
        if (source is null) return XsrResult.Failure(XsrRuntimeErrors.TargetNotFound("The theme is no longer installed."));
        var candidate = await QueryAsync<ThemePrepareQuery, ThemeCandidate>(ThemesContract.Prepare,
            new ThemePrepareQuery("", source.SourceKind, identity) { OptionsOverride = overrides, OptionsDocument = document, ExpectedRevision = revision }, token).ConfigureAwait(false);
        if (!candidate.IsSuccess) return XsrResult.Failure(candidate.Error!);
        bool preview = Snapshot.IsPreview;
        async Task SavePreviewOptions(CancellationToken cancellation)
        {
            CheckRequest(request, cancellation);
            var result = await CommandAsync(ThemesContract.SaveOptions,
                new ThemeOptionsSaveCommand(identity, candidate.Value!.Options, revision), cancellation).ConfigureAwait(false);
            if (!result.IsSuccess) throw new IOException(result.Error!.Message);
        }
        return await ActivateCandidateCoreAsync(candidate.Value!, preview, request, token,
            preview ? SavePreviewOptions : null).ConfigureAwait(false);
    }

    internal Task<XsrResult> SetMediaVolumeAsync(double volume, CancellationToken token)
    {
        long generation = _appearance.Current?.Generation ?? -1;
        string? sourceIdentity = Snapshot.PresentedSourceIdentity;
        return ExclusiveAsync(async (request, cancellation) =>
        {
            if (!double.IsFinite(volume) || volume is < 0 or > 1) return Failure(new ArgumentOutOfRangeException(nameof(volume)));
            if (_appearance.Current?.Generation != generation || _active is not { } active || active.Candidate.SourceIdentity != sourceIdentity)
                return XsrResult.Failure(XsrRuntimeErrors.Cancelled());
            var option = active.Candidate.Manifest.Options.FirstOrDefault(value => value.Id == "music.volume");
            if (option is not null && option.Kind is ThemeOptionKind.Number or ThemeOptionKind.Range)
            {
                double minimum = option.Minimum ?? 0, maximum = option.Maximum ?? 100, step = option.Step ?? 1;
                double value = Math.Clamp(minimum + Math.Round((Math.Clamp(volume * 100, minimum, maximum) - minimum) / step,
                    MidpointRounding.AwayFromZero) * step, minimum, maximum);
                await RefreshSnapshotAsync(cancellation).ConfigureAwait(false);
                return await PrepareOptionsCoreAsync(sourceIdentity!, Snapshot.Current.Revision,
                    new Dictionary<string, ThemeOptionValue> { [option.Id] = new(option.Kind, value.ToString(System.Globalization.CultureInfo.InvariantCulture)) },
                    null, request, cancellation).ConfigureAwait(false);
            }
            CheckRequest(request, cancellation);
            return await CommandAsync(SettingsPolicyContract.SetCommand,
                new SettingsMutation("music.volume", SettingsLayer.Global,
                    new(SettingsOverrideMode.Custom, Math.Round(volume * 100, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture))), cancellation).ConfigureAwait(false);
        }, token);
    }

    public async Task<XsrResult<ThemeOptionsExport>> ExportOptionsAsync(ThemeOptionsExportQuery query, CancellationToken token) =>
        await QueryAsync<ThemeOptionsExportQuery, ThemeOptionsExport>(ThemesContract.ExportOptions, query, token).ConfigureAwait(false);

    public Task<XsrResult> GrantAsync(ThemeResourceGrantCommand command, CancellationToken token) =>
        ExclusiveAsync((_, cancellation) => CommandAsync(ThemesContract.Grant, command, cancellation), token);

    public Task<XsrResult> RevokeAsync(ThemeResourceRevokeCommand command, CancellationToken token) =>
        ExclusiveAsync(async (request, cancellation) =>
        {
            long retiredGeneration = _appearance.Current?.Generation ?? 0;
            var grant = Snapshot.Current.Grants.FirstOrDefault(item => item.GrantId == command.GrantId);
            if (grant is not null && (_active?.Candidate.SourceIdentity == grant.SourceIdentity || _committed?.Candidate.SourceIdentity == grant.SourceIdentity))
            {
                var recovered = await RecoverBuiltInAsync(request, cancellation).ConfigureAwait(false);
                if (!recovered.IsSuccess) return recovered;
            }
            CheckRequest(request, cancellation);
            await FlushRetiredNativeAsync(retiredGeneration, cancellation).ConfigureAwait(false);
            return await CommandAsync(ThemesContract.Revoke, command, cancellation).ConfigureAwait(false);
        }, token, true);

    public Task<XsrResult> RemoveAsync(ThemeRemoveCommand command, CancellationToken token) =>
        ExclusiveAsync(async (request, cancellation) =>
        {
            long retiredGeneration = _appearance.Current?.Generation ?? 0;
            if (_active?.Candidate.SourceIdentity == command.SourceIdentity || _committed?.Candidate.SourceIdentity == command.SourceIdentity)
            {
                var recovered = await RecoverBuiltInAsync(request, cancellation).ConfigureAwait(false);
                if (!recovered.IsSuccess) return recovered;
            }
            CheckRequest(request, cancellation);
            await FlushRetiredNativeAsync(retiredGeneration, cancellation).ConfigureAwait(false);
            return await CommandAsync(ThemesContract.Remove, command, cancellation).ConfigureAwait(false);
        }, token, true);

    private Task FlushRetiredNativeAsync(long retiredGeneration, CancellationToken token) => OnRenderAsync(() =>
    {
        long cutoff = Math.Min(retiredGeneration, _platform.ThemeAppearanceGeneration - 1);
        if (cutoff >= 0) _platform.FlushRetiredThemeAppearanceResources(cutoff);
    }, token);

    public Task<XsrResult> WatchAsync(ThemeWatchCommand command, CancellationToken token) =>
        _safeMode && command.Enabled ? Task.FromResult(XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("themes.safe_mode"), "Safe mode bypasses theme watchers.")))
            : ExclusiveAsync((_, cancellation) => CommandAsync(ThemesContract.Watch, command, cancellation), token);

    public async Task<XsrResult> OpenFolderAsync(string sourceIdentity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var source = Snapshot.Catalogue.Themes.FirstOrDefault(theme => theme.SourceIdentity == sourceIdentity);
        if ((source?.DevelopmentDirectory ?? source?.InstalledDirectory) is not { } path)
            return XsrResult.Failure(XsrRuntimeErrors.TargetNotFound("This theme has no installed directory."));
        try { await OnRenderAsync(() => _platform.OpenDirectory(path), token).ConfigureAwait(false); return XsrResult.Success(); }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException) { return Failure(error); }
    }
}
