using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private readonly HashSet<string> _exportExcluded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XsrUiEntityId> _exportFileViews = [];
    private XsrUiEntityId _exportSearch, _exportSummary, _exportOptionsPath, _exportCountRoot;
    private string _exportSearchDraft = "", _exportSummaryDraft = "", _exportOptionsPathDraft = "";
    private int _exportFilePage;
    private Task<XsrResult<InstanceExportOptions>>? _exportOptionsRead;

    private void ResetExportFiles()
    {
        _exportExcluded.Clear(); RetireWorldRows(_exportFileViews, []);
        RemoveWorldView(ref _exportCountRoot);
        _exportSearch = _exportSummary = _exportOptionsPath = default;
        _exportSearchDraft = _exportSummaryDraft = _exportOptionsPathDraft = ""; _exportFilePage = 0;
        ObserveRetiredInstanceToolRead(_exportOptionsRead); _exportOptionsRead = null;
    }
    private void UpdateExportFiles()
    {
        bool rebuild = false;
        if (_shell.Tree.IsAlive(_exportSearch))
        {
            string search = _shell.Tree.GetComponent<XsrUiTextInput>(_exportSearch)!.ReadDraft();
            if (search != _exportSearchDraft) { _exportSearchDraft = search; _exportFilePage = 0; rebuild = true; }
        }
        if (_exportOptionsRead is { IsCompleted: true } read)
        {
            _exportOptionsRead = null;
            if (read.IsCompletedSuccessfully && read.Result.IsSuccess)
            {
                var options = read.Result.Value!;
                _exportNameDraft = options.Name; _exportVersionDraft = options.Version; _exportSummaryDraft = options.Summary;
                _exportCategories.Clear(); _exportCategories.UnionWith(options.Categories);
                _exportExcluded.Clear(); _exportExcluded.UnionWith(options.ExcludedPaths.Where(path => !path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)));
                _shell.Renderer.SetTextInputValue(_exportName, options.Name);
                _shell.Renderer.SetTextInputValue(_exportVersion, options.Version);
                _shell.Renderer.SetTextInputValue(_exportSummary, options.Summary);
                _exportFilePage = 0; rebuild = true;
            }
            else _feedback.Error(WorldTerm("export.read-error"));
        }
        if (rebuild && _management is { } snapshot) BuildModpackExport(snapshot);
    }
    private void BuildExportFiles(Dictionary<string, XsrUiEntityId> nodes, InstanceModpackExportPreview? preview, bool initialize)
    {
        _exportSearch = nodes["ExportSearch"]; _exportSummary = nodes["ExportSummary"]; _exportOptionsPath = nodes["ExportOptionsPath"];
        if (initialize)
        {
            _shell.Renderer.SetTextInputValue(_exportSearch, _exportSearchDraft);
            _shell.Renderer.SetTextInputValue(_exportSummary, _exportSummaryDraft);
            _shell.Renderer.SetTextInputValue(_exportOptionsPath, _exportOptionsPathDraft);
        }
        void Rebuild() { CaptureExportDrafts(); if (_management is { } snapshot) BuildModpackExport(snapshot); }
        BindWorldAction(nodes, "Export.All", () => { _exportCategories.UnionWith(preview?.Categories.Select(item => item.Id) ?? []); _exportExcluded.Clear(); Rebuild(); });
        BindWorldAction(nodes, "Export.None", () => { _exportCategories.Clear(); Rebuild(); });
        BindWorldAction(nodes, "Export.Reset", () => { _exportCategories.Clear(); _exportCategories.UnionWith(["mods", "config", "defaultconfigs", "options.txt", "resourcepacks", "shaderpacks", "kubejs", "scripts", "datapacks"]); _exportExcluded.Clear(); Rebuild(); });
        var files = preview?.Files.Where(file => _exportCategories.Contains(file.Category)).ToArray() ?? [];
        var filtered = files.Where(file => file.Category != "mods").GroupBy(file =>
        {
            string[] parts = file.Path.Split('/');
            return file.Category is "saves" or "resourcepacks" or "shaderpacks" or "texturepacks" && parts.Length > 2
                ? parts[0] + "/" + parts[1] : file.Path;
        }).Select(group => new { Path = group.Key, Files = group.ToArray(), Bytes = group.Sum(file => file.Bytes) })
            .Where(group => group.Path.Contains(_exportSearchDraft, StringComparison.OrdinalIgnoreCase)).ToArray();
        int pages = Math.Max(1, (filtered.Length + 31) / 32); _exportFilePage = Math.Clamp(_exportFilePage, 0, pages - 1);
        var visible = filtered.Skip(_exportFilePage * 32).Take(32).ToArray();
        RetireWorldRows(_exportFileViews, visible.Select(file => file.Path));
        foreach (var file in visible)
        {
            string key = "Export.File." + InstanceToolIdentity(file.Path);
            var row = WorldRow(nodes["Export.Files"], _exportFileViews, file.Path, "ExportFile", new()
            { ["key"] = key, ["choice"] = key + ".choice", ["path"] = file.Path, ["selected"] = Flag(file.Files.All(entry => !_exportExcluded.Contains(entry.Path))), ["size"] = (file.Bytes / 1048576d).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) });
            BindWorldAction(row, key + ".choice", () =>
            {
                bool included = file.Files.All(entry => !_exportExcluded.Contains(entry.Path));
                foreach (var entry in file.Files) { if (included) _exportExcluded.Add(entry.Path); else _exportExcluded.Remove(entry.Path); }
                Rebuild();
            });
        }
        _dynamic.Reorder(nodes["Export.Files"], visible.Select(file => _exportFileViews[file.Path]).ToArray());
        WorldView(nodes["Export.Count"], ref _exportCountRoot, "ExportCount", new()
        { ["value"] = $"{_exportFilePage + 1} / {pages} · {files.Count(file => !_exportExcluded.Contains(file.Path))} / {files.Length}" });
        BindWorldAction(nodes, "Export.Previous", () => { _exportFilePage = Math.Max(0, _exportFilePage - 1); Rebuild(); });
        BindWorldAction(nodes, "Export.Next", () => { _exportFilePage = Math.Min(pages - 1, _exportFilePage + 1); Rebuild(); });
        BindWorldAction(nodes, "Export.LoadOptions", () =>
        {
            CaptureExportDrafts();
            if (_exportOptionsRead is not null || !_queries.TryResolve(InstanceModpackExportContract.LoadOptions, out var route)) return;
            _exportOptionsRead = _queries.QueryAsync<InstanceExportOptionsQuery, InstanceExportOptions>(route, new(_exportOptionsPathDraft), cancellationToken: _exportStop?.Token ?? default).AsTask();
            WakeOnPlatformCompletion(_exportOptionsRead);
        });
        BindWorldAction(nodes, "Export.SaveOptions", () =>
        {
            CaptureExportDrafts();
            if (_managementWrite is not null || !_commands.TryResolve(InstanceModpackExportContract.SaveOptions, out var route)) return;
            _managementWriteInstance = _instance;
            _managementWrite = _commands.Dispatch(route, new InstanceExportOptionsSave(_exportOptionsPathDraft,
                new(_exportNameDraft, _exportVersionDraft, _exportSummaryDraft, _exportCategories.ToArray(), _exportExcluded.ToArray()))).Completion;
            WakeOnPlatformCompletion(_managementWrite);
        });
    }
}
