namespace Nexa.Xsr.Renderer;

/// <summary>Window-facing shell ownership boundary. Tree, state store and stage remain private to the owner.</summary>
public interface IXsrUiBackendShell
{
    string Title { get; }
    string Version { get; }
    XsrUiEntityId Root { get; }
    XsrUiEntityId CurrentPage { get; }
    bool HasContentTransition { get; }
    XsrSemanticId SelectedNavigationId { get; }
    XsrUiShellPalette Palette { get; }
    bool IsNavigationExpanded { get; }
    double RailPresentationProgress { get; }
    IXsrUiBackendRenderer Renderer { get; }
    event EventHandler? RenderRequested;
    event EventHandler? NavigationExpandedChanged;
    XsrUiScene Render(XsrUiSize viewport);
    void SetRailPresentationProgress(double value);
    void PublishWindowActivity(bool active, bool minimized);
    void PublishWindowMetrics(double trafficLightPadding, bool fullscreen, double contentInset = 0);
}
