using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{
    private readonly XsrQueryRouter? _recoveryQueries;

    private bool TryShowCrashDialog(MinecraftProcessFailure failure)
    {
        string reason = ProductPresentationText.Format("CrashReason", _shell.Renderer.LocalizeText, ("summary", CrashSummary(failure.Report)), ("message", failure.Report.Message));
        if (!_feedback.TryShowMessageDialog("minecraft.crash." + failure.SessionId,
            ProductPresentationText.Format("CrashTitle", _shell.Renderer.LocalizeText, ("instance", failure.InstanceId)),
            ProductPresentationText.Format("CrashComparing", _shell.Renderer.LocalizeText, ("reason", reason)), ProductPresentationText.Raw("crash.acknowledge"), out var id, localizeMessage: false, localizeTitle: false, isLoading: true))
            return false;
        _ = LoadCrashChangesAsync(failure, id, reason);
        return true;
    }

    private async Task LoadCrashChangesAsync(MinecraftProcessFailure failure, Guid dialogId, string reason)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(failure.InstanceDirectory) || _recoveryQueries is null
                || !_recoveryQueries.TryResolve(InstanceRecoveryContract.Query, out var route))
            {
                _feedback.TryUpdateMessageDialog(dialogId, ProductPresentationText.Format("CrashMissingInstance", _shell.Renderer.LocalizeText, ("reason", reason)), localizeMessage: false);
                return;
            }
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            void CancelIfClosed(object? sender, EventArgs args)
            {
                if (_feedback.Snapshot().Dialog?.Id == dialogId) return;
                try { stop.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            _feedback.Changed += CancelIfClosed;
            Nexa.Xsr.XsrResult<InstanceRecoveryReport> result;
            try
            {
                CancelIfClosed(null, EventArgs.Empty);
                // Dispatch away from render even if a query handler has synchronous preparation.
                result = await Task.Run(async () => await _recoveryQueries
                    .QueryAsync<InstanceRecoveryQuery, InstanceRecoveryReport>(route, new(failure.InstanceDirectory),
                        cancellationToken: stop.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }
            finally { _feedback.Changed -= CancelIfClosed; }
            if (_disposed) return;
            if (!result.IsSuccess)
            {
                _feedback.TryUpdateMessageDialog(dialogId, ProductPresentationText.Format("CrashComparisonFailed", _shell.Renderer.LocalizeText, ("reason", reason), ("error", result.Error?.Message ?? "")), localizeMessage: false);
                return;
            }
            new CrashChangesPresentation(_feedback, dialogId, reason, result.Value!,
                () => ShowCrashRecoveryChoices(failure, reason, result.Value!), _shell.Renderer.LocalizeText).Show();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            _feedback.TryUpdateMessageDialog(dialogId, ProductPresentationText.Format("CrashComparisonUnreadable", _shell.Renderer.LocalizeText, ("reason", reason)), localizeMessage: false);
        }
    }

    private void ShowCrashRecoveryChoices(MinecraftProcessFailure failure, string reason, InstanceRecoveryReport report, int index = -1, int categoryIndex = -1)
    {
        if (_disposed || report.BaselineRevision is not { } revision || report.Changes.Count == 0
            || !_foundationCommands.TryResolve(InstanceRecoveryContract.Restore, out var route)) return;
        var categories = report.Changes.GroupBy(change => change.Category).OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        IReadOnlyList<InstanceRecoveryChange> changes = categoryIndex >= 0 ? categories[categoryIndex].ToArray()
            : index < 0 ? report.Changes : [report.Changes[index]];
        string description = categoryIndex >= 0 ? ProductPresentationText.Format("CrashRestoreCategory", _shell.Renderer.LocalizeText,
                ("category", RecoveryExplanation.Display(categories[categoryIndex].Key)), ("count", changes.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)))
            : index < 0 ? ProductPresentationText.Format("CrashRestoreAll", _shell.Renderer.LocalizeText, ("count", changes.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)))
            : ProductPresentationText.Format("CrashRestoreItem", _shell.Renderer.LocalizeText,
                ("index", (index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture)), ("count", report.Changes.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                ("category", RecoveryExplanation.Display(changes[0].Category)), ("path", RecoveryExplanation.Display(changes[0].Path)));
        _feedback.ShowDialog("recovery.crash.choose", ProductPresentationText.Raw("crash.restoreTitle"),
            ProductPresentationText.Format("CrashRestoreBody", _shell.Renderer.LocalizeText, ("reason", reason), ("description", description)),
            ProductPresentationText.Raw(categoryIndex >= 0 ? "crash.restoreCategory" : index < 0 ? "crash.restoreAll" : "crash.restoreItem"), ProductPresentationText.Raw("crash.cancel"),
            accepted =>
            {
                if (accepted) _ = RestoreCrashChangesAsync(failure, report, revision, changes, route);
            }, categoryIndex >= 0 ? (categoryIndex + 1 < categories.Length ? ProductPresentationText.Raw("crash.nextCategory") : ProductPresentationText.Raw("crash.chooseItems"))
                : index < 0 ? ProductPresentationText.Raw("crash.chooseCategories") : ProductPresentationText.Raw("crash.nextItem"), () =>
            {
                // A new dialog lifetime is needed: same-key updates intentionally retain callbacks.
                if (_feedback.Snapshot().Dialog is { } dialog) _feedback.DismissDialog(dialog.Id);
                if (categoryIndex >= 0)
                {
                    if (categoryIndex + 1 < categories.Length) ShowCrashRecoveryChoices(failure, reason, report, categoryIndex: categoryIndex + 1);
                    else ShowCrashRecoveryChoices(failure, reason, report, 0);
                }
                else if (index < 0) ShowCrashRecoveryChoices(failure, reason, report, categoryIndex: 0);
                else ShowCrashRecoveryChoices(failure, reason, report, (index + 1) % report.Changes.Count);
            }, localizeMessage: false);
    }

    private async Task RestoreCrashChangesAsync(MinecraftProcessFailure failure, InstanceRecoveryReport report, Guid revision,
        IReadOnlyList<InstanceRecoveryChange> changes, Nexa.Xsr.XsrCommandId route)
    {
        var result = await _foundationCommands.Dispatch(route, new InstanceRecoveryRestoreCommand(report.InstanceDirectory,
            revision, report.Fingerprint, changes.ToArray()), cancellationToken: _lifetimeCancellation.Token).Completion.ConfigureAwait(false);
        if (_disposed) return;
        if (result.IsSuccess)
        {
            _feedback.Success(ProductPresentationText.Raw("crash.restored"));
            if (_libraryCommands.TryResolve(Nexa.Services.Minecraft.MinecraftLibraryRoutes.Refresh, out var refresh))
                _ = _libraryCommands.Dispatch(refresh, new Nexa.Services.Minecraft.MinecraftLibraryRefreshCommand());
        }
        else _feedback.Error(result.Error?.Message ?? ProductPresentationText.Raw("crash.restoreError"));
        TryShowCrashDialog(failure);
    }
}

