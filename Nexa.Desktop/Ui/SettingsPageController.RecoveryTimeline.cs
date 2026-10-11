using System.Globalization;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private void BuildRecoveryTimeline(InstanceManagementSnapshot snapshot)
    {
        if (_selected != "recovery") return;
        var storage = snapshot.RecoveryStorage;
        var points = storage is { Complete: true }
            ? storage.Snapshots.OrderBy(item => item.CapturedAt).ThenBy(item => item.Revision).TakeLast(16).ToArray() : [];
        var root = AppendInstanceTool(_sections, "SettingsInstanceTimeline.d.nxml", new()
        {
            ["incomplete"] = Flag(storage is not { Complete: true }),
            ["empty"] = Flag(storage is { Complete: true } && points.Length == 0),
            ["hasPoints"] = Flag(points.Length > 0), ["more"] = Flag(storage?.Snapshots.Count > points.Length),
            ["hasComparison"] = Flag(snapshot.RecoveryComparison is { UnavailableReason: null }),
            ["changes"] = InstanceNumber(snapshot.RecoveryComparison?.Changes.Count ?? 0)
        });
        var rows = CatalogNodes(root)["RecoveryTimeline.Rows"];
        InstanceRecoverySnapshotSummary? previous = null;
        foreach (var point in points)
        {
            string template = "SettingsInstanceTimeline" + (previous is null ? "First" : "Delta")
                + (point.Revision == snapshot.RecoveryComparison?.BaselineRevision ? "Baseline" : "") + ".d.nxml";
            var data = new Dictionary<string, string>
            {
                ["row"] = "RecoveryTimeline." + point.Revision,
                ["time"] = point.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                ["files"] = InstanceNumber(point.Files), ["size"] = InstanceToolSize(point.ContentBytes)
            };
            if (previous is not null) data["delta"] = (point.Files - previous.Files).ToString("+0;-0;0", CultureInfo.InvariantCulture);
            _dynamic.Append(rows, DesktopMarkup.Dynamic(template, data, _shell.Renderer.LocalizeText));
            previous = point;
        }
    }
}
