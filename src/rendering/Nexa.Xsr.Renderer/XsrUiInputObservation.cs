namespace Nexa.Xsr.Renderer;

/// <summary>Renderer input facts. Consumers must scope observations to their owned entities.</summary>
public sealed record XsrUiInputObservation(string Kind, string Phase, double X = 0, double Y = 0, string? Text = null);
public sealed partial class XsrUiRenderer
{
    public event Action<XsrUiEntityId, XsrUiInputObservation>? InputObserved;
    private void ObserveInput(XsrUiEntityId entity, string kind, string phase, double x = 0, double y = 0, string? text = null)
    {
        if (InputObserved is { } observer && entity.IsAssigned && _tree.IsAlive(entity) && IsInVisibleTree(entity) && IsEnabled(_tree.GetComponent<XsrUiInput>(entity))) observer(entity, new(kind, phase, x, y, text));
    }
}