// Bounded pages keep wrapping/layout work independent of the size of a modpack.
// Every change remains reachable, without creating thousands of UI entities.
internal sealed class CrashChangesPresentation(DesktopFeedbackService feedback, Guid dialogId,
    string reason, InstanceRecoveryReport report, Action? restore = null, Func<string, string>? localize = null)
{
    internal const int PageSize = 12;
    private int _page;

    public void Show()
    {
        if (report.UnavailableReason is { } unavailable)
        {
            feedback.TryUpdateMessageDialog(dialogId, ProductPresentationText.Format("CrashUnavailable", localize, ("reason", reason), ("unavailable", unavailable)), localizeMessage: false);
            return;
        }
        int count = report.Changes.Count, pages = Math.Max(1, (count + PageSize - 1) / PageSize);
        string heading = count == 0 ? ProductPresentationText.Term("crash.noChanges", localize)
            : ProductPresentationText.Format("CrashChangesHeading", localize, ("count", count.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                ("page", (_page + 1).ToString(System.Globalization.CultureInfo.CurrentCulture)), ("pages", pages.ToString(System.Globalization.CultureInfo.CurrentCulture)));
        var rows = report.Changes.Skip(_page * PageSize).Take(PageSize).Select(change =>
            ProductPresentationText.Format("CrashChangesRow", localize, ("kind", RecoveryExplanation.Kind(change.Kind, localize)),
                ("category", RecoveryExplanation.Category(change.Category, localize)), ("path", RecoveryExplanation.Display(change.Path))));
        string body = ProductPresentationText.Format("CrashChangesBody", localize, ("reason", reason), ("summary", RecoveryExplanation.Summary(report.Changes, localize)),
            ("heading", heading), ("rows", string.Join("\n\n", rows)));
        feedback.TryUpdateMessageDialog(dialogId, body, pages > 1 ? (_page + 1 == pages ? ProductPresentationText.Raw("crash.firstPage") : ProductPresentationText.Raw("crash.nextPage")) : null,
            pages > 1 ? () => { _page = (_page + 1) % pages; Show(); }
        : null, count > 0 && restore is not null ? ProductPresentationText.Raw("crash.rollback") : null, count > 0 ? restore : null, localizeMessage: false);
    }

}
