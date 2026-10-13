using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private InstanceModpackExportPreview? _exportPreview;
    private Task<XsrResult<InstanceModpackExportPreview>>? _exportRead;
    private CancellationTokenSource? _exportStop;
    private string? _exportInstance, _exportError;
    private bool _exportLoaded;
    private readonly HashSet<string> _exportCategories = new(StringComparer.Ordinal) { "mods", "config", "defaultconfigs" };
    private XsrUiEntityId _exportName, _exportVersion, _exportPath;
    private string _exportNameDraft = "", _exportVersionDraft = "1.0.0", _exportPathDraft = "";
    private Func<Task<string?>>? _pickExportDirectory;
    private Task<string?>? _exportDirectoryRead;
    internal void ConfigureExport(Func<Task<string?>> picker) => _pickExportDirectory = picker;
    private void CancelExport()
    {
        _exportStop?.Cancel(); _exportStop?.Dispose(); _exportStop = null; ObserveRetiredInstanceToolRead(_exportRead); ObserveRetiredInstanceToolRead(_exportDirectoryRead); _exportRead = null; _exportPreview = null;
        _exportInstance = null; _exportError = null; _exportLoaded = false; _exportDirectoryRead = null;
        _exportName = _exportVersion = _exportPath = default;
        _exportNameDraft = ""; _exportVersionDraft = "1.0.0"; _exportPathDraft = "";
        _exportCategories.Clear(); _exportCategories.UnionWith(["mods", "config", "defaultconfigs"]);
    }
    private void UpdateExport()
    {
        if (_selected != "modpack") { if (_exportInstance is not null) CancelExport(); return; }
        if (_exportInstance != _instance) { CancelExport(); _exportInstance = _instance; }
        if (_selected != "modpack" || _instance is null || !_managementLoaded) return;
        CaptureExportDrafts();
        if (_exportDirectoryRead is { IsCompleted: true } picking)
        {
            _exportDirectoryRead = null;
            if (picking.IsCompletedSuccessfully && picking.Result is { } directory)
            {
                _exportPathDraft = Path.Combine(directory, Path.GetFileName(_instance) + ".mrpack");
                if (_shell.Tree.IsAlive(_exportPath)) _shell.Renderer.SetTextInputValue(_exportPath, _exportPathDraft);
            }
            else if (picking.IsFaulted) _feedback.Error(WorldTerm("export.pick-error"));
        }
        if (!_exportLoaded && _exportRead is null && _queries.TryResolve(InstanceModpackExportContract.Preview, out var route))
        {
            _exportStop = new(); _exportRead = _queries.QueryAsync<InstanceModpackExportQuery, InstanceModpackExportPreview>(route, new(_instance), cancellationToken: _exportStop.Token).AsTask();
            WakeOnPlatformCompletion(_exportRead);
        }
        if (_exportRead is not { IsCompleted: true } read) return;
        _exportRead = null; _exportLoaded = true;
        if (PendingQuery.Succeeded(read)) _exportPreview = read.Result.Value;
        else _exportError = WorldTerm("export.read-error");
        if (_management is { } snapshot) BuildModpackExport(snapshot);
    }
    private void CaptureExportDrafts()
    {
        string Draft(XsrUiEntityId field, string fallback) => field.IsAssigned && _shell.Tree.IsAlive(field)
            ? _shell.Tree.GetComponent<XsrUiTextInput>(field)!.ReadDraft() : fallback;
        _exportNameDraft = Draft(_exportName, _exportNameDraft); _exportVersionDraft = Draft(_exportVersion, _exportVersionDraft); _exportPathDraft = Draft(_exportPath, _exportPathDraft);
    }
    private XsrUiEntityId _exportRoot;
    private readonly Dictionary<string, XsrUiEntityId> _exportCategoryViews = [];
    private void BuildModpackExport(InstanceManagementSnapshot snapshot)
    {
        if (!_shell.Tree.IsAlive(_sections)) return;
        if (_exportNameDraft.Length == 0) _exportNameDraft = Path.GetFileName(snapshot.InstanceDirectory);
        bool initialize = !_shell.Tree.IsAlive(_exportRoot) || _shell.Tree.Parent(_exportRoot) != _sections;
        var preview = _exportPreview;
        var nodes = WorldView(_sections, ref _exportRoot, "Export", new()
        {
            ["gameVersion"] = snapshot.GameVersion,
            ["recordedVersion"] = snapshot.ModpackVersion.Length > 0 ? snapshot.ModpackVersion : WorldTerm("unrecorded"),
            ["name"] = _exportNameDraft,
            ["version"] = _exportVersionDraft,
            ["path"] = _exportPathDraft,
            ["picker"] = Flag(_pickExportDirectory is not null),
            ["preview"] = Flag(preview is not null),
            ["enabled"] = Flag(_managementWrite is null),
            ["loading"] = Flag(preview is null && _exportError is null),
            ["hasError"] = Flag(_exportError is not null),
            ["error"] = _exportError ?? ""
        });
        _exportName = nodes["ExportName"]; _exportVersion = nodes["ExportVersion"]; _exportPath = nodes["ExportPath"];
        if (initialize)
        {
            _shell.Renderer.SetTextInputValue(_exportName, _exportNameDraft); _shell.Renderer.SetTextInputValue(_exportVersion, _exportVersionDraft); _shell.Renderer.SetTextInputValue(_exportPath, _exportPathDraft);
        }
        BindWorldAction(nodes, WorldKey("ExportChoose"), () =>
        {
            if (_exportDirectoryRead is not null || _pickExportDirectory is null) return;
            _exportDirectoryRead = _pickExportDirectory(); WakeOnPlatformCompletion(_exportDirectoryRead);
        });
        var categories = preview?.Categories.ToArray() ?? [];
        RetireWorldRows(_exportCategoryViews, categories.Select(category => category.Id));
        foreach (var category in categories)
        {
            string key = "ExportCategory." + InstanceToolIdentity(category.Id), choice = "ExportCategoryChoice." + category.Id;
            var row = WorldRow(nodes["Export.Categories"], _exportCategoryViews, category.Id, "ExportCategory", new()
            {
                ["key"] = key,
                ["choice"] = choice,
                ["label"] = category.Label,
                ["selected"] = Flag(_exportCategories.Contains(category.Id)),
                ["files"] = WorldNumber(category.FileCount),
                ["size"] = (category.Bytes / 1048576d).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)
            });
            BindWorldAction(row, choice, () =>
            {
                CaptureExportDrafts();
                if (!_exportCategories.Remove(category.Id)) _exportCategories.Add(category.Id);
                BuildModpackExport(snapshot);
            });
        }
        _dynamic.Reorder(nodes["Export.Categories"], categories.Select(category => _exportCategoryViews[category.Id]).ToArray());
        BindWorldAction(nodes, WorldKey("Export"), () =>
        {
            if (preview is null || _managementWrite is not null || _instance is null || !_commands.TryResolve(InstanceModpackExportContract.Export, out var route)) return;
            _managementWriteInstance = _instance;
            _managementWrite = _commands.Dispatch(route, new InstanceModpackExportCommand(_instance,
                _shell.Tree.GetComponent<XsrUiTextInput>(_exportPath)!.ReadDraft(), _shell.Tree.GetComponent<XsrUiTextInput>(_exportName)!.ReadDraft(),
                _shell.Tree.GetComponent<XsrUiTextInput>(_exportVersion)!.ReadDraft(), preview.Categories.Where(c => _exportCategories.Contains(c.Id)).Select(c => c.Id).ToArray())).Completion;
            WakeOnPlatformCompletion(_managementWrite); BuildModpackExport(snapshot);
        });
    }
}
