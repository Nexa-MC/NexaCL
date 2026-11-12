using Nexa.Services.Themes;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class DesktopThemeSession
{
    private long _pluginPreviewLease, _nextPluginPreviewLease;
    private XsrResult RetirePluginPreview(XsrResult result)
    { if (result.IsSuccess) _pluginPreviewLease = 0; return result; }
    internal async Task<XsrResult<long>> ActivatePluginAsync(string sourceIdentity, string sourceRevision, long expectedRevision, bool preview, CancellationToken token)
    {
        long generation = 0;
        var result = await ExclusiveAsync(async (request, cancellation) =>
        {
            await RefreshSnapshotAsync(cancellation).ConfigureAwait(false);
            var entry = Snapshot.Catalogue.Themes.FirstOrDefault(t => t.SourceIdentity == sourceIdentity);
            if (entry is null || entry.SourceRevision != sourceRevision || Snapshot.Current.Revision != expectedRevision)
                return Failure(new IOException("The theme changed after the plugin preview was prepared."));
            var candidate = await QueryAsync<ThemePrepareQuery, ThemeCandidate>(ThemesContract.Prepare,
                new("", entry.SourceKind, sourceIdentity, ExpectedRevision: expectedRevision), cancellation).ConfigureAwait(false);
            if (!candidate.IsSuccess) return XsrResult.Failure(candidate.Error!);
            if (candidate.Value!.SourceRevision != sourceRevision)
            {
                await CommandAsync(ThemesContract.Revert, new ThemeRevertCommand(candidate.Value.CandidateId), CancellationToken.None).ConfigureAwait(false);
                return Failure(new IOException("The theme source changed during preparation."));
            }
            var activated = await ActivateCandidateCoreAsync(candidate.Value, preview, request, cancellation).ConfigureAwait(false);
            if (activated.IsSuccess) { generation = ++_nextPluginPreviewLease; _pluginPreviewLease = preview ? generation : 0; }
            return activated;
        }, token).ConfigureAwait(false);
        return result.IsSuccess ? XsrResult.Success(generation) : XsrResult.Failure<long>(result.Error!);
    }
    internal Task<XsrResult> EndPluginPreviewAsync(long generation, CancellationToken token) => ExclusiveAsync(async (request, cancellation) =>
    {
        if (_pluginPreviewLease != generation || _active is null || ReferenceEquals(_active, _committed)) return XsrResult.Success();
        var entry = Snapshot.Catalogue.Themes.FirstOrDefault(theme => theme.SourceIdentity == _committed?.Candidate.SourceIdentity)
            ?? Snapshot.Catalogue.Themes.First(theme => theme.SourceKind == ThemeSourceKind.BuiltIn);
        return RetirePluginPreview(await ActivateSourceCoreAsync(entry, false, request, cancellation, committedSource: true).ConfigureAwait(false));
    }, token);
}
