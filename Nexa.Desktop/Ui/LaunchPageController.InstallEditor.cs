using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{
    private bool _editingInstall;
    private MinecraftInstallEditPlan? _editPlan;
    private string? _editPlanKey;
    private MinecraftInstallEditSnapshot? _installEdit;
    private Task<XsrResult<MinecraftInstallEditSnapshot>>? _installEditRead;

    private void OpenInstallEditor(XsrUiEntityId source)
    {
        if (_installCatalogQueries is null || !_installCatalogQueries.TryResolve(MinecraftInstallEditContract.Query, out XsrQueryId route))
        { _feedback.Warn(InstallCopy("copy.bfe55e324dd7")); return; }
        string root = ReadCell(LaunchPageState.InstanceDirectoryKey);
        string instance = ReadCell(LaunchPageState.SelectedInstanceKey);
        if (string.IsNullOrWhiteSpace(instance)) return;
        ResetInstallSelection();
        _editingInstall = true;
        SetInstallEditorLabels(true);
        _shell.Renderer.SetTextInputValue(_javaInstallEntities["JavaInstallVersionInput"], instance);
        OpenSubpage(_javaInstallPage, source);
        _installEditRead = _installCatalogQueries.QueryAsync<MinecraftInstallEditQuery, MinecraftInstallEditSnapshot>(route,
            new(root, instance), cancellationToken: _lifetimeCancellation.Token).AsTask();
    }

    private void SetInstallEditorLabels(bool editing)
    {
        Publish(LaunchPageState.JavaInstallTitleKey, InstallCopy(editing ? "edit.title" : "install.title"));
        _editPlan = null;
        _editPlanKey = null;
        ReloadInstall(_installWorkspace, ("startEnabled", InstallFlag(!editing)),
            ("startText", InstallCopy(editing ? "edit.save" : "install.start")),
            ("startLabel", InstallCopy(editing ? "edit.save-label" : "install.start-label")));
    }

    private void ProjectInstallEditor()
    {
        if (!_editingInstall || _installEditRead is not { IsCompleted: true } read) return;
        _installEditRead = null;
        if (!PendingQuery.Succeeded(read))
        { _feedback.Warn(InstallCopy("copy.7a8cf0d72095")); return; }
        _installEdit = read.Result.Value!;
        _installGameChosen = true;
        _selectedInstallVersion = _installEdit.GameVersion;
        foreach (var selection in _installEdit.Selection) _selectedInstallBuilds[selection.Loader] = selection.Version;
        _selectedInstallLoader = _installEdit.Selection.Count > 0 ? _installEdit.Selection[0].Loader.ToString() : LaunchInstallText.Raw("loader.vanilla");
        PrefetchInstallCatalog(_installEdit.GameVersion);
        UpdateJavaInstallSubpageVisibility("JavaMinecraftPage");
        _catalogRevision = -1;
        _catalogFirst = -1;
        UpdateTitleBar();
    }
    private void ProjectInstallEditPlan()
    {
        if (_installEdit is null || _installCatalogQueries is null
            || !_installCatalogQueries.TryResolve(MinecraftInstallEditPlanContract.Query, out XsrQueryId route)) return;
        string name = _shell.Tree.GetComponent<XsrUiTextInput>(_javaInstallEntities["JavaInstallVersionInput"])!.ReadDraft().Trim();
        string key = name + "\n" + string.Join(";", _selectedInstallBuilds.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value));
        if (key == _editPlanKey) return;
        var pending = _installCatalogQueries.QueryAsync<MinecraftInstallEditPlanQuery, MinecraftInstallEditPlan>(route,
            new(_installEdit, _selectedInstallBuilds.Select(pair => new InstallBuildSelection(pair.Key, pair.Value)).ToArray()));
        if (!pending.IsCompletedSuccessfully) return;
        var completed = pending.Result;
        if (!completed.IsSuccess) return;
        _editPlan = completed.Value!;
        _editPlanKey = key;
        bool renamed = name != _installEdit.InstanceId;
        string label = renamed && _editPlan.Kind == MinecraftInstallEditKind.Unchanged ? InstallCopy("edit.rename") : InstallCopy(_editPlan.Kind switch
        {
            MinecraftInstallEditKind.Unchanged => "edit.unchanged",
            MinecraftInstallEditKind.Reinstall => "edit.reinstall",
            _ => _editPlan.ChangedLoaders.All(loader => !_installEdit.Selection.Any(original => original.Loader == loader)) ? "edit.add" : "edit.components",
        });
        ReloadInstall(_installWorkspace, ("startText", label), ("startLabel", label),
            ("startEnabled", InstallFlag(name.Length > 0 && (renamed || _editPlan.Kind != MinecraftInstallEditKind.Unchanged))));
    }

}
