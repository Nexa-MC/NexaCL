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
        var root = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsInstanceDevelopment.d.nxml"));
        var slot = CatalogNodes(root)["InstanceDevelopmentMode.Control"];
        if (_catalog?.Entries.FirstOrDefault(item => item.Id == "instance.development-mode") is { } setting)
            BuildRow(slot, setting);
    }
}
