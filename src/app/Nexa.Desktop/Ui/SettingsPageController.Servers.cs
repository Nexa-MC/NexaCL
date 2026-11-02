using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private InstanceServerList? _serverList;
    private Task<XsrResult<InstanceServerList>>? _serverRead;
    private CancellationTokenSource? _serverStop;
    private string? _serverInstance, _serverError;
    private bool _serverLoaded;
    private int? _serverEditing;
    private XsrUiEntityId _serverName, _serverAddress, _serverWorkspaceRoot, _serverEditorRoot;
    private int? _serverEditorProjection;
    internal Action<string, string>? JoinManagementServer { get; set; }
    private Task<XsrResult<InstanceServerStatus>>? _serverStatusRead;
    private int _serverStatusIndex;
    private readonly Dictionary<int, InstanceServerStatus> _serverStatuses = [];
    private readonly Dictionary<string, XsrUiEntityId> _serverViews = [];
    private void CancelServers()
    {
        _serverStop?.Cancel(); _serverStop?.Dispose(); _serverStop = null;
        ObserveRetiredInstanceToolRead(_serverRead); ObserveRetiredInstanceToolRead(_serverStatusRead);
        _serverRead = null; _serverList = null; _serverInstance = null; _serverLoaded = false; _serverEditing = null; _serverError = null;
        _serverStatusRead = null; _serverStatuses.Clear(); _serverEditorProjection = null;
    }
    private void UpdateServers()
    {
        if (_selected != "servers") { if (_serverInstance is not null) CancelServers(); return; }
        if (_instance != _serverInstance) { CancelServers(); _serverInstance = _instance; }
        if (_instance is null || !_managementLoaded) return;
        if (_serverStatusRead is { IsCompleted: true } status)
        {
            _serverStatusRead = null;
            if (PendingQuery.Succeeded(status)) _serverStatuses[_serverStatusIndex] = status.Result.Value!;
            BuildServers();
        }
        if (!_serverLoaded && _serverRead is null && _queries.TryResolve(InstanceServerListContract.Read, out var route))
        {
            _serverStop = new(); _serverRead = _queries.QueryAsync<InstanceServerListQuery, InstanceServerList>(route, new(_instance), cancellationToken: _serverStop.Token).AsTask();
            WakeOnPlatformCompletion(_serverRead);
        }
        if (_serverRead is not { IsCompleted: true } read) return;
        _serverRead = null; _serverLoaded = true;
        if (PendingQuery.Succeeded(read)) _serverList = read.Result.Value;
        else _serverError = WorldTerm("server.read-error");
        BuildServers();
    }
    private void BuildServers()
    {
        if (!_shell.Tree.IsAlive(_sections)) return;
        var list = _serverList;
        var nodes = WorldView(_sections, ref _serverWorkspaceRoot, "Servers", new()
        {
            ["loading"] = Flag(list is null && _serverError is null),
            ["hasError"] = Flag(_serverError is not null),
            ["error"] = _serverError ?? "",
            ["loaded"] = Flag(list is not null),
            ["count"] = WorldNumber(list?.Entries.Count ?? 0)
        });
        BindWorldAction(nodes, WorldKey("ServerAdd"), () => { _serverEditing = -1; BuildServers(); });
        if (list is null) return;
        if (_serverEditing is { } editing)
        {
            if (editing >= list.Entries.Count) { _serverEditing = null; RemoveWorldView(ref _serverEditorRoot); }
            else
            {
                if (_serverEditorProjection != editing) { RemoveWorldView(ref _serverEditorRoot); }
                bool initialize = !_shell.Tree.IsAlive(_serverEditorRoot) || _shell.Tree.Parent(_serverEditorRoot) != nodes["Servers.Editor"];
                var entry = editing >= 0 ? list.Entries[editing] : new(-1, "", "");
                var editor = WorldView(nodes["Servers.Editor"], ref _serverEditorRoot, "ServerEditor", new()
                { ["adding"] = Flag(editing < 0), ["editing"] = Flag(editing >= 0), ["name"] = entry.Name, ["address"] = entry.Address });
                _serverEditorProjection = editing; _serverName = editor["ServerName"]; _serverAddress = editor["ServerAddress"];
                if (initialize) { _shell.Renderer.SetTextInputValue(_serverName, entry.Name); _shell.Renderer.SetTextInputValue(_serverAddress, entry.Address); }
                BindWorldAction(editor, WorldKey("Cancel"), () => { _serverEditing = null; BuildServers(); });
                BindWorldAction(editor, WorldKey("Save"), () =>
                {
                    string name = _shell.Tree.GetComponent<XsrUiTextInput>(_serverName)!.ReadDraft(), address = _shell.Tree.GetComponent<XsrUiTextInput>(_serverAddress)!.ReadDraft();
                    var updated = list.Entries.ToList(); var changed = entry with { Name = name, Address = address };
                    if (editing < 0) updated.Add(changed); else updated[editing] = changed;
                    SaveServers(updated);
                });
            }
        }
        else { RemoveWorldView(ref _serverEditorRoot); _serverEditorProjection = null; }
        var entries = list.Entries.Select((entry, index) => (Entry: entry, Index: index, Key: "ServerEntry." + WorldNumber(index))).ToArray();
        RetireWorldRows(_serverViews, entries.Select(entry => entry.Key));
        foreach (var (entry, index, key) in entries)
        {
            string check = index == 0 ? WorldKey("ServerCheck") : key + ".Check", join = index == 0 ? WorldKey("ServerJoin") : key + ".Join",
                edit = index == 0 ? WorldKey("ServerEdit") : key + ".Edit", up = index == 0 ? WorldKey("ServerUp") : key + ".Up",
                down = index == 0 ? WorldKey("ServerDown") : key + ".Down", remove = index == 0 ? WorldKey("ServerRemove") : key + ".Remove";
            var row = WorldRow(nodes["Servers.Rows"], _serverViews, key, "ServerRow", new()
            {
                ["key"] = key,
                ["nameKey"] = key + ".Name",
                ["addressKey"] = key + ".Address",
                ["name"] = StripContentFormatting(entry.Name),
                ["address"] = StripContentFormatting(entry.Address),
                ["status"] = _serverStatusRead is not null && _serverStatusIndex == index ? WorldTerm("server.connecting") : ServerStatusLabel(index),
                ["check"] = check,
                ["join"] = join,
                ["edit"] = edit,
                ["up"] = up,
                ["down"] = down,
                ["remove"] = remove,
                ["canCheck"] = Flag(_serverStatusRead is null),
                ["canJoin"] = Flag(JoinManagementServer is not null),
                ["canUp"] = Flag(index > 0),
                ["canDown"] = Flag(index + 1 < list.Entries.Count)
            });
            ApplyContentFormatting(row[key + ".Name"], entry.Name); ApplyContentFormatting(row[key + ".Address"], entry.Address);
            BindWorldAction(row, check, () =>
            {
                if (_serverStatusRead is not null || _serverInstance is null || !_queries.TryResolve(InstanceServerListContract.Status, out var route)) return;
                _serverStatusIndex = index;
                _serverStatusRead = _queries.QueryAsync<InstanceServerStatusQuery, InstanceServerStatus>(route, new(_serverInstance, list.Revision, entry.SourceIndex), cancellationToken: _serverStop?.Token ?? default).AsTask();
                WakeOnPlatformCompletion(_serverStatusRead); BuildServers();
            });
            BindWorldAction(row, join, () => JoinManagementServer?.Invoke(_serverInstance!, entry.Address));
            BindWorldAction(row, edit, () => { _serverEditing = index; BuildServers(); });
            BindWorldAction(row, up, () => { if (index <= 0) return; var items = list.Entries.ToList(); (items[index - 1], items[index]) = (items[index], items[index - 1]); SaveServers(items); });
            BindWorldAction(row, down, () => { if (index + 1 >= list.Entries.Count) return; var items = list.Entries.ToList(); (items[index + 1], items[index]) = (items[index], items[index + 1]); SaveServers(items); });
            BindWorldAction(row, remove, () => _feedback.ShowDialog("server.remove", WorldTerm("server.remove-title"), WorldTerm("server.remove-message"), WorldTerm("remove"), WorldTerm("cancel"),
                accepted => { if (accepted && _serverInstance == _instance && _selected == "servers" && ReferenceEquals(_serverList, list)) SaveServers(list.Entries.Where((_, i) => i != index).ToArray()); }, localizeMessage: false));
        }
        _dynamic.Reorder(nodes["Servers.Rows"], entries.Select(entry => _serverViews[entry.Key]).ToArray());
    }
    private string ServerStatusLabel(int index)
    {
        if (!_serverStatuses.TryGetValue(index, out var status)) return WorldTerm("server.unchecked");
        if (!status.Reachable) return status.Description;
        return WorldFormat("ServerStatusFormat", new()
        {
            ["milliseconds"] = WorldNumber(status.Milliseconds),
            ["online"] = status.OnlinePlayers is { } online ? WorldNumber(online) : "?",
            ["maximum"] = status.MaxPlayers is { } maximum ? WorldNumber(maximum) : "?",
            ["version"] = status.Version,
            ["compatibility"] = ProtocolCompatibility(status),
            ["description"] = status.Description
        });
    }
    private string ProtocolCompatibility(InstanceServerStatus status) => status.ServerProtocol is { } server && status.ClientProtocol is { } client
        ? server == client ? WorldTerm("server.protocol-same") : WorldFormat("ServerProtocolFormat", new() { ["client"] = WorldNumber(client), ["server"] = WorldNumber(server) })
        : WorldTerm("server.protocol-unknown");
    private void SaveServers(IReadOnlyList<InstanceServerEntry> entries)
    {
        if (_managementWrite is not null || _instance is null || _serverList is null || !_commands.TryResolve(InstanceServerListContract.Save, out var route)) return;
        _managementWriteInstance = _instance;
        _managementWrite = _commands.Dispatch(route, new InstanceServerListSaveCommand(_instance, _serverList.Revision, entries)).Completion;
        WakeOnPlatformCompletion(_managementWrite);
    }
}
