using Nexa.Services.Settings;
using Nexa.Services.Themes;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Ui;

internal sealed partial class DesktopThemeSession
{
    private async Task<XsrResult> ExclusiveAsync(Func<long, CancellationToken, Task<XsrResult>> action,
        CancellationToken token, bool supersede = false)
    {
        CancellationToken requestToken; CancellationTokenSource? previousRequest = null; long request;
        lock (_requestGate)
        {
            if (_disposed) return XsrResult.Failure(XsrRuntimeErrors.Lifecycle());
            if (supersede)
            {
                previousRequest = _requestStop; _requestStop = new();
                request = Interlocked.Increment(ref _request);
            }
            else request = Volatile.Read(ref _request);
            requestToken = _requestStop.Token;
        }
        previousRequest?.Cancel(); previousRequest?.Dispose();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken, requestToken);
        bool admitted = false;
        try
        {
            await _operations.WaitAsync(linked.Token).ConfigureAwait(false); admitted = true;
            CheckRequest(request, linked.Token);
            SetSnapshot(busy: true);
            if (_themeRecoveryPending)
            {
                await RefreshSnapshotAsync(linked.Token).ConfigureAwait(false);
                var recovered = await RecoverBuiltInAsync(request, linked.Token).ConfigureAwait(false);
                if (!recovered.IsSuccess) { SetSnapshot(error: recovered.Error!.Message); return recovered; }
                _themeRecoveryPending = false;
                SetSnapshot();
            }
            var result = await action(request, linked.Token).ConfigureAwait(false);
            // A successful durable transaction remains successful if a later snapshot query is interrupted.
            if (result.IsSuccess && (linked.IsCancellationRequested || request != Volatile.Read(ref _request))) return result;
            await RefreshSnapshotAsync(linked.Token).ConfigureAwait(false);
            SetSnapshot(error: result.Error?.Message);
            return result;
        }
        catch (OperationCanceledException) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            _log(error.Message); SetSnapshot(error: error.Message); return Failure(error);
        }
        finally
        {
            if (admitted)
            {
                SetSnapshot(busy: false, error: Snapshot.Error);
                _operations.Release();
            }
        }
    }

    private void CheckRequest(long request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || request != Volatile.Read(ref _request)) throw new OperationCanceledException(token);
    }

    private static bool AppearancePolicyChanged(SettingsEffectiveSnapshot? before, SettingsEffectiveSnapshot after)
    {
        if (before is null) return true;
        // Source identity changes use the selection branch; its persisted mirror requires no new presentation.
        static bool RequiresRefresh(SettingsEffectiveValue value) => value.Key != "appearance.theme-pack-id"
            && (value.Key.StartsWith("appearance.", StringComparison.Ordinal) || value.Key.StartsWith("music.", StringComparison.Ordinal));
        var oldValues = before.Values.Where(RequiresRefresh).ToDictionary(value => value.Key, value => value.Value);
        return after.Values.Where(RequiresRefresh).Any(value => !oldValues.TryGetValue(value.Key, out var previous) || previous != value.Value);
    }

    private async Task RunUpdatesAsync()
    {
        try
        {
            await foreach (bool ignored in _updates.Reader.ReadAllAsync(_lifetimeToken).ConfigureAwait(false))
            {
                var result = await ExclusiveAsync(async (request, token) =>
                {
                    var settings = await CommittedSettingsRead.QueryAsync(_queries, token).ConfigureAwait(false)
                        ?? throw new IOException("Committed theme preferences are unavailable.");
                    bool changedPolicy = _baseRevision != settings.Revision && AppearancePolicyChanged(_baseSettings, settings);
                    _baseSettings = settings; _baseRevision = settings.Revision;
                    string? observedChoice = settings.Values.FirstOrDefault(value => value.Key == "appearance.theme-pack-id")?.Value.Value;
                    string? previousChoice = _policyChoice;
                    await RefreshSnapshotAsync(token).ConfigureAwait(false);
                    if (_active is null)
                    {
                        var legacy = settings.Values.Where(value => value.Key.StartsWith("appearance.", StringComparison.Ordinal)
                            || value.Key.StartsWith("music.", StringComparison.Ordinal)).ToDictionary(value => value.Key, value => value.Value.Value ?? "", StringComparer.Ordinal);
                        CheckRequest(request, token);
                        if (!_safeMode)
                        {
                            var migration = await CommandAsync(ThemesContract.Migrate, new ThemeLegacyMigrationCommand(legacy), token).ConfigureAwait(false);
                            if (!migration.IsSuccess) _log(migration.Error!.Message);
                        }
                        await RefreshSnapshotAsync(token).ConfigureAwait(false);
                        string? selected = _safeMode ? null : Snapshot.Current.Selection?.SourceIdentity;
                        var entry = Snapshot.Catalogue.Themes.FirstOrDefault(theme => theme.SourceIdentity == selected)
                            ?? Snapshot.Catalogue.Themes.FirstOrDefault(theme => theme.SourceKind == ThemeSourceKind.BuiltIn)
                            ?? throw new IOException("The built-in theme is unavailable.");
                        var initial = await ActivateSourceCoreAsync(entry, false, request, token).ConfigureAwait(false);
                        if (!initial.IsSuccess && entry.SourceKind != ThemeSourceKind.BuiltIn)
                            initial = await RecoverBuiltInAsync(request, token).ConfigureAwait(false);
                        if (!initial.IsSuccess) throw new IOException(initial.Error!.Message);
                    }
                    else
                    {
                        bool switched = false;
                        if (!_safeMode && _policyChoice is not null && observedChoice != _policyChoice)
                        {
                            var selected = Snapshot.Catalogue.Themes.FirstOrDefault(theme => theme.SourceIdentity == observedChoice || theme.Manifest.Id == observedChoice)
                                ?? Snapshot.Catalogue.Themes.FirstOrDefault(theme => observedChoice == "builtin" && theme.SourceKind == ThemeSourceKind.BuiltIn);
                            if (selected is not null && selected.SourceIdentity != _active.Candidate.SourceIdentity)
                            {
                                var activation = await ActivateSourceCoreAsync(selected, false, request, token).ConfigureAwait(false);
                                if (!activation.IsSuccess) return activation;
                                switched = true;
                            }
                        }
                        if (!switched && (changedPolicy || _appliedDark != IsDark))
                        {
                            var active = _active;
                            var candidate = await QueryAsync<ThemePrepareQuery, ThemeCandidate>(ThemesContract.Prepare,
                                new("", active.Candidate.SourceKind, active.Candidate.SourceIdentity) { OptionsOverride = _safeMode ? active.Candidate.Manifest.Options.ToDictionary(option => option.Id, option => option.Default, StringComparer.Ordinal) : active.Candidate.Options }, token).ConfigureAwait(false);
                            if (!candidate.IsSuccess) return XsrResult.Failure(candidate.Error!);
                            var refreshed = await ActivateCandidateCoreAsync(candidate.Value!, !ReferenceEquals(active, _committed), request, token).ConfigureAwait(false);
                            if (!refreshed.IsSuccess) return refreshed;
                        }
                    }
                    // MirrorSelection may have changed the desired identity during this very operation.
                    if (_policyChoice == previousChoice) _policyChoice = observedChoice;
                    return XsrResult.Success();
                }, _lifetimeToken).ConfigureAwait(false);
                if (result.Error?.Kind == XsrErrorKind.Cancelled && !_lifetimeToken.IsCancellationRequested)
                { _updates.Writer.TryWrite(true); continue; }
                if (!result.IsSuccess && _active is null) throw new IOException(result.Error!.Message);
                _initialReady.TrySetResult();
            }
        }
        catch (OperationCanceledException) { _initialReady.TrySetCanceled(_lifetimeToken); }
        catch (Exception error) { _initialReady.TrySetException(error); _appearance.FailInitial(error); }
    }

    private long NextGeneration() => Interlocked.Increment(ref _activation);
    private XsrUiThemeEnvironment Environment(PreparedTheme prepared) => new(PlatformName, IsDark,
        prepared.Appearance.Generation, Snapshot.Current.Revision, prepared.Candidate.Generation,
        prepared.Candidate.Generation, _capabilities);

    private async Task<XsrResult> ActivateSourceCoreAsync(ThemeCatalogueEntry entry, bool preview, long request, CancellationToken token, bool committedSource = false)
    {
        if (_safeMode && entry.SourceKind != ThemeSourceKind.BuiltIn)
            return XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("themes.safe_mode"), "Safe mode uses the built-in theme."));
        var candidate = await QueryAsync<ThemePrepareQuery, ThemeCandidate>(ThemesContract.Prepare, new ThemePrepareQuery("", entry.SourceKind, entry.SourceIdentity)
        {
            UseCommittedSource = committedSource,
            OptionsOverride = _safeMode ? entry.Manifest.Options.ToDictionary(option => option.Id, option => option.Default, StringComparer.Ordinal) : null,
        }, token).ConfigureAwait(false);
        if (!candidate.IsSuccess) return XsrResult.Failure(candidate.Error!);
        return await ActivateCandidateCoreAsync(candidate.Value!, preview, request, token).ConfigureAwait(false);
    }

    private bool RetainsCandidate(PreparedTheme prepared) => _active?.Candidate.CandidateId == prepared.Candidate.CandidateId
        || _committed?.Candidate.CandidateId == prepared.Candidate.CandidateId;

    private async Task<XsrResult> ActivateCandidateCoreAsync(ThemeCandidate candidate, bool preview, long request, CancellationToken token,
        Func<CancellationToken, Task>? persistOptions = null, bool allowDowngrade = false)
    {
        PreparedTheme? prepared = null;
        try
        {
            if (_safeMode && candidate.SourceKind != ThemeSourceKind.BuiltIn)
                return XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("themes.safe_mode"), "Safe mode uses the built-in theme."));
            prepared = await BuildPreparedAsync(candidate, NextGeneration(), token).ConfigureAwait(false);
            prepared = prepared with
            {
                DowngradeApproved = allowDowngrade || _active is { DowngradeApproved: true } approved
                    && approved.Candidate.SourceIdentity == candidate.SourceIdentity
                    && approved.Candidate.SourceRevision == candidate.SourceRevision
                    && approved.Candidate.PreviousVersion == candidate.PreviousVersion
            };
            CheckRequest(request, token);
            await ApplyPreparedAsync(prepared, !preview, !preview && !_safeMode, request, token, persistOptions: _safeMode ? null : persistOptions).ConfigureAwait(false);
            prepared = null;
            return XsrResult.Success();
        }
        finally
        {
            if (prepared is not null && !RetainsCandidate(prepared)) await RetirePreparedAsync(prepared).ConfigureAwait(false);
            else if (prepared is null && _active?.Candidate.CandidateId != candidate.CandidateId && _committed?.Candidate.CandidateId != candidate.CandidateId)
                await CommandAsync(ThemesContract.Revert, new ThemeRevertCommand(candidate.CandidateId), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RetirePreparedAsync(PreparedTheme prepared)
    {
        if (RetainsCandidate(prepared)) return;
        await ReleasePreparedAsync(prepared, CancellationToken.None).ConfigureAwait(false);
        var retired = await CommandAsync(ThemesContract.Revert, new ThemeRevertCommand(prepared.Candidate.CandidateId), CancellationToken.None).ConfigureAwait(false);
        if (!retired.IsSuccess && retired.Error?.Kind != XsrErrorKind.Rejected) _log(retired.Error!.Message);
    }

    private async Task ApplyPreparedAsync(PreparedTheme candidate, bool committed, bool persist,
        long request, CancellationToken token, bool compensation = false, Func<CancellationToken, Task>? persistOptions = null)
    {
        PreparedTheme? previous = _active, previousCommitted = _committed;
        var native = await _platform.PrepareThemeAppearanceAsync(candidate.NativeAppearance, token).ConfigureAwait(false);
        AvaloniaUiPreparedThemeAppearance? oldNative = null;
        bool transferred = false, durable = false;
        try
        {
            await OnRenderAsync(() =>
            {
                if (!compensation) CheckRequest(request, token);
                if (candidate.Program.RuleCount > 0) _shell.Renderer.ValidateTheme(candidate.Program, Environment(candidate));
                oldNative = _platform.CommitThemeAppearance(native); transferred = true;
                if (candidate.Program.RuleCount == 0) _shell.Renderer.ClearTheme(candidate.Appearance.Generation);
                else _shell.Renderer.ApplyTheme(candidate.Program, candidate.Appearance.Generation, Environment(candidate));
                _shell.Renderer.TextLayoutProvider = candidate.Candidate.SourceKind == ThemeSourceKind.BuiltIn ? null : native.TextLayout;
                _active = candidate; _appliedDark = IsDark; StartupSnapshot = native.StartupSnapshot;
                _appearance.Publish(candidate.Appearance);
                SetSnapshot();
            }, token).ConfigureAwait(false);
            if (WaitForAppearanceConsumers is { } wait) await wait(candidate.Appearance.Generation, token).ConfigureAwait(false);
            if (!compensation) CheckRequest(request, token);
            var current = await QueryAsync<ThemeCurrentQuery, ThemeCurrentSnapshot>(ThemesContract.Current, new(), token).ConfigureAwait(false);
            if (!current.IsSuccess) throw new IOException(current.Error!.Message);
            if (!compensation) CheckRequest(request, token);
            var activated = await CommandAsync(ThemesContract.Commit,
                new ThemeCommitCommand(candidate.Candidate.CandidateId, current.Value!.Revision, Preview: !persist)
                { AllowDowngrade = candidate.DowngradeApproved }, token).ConfigureAwait(false);
            if (!activated.IsSuccess) throw new IOException(activated.Error!.Message);
            if (persist)
            {
                durable = true;
            }
            else if (persistOptions is not null)
            {
                CheckRequest(request, token);
                await persistOptions(token).ConfigureAwait(false); durable = true;
            }
            if (committed) _committed = candidate;
        }
        catch
        {
            if (!transferred) native.Dispose();
            else if (!_disposed && !_themeRecoveryPending && !durable && !compensation && previous is not null)
            {
                long generation = NextGeneration();
                var rollback = previous with
                {
                    Appearance = previous.Appearance with { Generation = generation },
                    NativeAppearance = previous.NativeAppearance with { Generation = generation }
                };
                await ApplyPreparedAsync(rollback, ReferenceEquals(previous, previousCommitted), false,
                    Volatile.Read(ref _request), CancellationToken.None, compensation: true).ConfigureAwait(false);
            }
            else if (!_disposed && !_themeRecoveryPending && !durable && !compensation && previous is null)
            {
                await RestoreProductBasisAsync().ConfigureAwait(false);
            }
            oldNative?.Dispose();
            throw;
        }
        // The irreversible boundary is complete. Cleanup and selection mirroring must never roll it back.
        oldNative?.Dispose();
        foreach (var obsolete in new[] { previous, committed ? previousCommitted : null }.OfType<PreparedTheme>().DistinctBy(value => value.Candidate.CandidateId))
            if (!RetainsCandidate(obsolete))
                try { await RetirePreparedAsync(obsolete).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or OperationCanceledException)
                { _log("Theme retirement failed after activation: " + error.Message); }
        if (persist)
            try { await MirrorSelectionAsync(candidate.Candidate.SourceIdentity, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or OperationCanceledException)
            { _log("Theme selection mirror failed after a successful commit: " + error.Message); }
        if (persist && native.StartupSnapshot is { } startup && SaveCommittedStartup is { } save)
            try { await save(candidate.Candidate.SourceIdentity, startup).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
            { _log("Theme startup cache update failed after a successful commit: " + error.Message); }
    }

    private async Task RestoreProductBasisAsync()
    {
        long generation = NextGeneration();
        var prepared = await _platform.PrepareThemeAppearanceAsync(new(generation, UseNativeTextLayout: false), CancellationToken.None).ConfigureAwait(false);
        await OnRenderAsync(() =>
        {
            _platform.CommitThemeAppearance(prepared)?.Dispose();
            _shell.Renderer.ClearTheme(generation); _shell.Renderer.TextLayoutProvider = null;
            _active = _committed = null;
            if (_baseSettings is { } basis)
            {
                var values = basis.Values.Select(value => value.Key is "appearance.logo-path" or "appearance.background-path" or "appearance.video-path" or "music.path"
                    ? value with { Value = new(SettingsOverrideMode.Custom, "") } : value).ToArray();
                _appearance.Publish(new(generation, basis with { Values = values }, [], ThemeResources: true, HasPreparedImages: true));
            }
        }, CancellationToken.None).ConfigureAwait(false);
        if (WaitForAppearanceConsumers is { } wait) await wait(generation, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task MirrorSelectionAsync(string identity, CancellationToken token)
    {
        if (_safeMode) return;
        if (_baseSettings?.Values.FirstOrDefault(value => value.Key == "appearance.theme-pack-id")?.Value.Value == identity)
        { _policyChoice = identity; return; }
        var result = await CommandAsync(SettingsPolicyContract.SetCommand,
            new SettingsMutation("appearance.theme-pack-id", SettingsLayer.Global, new(SettingsOverrideMode.Custom, identity)), token).ConfigureAwait(false);
        if (result.IsSuccess) _policyChoice = identity;
        else _log("Theme selection mirror failed: " + result.Error!.Message);
    }

    private async Task<XsrResult> RecoverBuiltInAsync(long request, CancellationToken token)
    {
        var builtin = Snapshot.Catalogue.Themes.First(theme => theme.SourceKind == ThemeSourceKind.BuiltIn);
        var defaults = builtin.Manifest.Options.Where(option => option.Kind is ThemeOptionKind.File or ThemeOptionKind.Files or ThemeOptionKind.Directory)
            .ToDictionary(option => option.Id, option => option.Default, StringComparer.Ordinal);
        var candidate = await QueryAsync<ThemePrepareQuery, ThemeCandidate>(ThemesContract.Prepare,
            new("", builtin.SourceKind, builtin.SourceIdentity) { OptionsOverride = defaults }, token).ConfigureAwait(false);
        if (!candidate.IsSuccess) return XsrResult.Failure(candidate.Error!);
        var basis = _baseSettings;
        if (basis is not null)
            _baseSettings = basis with
            {
                Values = basis.Values.Select(value => value.Key == "appearance.logo-path"
                ? value with { Value = new(SettingsOverrideMode.Custom, "") } : value).ToArray()
            };
        try { return await ActivateCandidateCoreAsync(candidate.Value!, false, request, token).ConfigureAwait(false); }
        finally { _baseSettings = basis; }
    }

    private Task<XsrResult> ActivateWatchedAsync(Guid candidate, CancellationToken token) => ExclusiveAsync(async (request, cancellation) =>
    {
        var prepared = await QueryAsync<ThemeCandidateQuery, ThemeCandidate>(ThemesContract.Candidate, new(candidate), cancellation).ConfigureAwait(false);
        if (!prepared.IsSuccess) return XsrResult.Failure(prepared.Error!);
        return await ActivateCandidateCoreAsync(prepared.Value!, Snapshot.IsPreview, request, cancellation).ConfigureAwait(false);
    }, token, true);
}
