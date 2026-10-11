namespace Nexa.Desktop.Ui;

internal sealed partial class ResourcesPageController
{
    internal async Task PrepareVisibleStartupAsync(CancellationToken token)
    {
        if (_shell.Stage.Navigation.Current != Page && _shell.Stage.Navigation.Current != DetailPage) return;
        // Reuse the tasks started by FramePreparing; never dispatch a second provider read.
        for (int pass = 0; pass < 3; pass++)
        {
            OnFrame(this, EventArgs.Empty);
            Task[] pending = new Task?[] { _searching, _reading, _favoritesReading, _contextReading, _instanceReading, _sourcePolicyReading }
                .Concat(_retainedPages.Values.Select(static page => (Task?)page.Renewal).OfType<Task>())
                .Concat(_icons.Select(static item => (Task)item.Read))
                .Concat(_translations.Select(static item => (Task)item.Read))
                .OfType<Task>().Where(task => !task.IsCompleted).ToArray();
            if (pending.Length == 0) return;
            await Task.WhenAll(pending).WaitAsync(token).ConfigureAwait(true);
        }
        OnFrame(this, EventArgs.Empty);
    }

    internal void SetStartupOffline()
    {
        if (_shell.Stage.Navigation.Current != Page && _shell.Stage.Navigation.Current != DetailPage) return;
        PauseContinuousList(); _listMediaPaused = true; _listFailure = DesktopResourcesText.Raw("copy.2ab252189eb7");
        Cancel(); CancelIcons(); CancelTranslations();
        // Keep the resolved placeholder state until navigation or an explicit retry. A final
        // preparation frame must not re-admit the icon requests that just exhausted its budget.
        _iconPage = _shell.Stage.Navigation.Current;
        _contextStop.Cancel();
        _contextReading = null;
        if (_result is not null)
        {
            _result = _result with
            {
                Notice = string.IsNullOrWhiteSpace(_result.Notice)
                ? DesktopResourcesText.Raw("copy.2ab252189eb7") : _result.Notice + DesktopResourcesText.Raw("copy.c5ba3bfa576f")
            };
            UpdatePagination();
            return;
        }
        ShowFailure(_entities["ResourceList"], DesktopResourcesText.Raw("copy.801a5ce37776"),
            RequestNextPage, _listActions);
        UpdatePagination();
    }
}
