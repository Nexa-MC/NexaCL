using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private Task<XsrResult<InstanceModRemovalPreview>>? _modRemovalRead;
    private InstanceContentRemoveCommand? _modRemovalPrimary;
    private CancellationTokenSource? _modRemovalStop;
    private long _modRemovalGeneration;
    private Guid _modRemovalDialog;

    private void CancelModRemovalPreview()
    {
        Interlocked.Increment(ref _modRemovalGeneration);
        ObserveRetiredInstanceToolRead(_modRemovalRead);
        _modRemovalStop?.Cancel(); _modRemovalStop?.Dispose(); _modRemovalStop = null;
        _modRemovalRead = null; _modRemovalPrimary = null;
        if (_modRemovalDialog != Guid.Empty) _feedback.DismissDialog(_modRemovalDialog);
        _modRemovalDialog = default;
    }
    private void StartModRemovalPreview(InstanceContentRemoveCommand command, XsrQueryId route)
    {
        CancelModRemovalPreview(); _modRemovalPrimary = command; _modRemovalStop = new();
        _modRemovalRead = _queries.QueryAsync<InstanceModRemovalQuery, InstanceModRemovalPreview>(route, new(command), cancellationToken: _modRemovalStop.Token).AsTask();
        WakeOnPlatformCompletion(_modRemovalRead);
    }
    private void UpdateModRemovalPreview()
    {
        if (_modRemovalPrimary is not { } primary) return;
        if (_instance != primary.InstanceDirectory || _selected != "mods") { CancelModRemovalPreview(); return; }
        if (_modRemovalRead is not { IsCompleted: true } read) return;
        _modRemovalRead = null; _modRemovalPrimary = null; _modRemovalStop?.Dispose(); _modRemovalStop = null;
        if (!PendingQuery.Succeeded(read) || !_commands.TryResolve(InstanceManagementContract.RemoveMod, out var route))
        { _feedback.Error(ContentTerm("copy.764322c85636")); return; }
        var preview = read.Result.Value!;
        string message = ContentFormat("RemoveContent", ("name", RecoveryExplanation.Display(primary.Name)));
        if (preview.RequiredBy.Count > 0) message += ContentTerm("copy.d4be1b8287e4") + string.Join(ContentTerm("nameSeparator"), preview.RequiredBy.Take(12).Select(RecoveryExplanation.Display))
            + (preview.RequiredBy.Count > 12 ? ContentFormat("MoreRequired", ("count", (preview.RequiredBy.Count - 12).ToString(System.Globalization.CultureInfo.CurrentCulture))) : ContentTerm("sentenceEnd"));
        if (preview.Orphans.Count > 0) message += ContentTerm("copy.241cbb8edeba") + string.Join("\n", preview.Orphans.Take(12).Select(item => RecoveryExplanation.Display(item.Name)))
            + (preview.Orphans.Count > 12 ? ContentFormat("MoreOrphans", ("count", (preview.Orphans.Count - 12).ToString(System.Globalization.CultureInfo.CurrentCulture))) : "");
        if (preview.Notice is not null) message += "\n" + _shell.Renderer.LocalizeText(preview.Notice);
        long generation = _modRemovalGeneration; long scope = _contentScopeGeneration;
        void Remove(bool withOrphans)
        {
            if (_managementWrite is not null || _instance != primary.InstanceDirectory || _selected != "mods" || !_visible || _modRemovalGeneration != generation || _contentScopeGeneration != scope) return;
            _managementWriteInstance = primary.InstanceDirectory;
            _managementWrite = _commands.Dispatch(route, new InstanceModRemovalCommand(primary, withOrphans ? preview.Orphans : [])).Completion;
            WakeOnPlatformCompletion(_managementWrite);
        }
        _modRemovalDialog = _feedback.ShowDialog("content.remove-mod", ContentTerm("copy.3e8aba8feedb"), message, preview.Orphans.Count > 0 ? ContentTerm("copy.77cf786d08dc") : ContentTerm("copy.6135d4159e89"), ContentTerm("copy.2cd0f3be8738"),
            accepted => { if (_modRemovalGeneration == generation) _modRemovalDialog = default; if (accepted) Remove(false); }, preview.Orphans.Count > 0 ? ContentTerm("copy.d3d4d975dda3") : null, () => { if (_modRemovalGeneration == generation) _modRemovalDialog = default; Remove(true); }, localizeMessage: false);
    }
}
