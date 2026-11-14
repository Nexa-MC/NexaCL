using Nexa.Services.Themes;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class DesktopThemeSession
{
    private long _externalPreviewLease, _nextExternalPreviewLease;
    private XsrResult RetireExternalPreview(XsrResult result)
    { if (result.IsSuccess) _externalPreviewLease = 0; return result; }
    internal async Task<XsrResult<long>> ActivateExternalAsync(string sourceIdentity, string sourceRevision, long expectedRevision, bool preview, CancellationToken token)
    {
        long generation = 0;
        var result = await ExclusiveAsync(async (request, cancellation) =>
        {
            await RefreshSnapshotAsync(cancellation).ConfigureAwait(false);
            var entry = Snapshot.Catalogue.Themes.FirstOrDefault(t => t.SourceIdentity == sourceIdentity);
            if (entry is null || entry.SourceRevision != sourceRevision || Snapshot.Current.Revision != expectedRevision)
                return Failure(new IOException("The theme changed after activation was prepared."));
            var candidate = await QueryAsync<ThemePrepareQuery, ThemeCandidate>(ThemesContract.Prepare,
                new("", entry.SourceKind, sourceIdentity, ExpectedRevision: expectedRevision), cancellation).ConfigureAwait(false);
            if (!candidate.IsSuccess) return XsrResult.Failure(candidate.Error!);
            if (candidate.Value!.SourceRevision != sourceRevision)
            {
                await CommandAsync(ThemesContract.Revert, new ThemeRevertCommand(candidate.Value.CandidateId), CancellationToken.None).ConfigureAwait(false);
                return Failure(new IOException("The theme source changed during preparation."));
            }
            var activated = await ActivateCandidateCoreAsync(candidate.Value, preview, request, cancellation).ConfigureAwait(false);
            if (activated.IsSuccess) { generation = ++_nextExternalPreviewLease; _externalPreviewLease = preview ? generation : 0; }
            return activated;
        }, token).ConfigureAwait(false);
        return result.IsSuccess ? XsrResult.Success(generation) : XsrResult.Failure<long>(result.Error!);
    }
    internal async Task<XsrResult> EndExternalPreviewAsync(long generation, CancellationToken token)
    {
        while (true)
        {
            lock (_requestGate) if (_disposed) return XsrResult.Success();
            var result = await EndExternalPreviewCoreAsync(generation, token).ConfigureAwait(false);
            if (result.IsSuccess || result.Error!.Kind != XsrErrorKind.Cancelled || token.IsCancellationRequested) return result;
            // A newer UI request superseded restoration. Wait behind it and recheck
            // the exact lease: failed user actions must not strand the old preview.
            await Task.Yield();
        }
    }
    private Task<XsrResult> EndExternalPreviewCoreAsync(long generation, CancellationToken token) => ExclusiveAsync(async (request, cancellation) =>
    {
        if (_externalPreviewLease != generation || _active is null || ReferenceEquals(_active, _committed)) return XsrResult.Success();
        var entry = Snapshot.Catalogue.Themes.FirstOrDefault(theme => theme.SourceIdentity == _committed?.Candidate.SourceIdentity)
            ?? Snapshot.Catalogue.Themes.First(theme => theme.SourceKind == ThemeSourceKind.BuiltIn);
        var restored = await ActivateSourceCoreAsync(entry, false, request, cancellation, committedSource: true).ConfigureAwait(false);
        if (!restored.IsSuccess)
        {
            CheckRequest(request, cancellation);
            _log("The committed theme could not be restored after preview; restoring the built-in theme.");
            restored = await RecoverBuiltInAsync(request, cancellation).ConfigureAwait(false);
        }
        return RetireExternalPreview(restored);
    }, token);
}
