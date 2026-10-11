using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    internal Action<Uri>? OpenAboutLink { get; set; }
    internal Func<CancellationToken, Task<bool>>? ExportDiagnostics { get; set; }
    private void BuildAboutPage()
    {
        var root = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsAbout.d.nxml", new Dictionary<string, string>
        {
            ["product"] = Program.ProductDisplayTitle(_shell.Version),
            ["links"] = Flag(OpenAboutLink is not null),
            ["diagnostics"] = Flag(ExportDiagnostics is not null),
        }));
        var nodes = CatalogNodes(root);
        if (OpenAboutLink is { } open)
        {
            _managementActions[nodes["Management.源代码"]] = () => open(new Uri("https://github.com/PCL-N-Edition/PCL-N"));
            _managementActions[nodes["Management.完整许可证"]] = () => open(new Uri("https://github.com/PCL-N-Edition/PCL-N/blob/refactor/xsr/LICENSE"));
            _managementActions[nodes["Management.贡献者"]] = () => open(new Uri("https://github.com/PCL-N-Edition/PCL-N/graphs/contributors"));
        }
        if (ExportDiagnostics is not null) BuildDiagnosticExportControls(nodes["AboutDiagnosticActions"]);
        var scroll = _shell.Tree.GetComponent<XsrUiScroll>(_sections)!;
        scroll.OffsetY = _scrollPositions.GetValueOrDefault(_selected);
        _shell.Tree.GetComponent<XsrUiTransition>(_sections)!.Key = _selected;
        _shell.Tree.MarkDirty(_sections, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }

}
