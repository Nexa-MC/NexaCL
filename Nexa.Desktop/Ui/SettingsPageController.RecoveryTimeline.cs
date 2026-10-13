using System.Globalization;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private XsrUiEntityId _recoveryTimelineRoot;
    private string? _recoveryTimelineInstance, _recoveryTimelineLanguage;
    private Func<string, string>? _recoveryTimelineLocalizer;
    private InstanceRecoveryStorage? _recoveryTimelineStorage;
    private InstanceRecoveryReport? _recoveryTimelineComparison;
    private long _recoveryTimelineTextRevision;
    private readonly Dictionary<Guid, (XsrUiEntityId Root, string Template)> _recoveryTimelineRows = [];

    private bool RecoveryTimelineNeedsProjection(InstanceManagementSnapshot snapshot) =>
        !_shell.Tree.IsAlive(_recoveryTimelineRoot) || _shell.Tree.Parent(_recoveryTimelineRoot) != _sections
        || _recoveryTimelineInstance != _instance || !ReferenceEquals(snapshot.RecoveryStorage, _recoveryTimelineStorage)
        || !ReferenceEquals(snapshot.RecoveryComparison, _recoveryTimelineComparison)
        || !ReferenceEquals(_recoveryTimelineLocalizer, _shell.Renderer.TextLocalizer)
        || _recoveryTimelineLanguage != DesktopResourceText.Language(_store);

    private void BuildRecoveryTimeline(InstanceManagementSnapshot snapshot)
    {
        if (_selected != "recovery" || !RecoveryTimelineNeedsProjection(snapshot)) return;
        var storage = snapshot.RecoveryStorage;
        var points = storage is { Complete: true }
            ? storage.Snapshots.OrderBy(item => item.CapturedAt).ThenBy(item => item.Revision).TakeLast(16).ToArray() : [];
        bool fresh = !_shell.Tree.IsAlive(_recoveryTimelineRoot) || _shell.Tree.Parent(_recoveryTimelineRoot) != _sections
            || _recoveryTimelineInstance != _instance;
        bool languageChanged = !ReferenceEquals(_recoveryTimelineLocalizer, _shell.Renderer.TextLocalizer)
            || _recoveryTimelineLanguage != DesktopResourceText.Language(_store);
        long textRevision = _recoveryTimelineTextRevision + (languageChanged ? 1 : 0);
        var rootData = new Dictionary<string, string>
        {
            ["incomplete"] = Flag(storage is not { Complete: true }),
            ["empty"] = Flag(storage is { Complete: true } && points.Length == 0),
            ["hasPoints"] = Flag(points.Length > 0),
            ["more"] = Flag(storage?.Snapshots.Count > points.Length),
            ["hasComparison"] = Flag(snapshot.RecoveryComparison is { UnavailableReason: null }),
            ["changes"] = InstanceNumber(snapshot.RecoveryComparison?.Changes.Count ?? 0),
            ["language"] = InstanceNumber(textRevision),
        };
        var candidates = new List<(InstanceRecoverySnapshotSummary Point, string Template, Dictionary<string, string> Data)>();
        InstanceRecoverySnapshotSummary? previous = null;
        foreach (var point in points)
        {
            string template = "SettingsInstanceTimeline" + (previous is null ? "First" : "Delta")
                + (point.Revision == snapshot.RecoveryComparison?.BaselineRevision ? "Baseline" : "") + ".d.nxml";
            var data = new Dictionary<string, string>
            {
                ["row"] = "RecoveryTimeline." + point.Revision,
                ["time"] = point.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                ["files"] = InstanceNumber(point.Files),
                ["size"] = InstanceToolSize(point.ContentBytes),
                ["language"] = InstanceNumber(textRevision),
            };
            if (previous is not null) data["delta"] = (point.Files - previous.Files).ToString("+0;-0;0", CultureInfo.InvariantCulture);
            candidates.Add((point, template, data));
            previous = point;
        }
        // Validate all formats before mutating the retained timeline or removing old rows.
        _ = DesktopMarkup.Dynamic("SettingsInstanceTimeline.d.nxml", rootData, _shell.Renderer.LocalizeText);
        foreach (var candidate in candidates)
            _ = DesktopMarkup.Dynamic(candidate.Template, candidate.Data, _shell.Renderer.LocalizeText);
        if (fresh)
        {
            _recoveryTimelineRows.Clear();
            _recoveryTimelineRoot = AppendInstanceTool(_sections, "SettingsInstanceTimeline.d.nxml", rootData);
        }
        else ReloadInstanceTool(_recoveryTimelineRoot, rootData);
        var rows = CatalogNodes(_recoveryTimelineRoot)["RecoveryTimeline.Rows"];
        var wanted = points.Select(point => point.Revision).ToHashSet();
        foreach (var revision in _recoveryTimelineRows.Keys.Where(revision => !wanted.Contains(revision)).ToArray())
        {
            var row = _recoveryTimelineRows[revision].Root;
            _dynamic.Remove(row); _instanceToolViews.Remove(row); _recoveryTimelineRows.Remove(revision);
        }
        foreach (var candidate in candidates)
        {
            if (_recoveryTimelineRows.TryGetValue(candidate.Point.Revision, out var current)
                && (current.Template != candidate.Template || !_shell.Tree.IsAlive(current.Root)))
            {
                _dynamic.Remove(current.Root); _instanceToolViews.Remove(current.Root);
                _recoveryTimelineRows.Remove(candidate.Point.Revision);
            }
            if (_recoveryTimelineRows.TryGetValue(candidate.Point.Revision, out current))
                ReloadInstanceTool(current.Root, candidate.Data);
            else _recoveryTimelineRows[candidate.Point.Revision] =
                (AppendInstanceTool(rows, candidate.Template, candidate.Data), candidate.Template);
        }
        _dynamic.Reorder(rows, points.Select(point => _recoveryTimelineRows[point.Revision].Root).ToArray());
        _recoveryTimelineStorage = storage; _recoveryTimelineComparison = snapshot.RecoveryComparison;
        _recoveryTimelineInstance = _instance; _recoveryTimelineTextRevision = textRevision;
        _recoveryTimelineLocalizer = _shell.Renderer.TextLocalizer;
        _recoveryTimelineLanguage = DesktopResourceText.Language(_store);
    }
}
