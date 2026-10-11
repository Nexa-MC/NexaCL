using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private bool InstanceDevelopmentMode => _instanceDirectory is not null && _values?.Values.Any(item =>
        item.Key == "instance.development-mode" && item.ValidationError is null && item.Value.Value == "true") == true;

    private static bool IsDevelopmentPage(string page) => page is "modpack" or "contentgraph" or "recovery" or "files" or "diagnostics";

    private void RebuildDevelopmentMode()
    {
        CancelManagementRead(); CancelOfflineReadiness(); CancelWorldSnapshots();
        ReleaseContentGraph();
        _contentDetail = null;
        if (!InstanceDevelopmentMode && IsDevelopmentPage(_selected)) _selected = "overview";
        RebuildManagementNavigation();
        BuildSections(navigating: true);
    }

    private void BuildInstanceDevelopmentMode()
    {
        var group = FormGroup(_sections, "InstanceDevelopmentMode", "整合包开发");
        if (_catalog?.Entries.FirstOrDefault(item => item.Id == "instance.development-mode") is { } setting)
            BuildRow(group, setting);
        var hint = Text(group, "显示导出、依赖网络、快照回滚和诊断工具，并自动保留历史快照。", 12, Muted, 38);
        _shell.Tree.GetComponent<XsrUiVisualStyle>(hint)!.WrapText = true;
    }
}
