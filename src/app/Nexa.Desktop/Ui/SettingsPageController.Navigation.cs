using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private void BuildNavigation()
    {
        foreach (var page in Pages)
        {
            if (!_pages.TryGetValue(page.Id, out var body))
            {
                body = _dynamic.Append(_pager, DesktopMarkup.Dynamic("SettingsSection.d.nxml", new Dictionary<string, string> { ["id"] = page.Id }));
                _pages[page.Id] = body;
            }
            if (page.Id == _selected) _sections = body;
            var button = _dynamic.Append(_navigationRoot, DesktopMarkup.Dynamic("SettingsNavigationOption.d.nxml",
                NavigationData(page.Id, page.Label, false)));
            _navigation[button] = page.Id;
        }
        _dynamic.Reorder(_pager, Pages.Select(page => _pages[page.Id]).ToArray());
        UpdateNavigation();
    }

    private static Dictionary<string, string> NavigationData(string id, string label, bool selected, double? width = null) => new()
    {
        ["key"] = "SettingsNav." + id,
        ["label"] = label,
        ["selected"] = Flag(selected),
        ["width"] = (width ?? CatalogCaptionWidth("navigation", label)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["style"] = selected ? "settings-navigation-option settings-navigation-option-selected" : "settings-navigation-option",
    };

    private readonly Dictionary<XsrUiEntityId, (string Label, double Width, bool Selected)> _navigationProjection = [];
    private void UpdateNavigation()
    {
        foreach (var stale in _navigationProjection.Keys.Where(entity => !_shell.Tree.IsAlive(entity)).ToArray()) _navigationProjection.Remove(stale);
        foreach (var (button, id) in _navigation)
        {
            bool selected = id == _selected;
            string label = _shell.Tree.GetComponent<XsrUiText>(button)!.Content;
            double width = CatalogCaptionWidth("navigation", _shell.Renderer.LocalizeText(label));
            var projected = (label, width, selected);
            if (!_navigationProjection.TryGetValue(button, out var previous) || previous != projected)
            {
                _dynamic.Reload(button, DesktopMarkup.Dynamic("SettingsNavigationOption.d.nxml", NavigationData(id, label, selected, width)));
                _navigationProjection[button] = projected;
            }
            if (selected) _shell.Tree.GetComponent<XsrUiSegmentedTrack>(_navigationRoot)!.Selected = button;
        }
    }

    private void UpdateNavigationWidths() => UpdateNavigation();
}
