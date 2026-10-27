namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static string LoadingWeight(bool loading) => loading ? "1" : "0";

    private void UpdatePageLoading()
    {
        string? message = _catalog is null && _catalogReading is not null
            ? ProductPresentationText.Raw("loading.settings")
            : _instanceDirectory is not null && _management is null && _managementRead is not null && _selected != "game"
                ? ContentTerm("loading") : null;
        bool loading = message is not null;
        if (_pageLoading == loading && _pageLoadingMessage == (message ?? "")) return;
        _pageLoading = loading; _pageLoadingMessage = message ?? "";
        _dynamic.Reload(_settingsWorkspace, DesktopMarkup.Dynamic(
            _instanceDirectory is null ? "SettingsPageWorkspace.d.nxml" : "VersionSettingsPageWorkspace.d.nxml",
            new Dictionary<string, string> { ["loading"] = Flag(loading), ["message"] = message ?? "" }));
    }
}
