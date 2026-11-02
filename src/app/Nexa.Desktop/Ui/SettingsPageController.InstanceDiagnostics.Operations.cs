using Nexa.Services.Logging;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private bool _instanceOperationView;
    private int _instanceOperationPage;
    private void ProjectInstanceOperationRows(XsrUiEntityId slot, DurableDiagnosticEntry[] entries, int page)
    {
        int index = 0;
        foreach (var entry in entries.Skip(page * 12).Take(12))
            _dynamic.Append(slot, DesktopMarkup.Dynamic("SettingsInstanceOperationRow.d.nxml", new Dictionary<string, string>
            {
                ["row"] = "InstanceDiagnostics.Operation." + index++,
                ["OperationTimestamp"] = InstanceTime(entry.Timestamp),
                ["OperationSession"] = entry.Instance!.SessionId.ToString("N"),
                ["OperationModule"] = entry.Module,
                ["OperationName"] = entry.Operation,
                ["OperationStage"] = entry.Stage,
                ["OperationOutcome"] = InstanceDiagnosticOutcome(entry.Outcome, operation: true),
            }));
    }
}
