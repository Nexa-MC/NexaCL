using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private int _recoverySnapshotPage, _recoveryChangesPage;
    private long _recoveryGeneration;
    private XsrUiEntityId _recoveryRoot;
    private string? _recoveryInstance, _recoveryLanguage;
    private bool _recoveryPolicyBuilt, _recoveryBusy, _recoveryDevelopment;
    private InstanceManagementSnapshot? _recoveryProjection;
    private InstanceRecoveryReport? _recoveryComparison;
    private readonly Dictionary<XsrUiEntityId, (string Template, Dictionary<string, string> Data)> _recoveryViews = [];
    private readonly Dictionary<string, XsrUiEntityId> _recoverySnapshots = [], _recoveryCategories = [], _recoveryChanges = [];
    private static readonly Lazy<Dictionary<string, string>> RecoveryTerms = new(() =>
    {
        var nodes = DesktopMarkup.Load("SettingsRecoveryTerms.nxml").Root.Children;
        if (nodes.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
            || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("Recovery terms must be keyed literal text.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });

    private string RecoveryTerm(string key) => _shell.Renderer.LocalizeText(RecoveryTerms.Value[key]);
    private string RecoveryFormat(string name, params (string Key, string Value)[] values)
    {
        var text = DesktopMarkup.Dynamic("SettingsRecoveryText" + name + ".d.nxml",
            values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal), _shell.Renderer.LocalizeText).Root;
        if (text.Recipe != NxmlRuntimeRecipe.Text || text.Content is null || text.Bindings.Count != 0 || text.Children.Count != 0)
            throw new NxmlCompileException("Recovery formats must produce literal text only.");
        return text.Content;
    }
    private static string RecoveryNumber(long value) => value.ToString(CultureInfo.CurrentCulture);
    private static string RecoveryIdentity(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string RecoveryKind(InstanceRecoveryChangeKind kind) => RecoveryTerm("kind." + (Enum.IsDefined(kind) ? kind : InstanceRecoveryChangeKind.Modified));
    private string RecoveryCategory(string category) => RecoveryTerms.Value.TryGetValue("category." + category, out var label)
        ? _shell.Renderer.LocalizeText(label) : RecoveryExplanation.Display(category);
    private string RecoverySize(long bytes)
    {
        string unit = bytes >= 1024L * 1024 * 1024 ? "GiB" : bytes >= 1024 * 1024 ? "MiB" : "KiB";
        double divisor = unit == "GiB" ? 1024d * 1024 * 1024 : unit == "MiB" ? 1024d * 1024 : 1024d;
        return RecoveryFormat("Size", ("value", (bytes / divisor).ToString("0.##", CultureInfo.CurrentCulture)), ("unit", unit));
    }

    private XsrUiEntityId RecoveryView(XsrUiEntityId slot, XsrUiEntityId root, string template, Dictionary<string, string> data)
    {
        var fragment = DesktopMarkup.Dynamic(template, data);
        if (!_shell.Tree.IsAlive(root) || _shell.Tree.Parent(root) != slot)
            root = _dynamic.Append(slot, fragment);
        else if (!_recoveryViews.TryGetValue(root, out var previous) || previous.Data.Count != data.Count
            || data.Any(value => previous.Data.GetValueOrDefault(value.Key) != value.Value))
            _dynamic.Reload(root, fragment);
        _recoveryViews[root] = (template, data);
        return root;
    }

    private void RetireRecoveryRows(Dictionary<string, XsrUiEntityId> rows, IEnumerable<string> wanted)
    {
        var retained = wanted.ToHashSet(StringComparer.Ordinal);
        foreach (var key in rows.Keys.Where(key => !retained.Contains(key) || !_shell.Tree.IsAlive(rows[key])).ToArray())
        {
            var root = rows[key];
            if (_shell.Tree.IsAlive(root)) _shell.Tree.Walk(root, entity => { _managementActions.Remove(entity); return true; });
            _dynamic.Remove(root); _recoveryViews.Remove(root); rows.Remove(key);
        }
    }

    private void BindRecoveryAction(Dictionary<string, XsrUiEntityId> nodes, string key, Action action)
    {
        long generation = _recoveryGeneration;
        BindInstanceToolAction(nodes, key, () =>
        {
            if (_recoveryGeneration == generation && _shell.Stage.Navigation.Current == Page) action();
        });
    }

    private bool TryReloadRecoveryStorage()
    {
        if (_selected != "recovery" || _management is not { } snapshot || _recoveryInstance != _instance
            || !_shell.Tree.IsAlive(_recoveryRoot) || _shell.Tree.Parent(_recoveryRoot) != _sections) return false;
        BuildRecoveryStorage(snapshot);
        BuildRecoveryTimeline(snapshot);
        BuildOfflineReadiness(snapshot);
        return true;
    }

    private void UpdateRecoveryStorage()
    {
        if (_selected != "recovery" || !_shell.Tree.IsAlive(_recoveryRoot)) return;
        if (!ReferenceEquals(_management, _recoveryProjection) || _recoveryBusy != (_managementWrite is not null)
            || _recoveryDevelopment != InstanceDevelopmentMode || _recoveryLanguage != RecoveryTerm("confirmTitle"))
            TryReloadRecoveryStorage();
        else if (_management is { } snapshot && RecoveryTimelineNeedsProjection(snapshot))
            BuildRecoveryTimeline(snapshot);
    }

    private void BuildRecoveryStorage(InstanceManagementSnapshot snapshot)
    {
        const int pageSize = 8;
        bool fresh = !_shell.Tree.IsAlive(_recoveryRoot) || _shell.Tree.Parent(_recoveryRoot) != _sections || _recoveryInstance != _instance;
        var storage = snapshot.RecoveryStorage;
        var comparison = snapshot.RecoveryComparison;
        int pages = Math.Max(1, ((storage?.Snapshots.Count ?? 0) + pageSize - 1) / pageSize);
        int changePages = Math.Max(1, ((comparison?.Changes.Count ?? 0) + pageSize - 1) / pageSize);
        _recoverySnapshotPage = Math.Clamp(_recoverySnapshotPage, 0, pages - 1);
        _recoveryChangesPage = Math.Clamp(_recoveryChangesPage, 0, changePages - 1);
        bool canCompare = comparison is { UnavailableReason: null };
        bool canRestore = canCompare && comparison!.BaselineRevision is not null && _managementWrite is null;
        var data = new Dictionary<string, string>
        {
            ["development"] = Flag(InstanceDevelopmentMode),
            ["normal"] = Flag(!InstanceDevelopmentMode),
            ["loading"] = Flag(storage is null),
            ["loaded"] = Flag(storage is not null),
            ["hasError"] = Flag(storage?.Error is not null),
            ["error"] = storage?.Error ?? "",
            ["versionSize"] = storage is null ? "" : RecoveryFormat("EstimatedSize", ("size", RecoverySize(storage.VersionBytes)), ("estimated", storage.Complete ? "" : RecoveryTerm("estimated"))),
            ["snapshotSize"] = storage is null ? "" : RecoveryFormat("EstimatedSize", ("size", RecoverySize(storage.SnapshotBytes)), ("estimated", storage.Complete ? "" : RecoveryTerm("estimated"))),
            ["historyEmpty"] = Flag(storage is { Complete: true, Snapshots.Count: 0 }),
            ["historyUnavailable"] = Flag(storage is { Complete: false, Snapshots.Count: 0 }),
            ["historyPages"] = Flag(pages > 1),
            ["historyNext"] = RecoveryFormat("SnapshotPage", ("page", RecoveryNumber(_recoverySnapshotPage + 1)), ("pages", RecoveryNumber(pages))),
            ["comparisonLoading"] = Flag(comparison is null),
            ["comparisonUnavailable"] = Flag(comparison?.UnavailableReason is not null),
            ["unavailable"] = comparison?.UnavailableReason ?? "",
            ["comparisonEmpty"] = Flag(canCompare && comparison!.Changes.Count == 0),
            ["hasChanges"] = Flag(canCompare && comparison!.Changes.Count > 0),
            ["changeCount"] = RecoveryFormat("ChangeCount", ("count", RecoveryNumber(comparison?.Changes.Count ?? 0))),
            ["canRestore"] = Flag(canRestore),
            ["changePages"] = Flag(canCompare && changePages > 1),
            ["changeNext"] = RecoveryFormat("ChangePage", ("page", RecoveryNumber(_recoveryChangesPage + 1)), ("pages", RecoveryNumber(changePages))),
        };
        var snapshots = storage?.Snapshots.Skip(_recoverySnapshotPage * pageSize).Take(pageSize).ToArray() ?? [];
        var categories = canCompare ? comparison!.Changes.GroupBy(change => change.Category).Take(12).ToArray() : [];
        var changes = canCompare ? comparison!.Changes.Select((change, index) => (Change: change, Key: RecoveryIdentity(string.Join("\n",
            change.Path, change.Category, change.Kind.ToString(), change.SettingKey, change.Area, change.RelatedPath, RecoveryNumber(index)))))
            .Skip(_recoveryChangesPage * pageSize).Take(pageSize).ToArray() : [];
        Dictionary<string, string> SnapshotData(InstanceRecoverySnapshotSummary item) => new()
        {
            ["key"] = "RecoverySnapshot." + item.Revision.ToString("N"),
            ["time"] = RecoveryFormat("SnapshotTime", ("time", item.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)),
                ("latest", item.Revision == comparison?.BaselineRevision ? RecoveryTerm("latest") : "")),
            ["summary"] = RecoveryFormat("SnapshotSummary", ("files", RecoveryNumber(item.Files)), ("size", RecoverySize(item.ContentBytes))),
        };
        Dictionary<string, string> CategoryData(string category, int count) => new()
        {
            ["key"] = "RecoveryCategory." + RecoveryIdentity(category),
            ["restore"] = "RecoveryCategory.Restore." + RecoveryIdentity(category),
            ["label"] = RecoveryFormat("Category", ("category", RecoveryCategory(category)), ("count", RecoveryNumber(count))),
            ["enabled"] = Flag(canRestore),
        };
        Dictionary<string, string> ChangeData(InstanceRecoveryChange item, string key) => new()
        {
            ["key"] = "RecoveryChange." + key,
            ["restore"] = "RecoveryChange.Restore." + key,
            ["kind"] = RecoveryFormat("ChangeKind", ("kind", RecoveryKind(item.Kind)), ("category", RecoveryCategory(item.Category))),
            ["path"] = new string(item.Path.Select(character => char.IsControl(character) ? ' ' : character).ToArray()),
            ["enabled"] = Flag(canRestore),
        };
        // Compile every bounded candidate before touching retained controls or retiring rows.
        var snapshotData = snapshots.ToDictionary(item => item.Revision.ToString("N"), SnapshotData, StringComparer.Ordinal);
        var categoryData = categories.ToDictionary(category => category.Key, category => CategoryData(category.Key, category.Count()), StringComparer.Ordinal);
        var changeData = changes.ToDictionary(item => item.Key, item => ChangeData(item.Change, item.Key), StringComparer.Ordinal);
        foreach (var values in snapshotData.Values) _ = DesktopMarkup.Dynamic("SettingsRecoverySnapshot.d.nxml", values);
        foreach (var values in categoryData.Values) _ = DesktopMarkup.Dynamic("SettingsRecoveryCategory.d.nxml", values);
        foreach (var values in changeData.Values) _ = DesktopMarkup.Dynamic("SettingsRecoveryChange.d.nxml", values);
        var root = RecoveryView(_sections, fresh ? default : _recoveryRoot, "SettingsRecoveryWorkspace.d.nxml", data);
        if (fresh)
        {
            if (_shell.Tree.IsAlive(_recoveryRoot))
            {
                _shell.Tree.Walk(_recoveryRoot, entity => { _managementActions.Remove(entity); return true; });
                _dynamic.Remove(_recoveryRoot);
            }
            _recoveryViews.Clear(); _recoveryViews[root] = ("SettingsRecoveryWorkspace.d.nxml", data);
            _recoverySnapshots.Clear(); _recoveryCategories.Clear(); _recoveryChanges.Clear(); _recoveryPolicyBuilt = false;
            _recoveryGeneration++;
        }
        if (!ReferenceEquals(comparison, _recoveryComparison)) _recoveryGeneration++;
        _recoveryRoot = root; _recoveryInstance = _instance; _recoveryComparison = comparison;
        _recoveryProjection = snapshot; _recoveryBusy = _managementWrite is not null; _recoveryDevelopment = InstanceDevelopmentMode;
        _recoveryLanguage = RecoveryTerm("confirmTitle");
        var nodes = CatalogNodes(root);
        if (!_recoveryPolicyBuilt && _catalog?.Entries.FirstOrDefault(item => item.Id == "instance.recovery.keep-history") is { } setting)
        { BuildRow(nodes["RecoveryPolicy.Body"], setting); _recoveryPolicyBuilt = true; }
        BindRecoveryAction(nodes, "RecoveryHistory.Next", () =>
        { _recoverySnapshotPage = (_recoverySnapshotPage + 1) % pages; BuildRecoveryStorage(snapshot); UpdateEditors(); });
        BindRecoveryAction(nodes, "RecoveryChanges.Next", () =>
        { _recoveryChangesPage = (_recoveryChangesPage + 1) % changePages; BuildRecoveryStorage(snapshot); UpdateEditors(); });
        BindRecoveryAction(nodes, "RecoveryChanges.RestoreAll", () =>
        { if (comparison is not null) RestoreChanges(comparison, comparison.Changes); });

        RetireRecoveryRows(_recoverySnapshots, snapshots.Select(item => item.Revision.ToString("N")));
        foreach (var item in snapshots)
        {
            string key = item.Revision.ToString("N");
            _recoverySnapshots[key] = RecoveryView(nodes["RecoveryHistory.Rows"], _recoverySnapshots.GetValueOrDefault(key), "SettingsRecoverySnapshot.d.nxml", snapshotData[key]);
        }
        _dynamic.Reorder(nodes["RecoveryHistory.Rows"], snapshots.Select(item => _recoverySnapshots[item.Revision.ToString("N")]).ToArray());
        RetireRecoveryRows(_recoveryCategories, categories.Select(category => category.Key));
        foreach (var category in categories)
        {
            var selected = category.ToArray(); string key = RecoveryIdentity(category.Key);
            var row = RecoveryView(nodes["RecoveryChanges.Categories"], _recoveryCategories.GetValueOrDefault(category.Key), "SettingsRecoveryCategory.d.nxml", categoryData[category.Key]);
            _recoveryCategories[category.Key] = row;
            BindRecoveryAction(CatalogNodes(row), "RecoveryCategory.Restore." + key, () => RestoreChanges(comparison!, selected));
        }
        _dynamic.Reorder(nodes["RecoveryChanges.Categories"], categories.Select(category => _recoveryCategories[category.Key]).ToArray());
        RetireRecoveryRows(_recoveryChanges, changes.Select(item => item.Key));
        foreach (var (item, key) in changes)
        {
            var row = RecoveryView(nodes["RecoveryChanges.Rows"], _recoveryChanges.GetValueOrDefault(key), "SettingsRecoveryChange.d.nxml", changeData[key]);
            _recoveryChanges[key] = row;
            BindRecoveryAction(CatalogNodes(row), "RecoveryChange.Restore." + key, () => RestoreChanges(comparison!, [item]));
        }
        _dynamic.Reorder(nodes["RecoveryChanges.Rows"], changes.Select(item => _recoveryChanges[item.Key]).ToArray());
    }

    private bool RecoveryScopeCurrent(InstanceRecoveryReport report) => !_disposed && _visible && _selected == "recovery"
        && _shell.Stage.Navigation.Current == Page && _managementWrite is null
        && _instance == report.InstanceDirectory && _instanceDirectory?.Invoke() == report.InstanceDirectory
        && ReferenceEquals(report, _recoveryComparison) && _shell.Tree.IsAlive(_recoveryRoot);

    private void RestoreChanges(InstanceRecoveryReport report, IReadOnlyList<InstanceRecoveryChange> changes)
    {
        if (!RecoveryScopeCurrent(report) || report.BaselineRevision is not { } revision
            || !_commands.TryResolve(InstanceRecoveryContract.Restore, out var route)) return;
        long generation = _recoveryGeneration;
        var selected = changes.ToArray();
        _feedback.ShowDialog("recovery.confirm", RecoveryTerms.Value["confirmTitle"], RecoveryFormat("Confirm", ("count", RecoveryNumber(selected.Length))),
            RecoveryTerms.Value["restore"], RecoveryTerms.Value["cancel"], accepted =>
        {
            if (!accepted || generation != _recoveryGeneration || !RecoveryScopeCurrent(report)) return;
            _managementWriteInstance = report.InstanceDirectory;
            _managementWrite = _commands.Dispatch(route, new InstanceRecoveryRestoreCommand(report.InstanceDirectory, revision,
                report.Fingerprint, selected)).Completion;
            WakeOnPlatformCompletion(_managementWrite);
            BuildRecoveryStorage(_management!);
        }, localizeMessage: false);
    }
}
