using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private InstanceModpackExportPreview? _exportPreview;
    private Task<XsrResult<InstanceModpackExportPreview>>? _exportRead;
    private Task<XsrResult>? _exportWrite;
    private CancellationTokenSource? _exportStop;
    private string? _exportInstance, _exportError;
    private bool _exportLoaded;
    private readonly HashSet<string> _exportCategories = new(StringComparer.Ordinal) { "mods", "config", "defaultconfigs" };
    private XsrUiEntityId _exportName, _exportVersion, _exportPath;
    private string _exportNameDraft = "", _exportVersionDraft = "1.0.0", _exportPathDraft = "";
    private InstanceExportFormat _exportFormat;
    private bool _exportCore, _exportLibraries, _exportAssets;
    private Func<Task<string?>>? _pickExportDirectory;
    private Task<string?>? _exportDirectoryRead;
    internal void ConfigureExport(Func<Task<string?>> picker) => _pickExportDirectory = picker;
    private void CancelExport()
    {
        _exportStop?.Cancel(); _exportStop?.Dispose(); _exportStop = null; ObserveRetiredInstanceToolRead(_exportRead); ObserveRetiredInstanceToolRead(_exportDirectoryRead); _exportRead = null; _exportPreview = null;
        _exportInstance = null; _exportError = null; _exportLoaded = false; _exportDirectoryRead = null;
        _exportName = _exportVersion = _exportPath = default;
        _exportNameDraft = ""; _exportVersionDraft = "1.0.0"; _exportPathDraft = "";
        _exportFormat = InstanceExportFormat.Mrpack; _exportCore = _exportLibraries = _exportAssets = false;
        _exportCategories.Clear(); _exportCategories.UnionWith(["mods", "config", "defaultconfigs"]);
        ResetExportFiles();
        ObserveRetiredInstanceToolRead(_exportWrite); _exportWrite = null;
    }
    private void UpdateExport()
    {
        UpdateModpackUpdate();
        if (_selected != "modpack") { if (_exportInstance is not null) CancelExport(); return; }
        if (_exportInstance != _instance) { CancelExport(); _exportInstance = _instance; }
        if (_selected != "modpack" || _instance is null || !_managementLoaded) return;
        CaptureExportDrafts();
        if (_exportWrite is { IsCompleted: true } writing)
        {
            _exportWrite = null;
            if (!PendingQuery.Succeeded(writing))
                _feedback.Error(writing.IsCompletedSuccessfully ? writing.Result.Error?.Message ?? WorldTerm("export.write-error") : WorldTerm("export.write-error"));
            else _feedback.Success(WorldTerm("export.saved"));
            if (_management is { } current) BuildModpackExport(current);
        }
        UpdateExportFiles();
        if (_exportDirectoryRead is { IsCompleted: true } picking)
        {
            _exportDirectoryRead = null;
            if (picking.IsCompletedSuccessfully && picking.Result is { } directory)
            {
                _exportPathDraft = Path.Combine(directory, Path.GetFileName(_instance) + (_exportFormat == InstanceExportFormat.Zip ? ".zip" : ".mrpack"));
                if (_exportOptionsPathDraft.Length == 0)
                {
                    _exportOptionsPathDraft = Path.Combine(directory, Path.GetFileName(_instance) + ".export.json");
                    if (_shell.Tree.IsAlive(_exportOptionsPath)) _shell.Renderer.SetTextInputValue(_exportOptionsPath, _exportOptionsPathDraft);
                }
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
        _exportSummaryDraft = Draft(_exportSummary, _exportSummaryDraft);
        _exportOptionsPathDraft = Draft(_exportOptionsPath, _exportOptionsPathDraft);
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
            ["zip"] = Flag(_exportFormat == InstanceExportFormat.Zip),
            ["mrpack"] = Flag(_exportFormat == InstanceExportFormat.Mrpack),
            ["core"] = Flag(_exportCore),
            ["libraries"] = Flag(_exportLibraries),
            ["assets"] = Flag(_exportAssets),
            ["picker"] = Flag(_pickExportDirectory is not null),
            ["preview"] = Flag(preview is not null),
            ["enabled"] = Flag(_managementWrite is null && _exportWrite is null),
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
                ["label"] = _shell.Renderer.LocalizeText(category.Label),
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
        BuildExportFiles(nodes, preview, initialize);
        void ChangeFormat(InstanceExportFormat format)
        {
            CaptureExportDrafts(); _exportFormat = format;
            if (_exportPathDraft.Length > 0)
            {
                _exportPathDraft = Path.ChangeExtension(_exportPathDraft, format == InstanceExportFormat.Zip ? ".zip" : ".mrpack");
                _shell.Renderer.SetTextInputValue(_exportPath, _exportPathDraft);
            }
            BuildModpackExport(snapshot);
        }
        BindWorldAction(nodes, "Export.Zip", () => ChangeFormat(InstanceExportFormat.Zip));
        BindWorldAction(nodes, "Export.Mrpack", () => ChangeFormat(InstanceExportFormat.Mrpack));
        BindWorldAction(nodes, "Export.GameCore", () => { CaptureExportDrafts(); _exportCore = !_exportCore; BuildModpackExport(snapshot); });
        BindWorldAction(nodes, "Export.Libraries", () => { CaptureExportDrafts(); _exportLibraries = !_exportLibraries; BuildModpackExport(snapshot); });
        BindWorldAction(nodes, "Export.Assets", () => { CaptureExportDrafts(); _exportAssets = !_exportAssets; BuildModpackExport(snapshot); });
        BindWorldAction(nodes, WorldKey("Export"), () =>
        {
            if (preview is null || _managementWrite is not null || _exportWrite is not null || _instance is null || !_commands.TryResolve(InstanceModpackExportContract.Export, out var route)) return;
            _exportWrite = _commands.Dispatch(route, new InstanceModpackExportCommand(_instance,
                _shell.Tree.GetComponent<XsrUiTextInput>(_exportPath)!.ReadDraft(), _shell.Tree.GetComponent<XsrUiTextInput>(_exportName)!.ReadDraft(),
                _shell.Tree.GetComponent<XsrUiTextInput>(_exportVersion)!.ReadDraft(), preview.Categories.Where(c => _exportCategories.Contains(c.Id)).Select(c => c.Id).ToArray())
            {
                Summary = _shell.Tree.GetComponent<XsrUiTextInput>(_exportSummary)!.ReadDraft(),
                ExcludedPaths = _exportExcluded.ToArray(),
                Format = _exportFormat,
                IncludeGameCore = _exportFormat == InstanceExportFormat.Zip && _exportCore,
                IncludeLibraries = _exportFormat == InstanceExportFormat.Zip && _exportLibraries,
                IncludeAssets = _exportFormat == InstanceExportFormat.Zip && _exportAssets
            }).Completion;
            WakeOnPlatformCompletion(_exportWrite); BuildModpackExport(snapshot);
        });
    }
}
