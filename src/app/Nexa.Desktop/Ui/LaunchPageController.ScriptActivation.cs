using Nexa.Services.Minecraft;
using Nexa.Xsr;
namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{
    private Task<XsrResult>? _scriptSelection;
    private (string Root, string Instance)? _scriptTarget;
    internal void RunInstance(string root, string instance)
    {
        if (_disposed || LaunchBusy || _scriptSelection is not null) return;
        _scriptTarget = (root, instance); _scriptSelection = SelectScriptInstanceAsync(root, instance);
        _ = _scriptSelection.ContinueWith(_ => Interlocked.Exchange(ref _homeRefreshRequested, 1), TaskScheduler.Default);
    }
    private async Task<XsrResult> SelectScriptInstanceAsync(string root, string instance)
    {
        if (!_libraryCommands.TryResolve(MinecraftLibraryRoutes.Directory, out var directory) || !_libraryCommands.TryResolve(MinecraftLibraryRoutes.Select, out var select))
            return XsrResult.Failure(Nexa.Services.Minecraft.MinecraftErrors.InvalidRequest(ProductPresentationText.Raw("feature.script.unavailable")));
        var opened = await _libraryCommands.Dispatch(directory, new MinecraftLibraryDirectoryCommand(root, true), cancellationToken: _lifetimeCancellation.Token).Completion.ConfigureAwait(false);
        if (!opened.IsSuccess) return opened;
        return await _libraryCommands.Dispatch(select, new MinecraftLibrarySelectCommand(root, instance), cancellationToken: _lifetimeCancellation.Token).Completion.ConfigureAwait(false);
    }
    private void PumpScriptInstance()
    {
        if (_scriptSelection is not { IsCompleted: true } read || _scriptTarget is not { } target) return;
        if (!read.IsCompletedSuccessfully || !read.Result.IsSuccess)
        {
            _scriptSelection = null; _scriptTarget = null;
            _feedback.Error(read.IsCompletedSuccessfully ? read.Result.Error?.Message ?? ProductPresentationText.Raw("feature.script.failed") : ProductPresentationText.Raw("feature.script.failed")); return;
        }
        if (_store.ReadAppliedValue(_libraryId) is not MinecraftLibrarySnapshot snapshot || snapshot.IsProvisional) return;
        _scriptSelection = null; _scriptTarget = null;
        if (!Nexa.Core.PathIdentity.Comparer.Equals(snapshot.RootDirectory, target.Root) || snapshot.SelectedInstanceId != target.Instance)
        { _feedback.Warn(ProductPresentationText.Raw("feature.script.changed")); return; }
        ProjectLibrary(); _shell.Stage.Navigation.Replace(_launchPage);
        _launchRequest = StartLaunchAsync(target.Instance, _launchPage, rootDirectory: target.Root);
    }
}
