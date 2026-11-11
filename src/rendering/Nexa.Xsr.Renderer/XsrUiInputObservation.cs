namespace Nexa.Xsr.Renderer;

/// <summary>Renderer input facts. Consumers must scope observations to their owned entities.</summary>
public sealed record XsrUiInputObservation(string Kind, string Phase, double X = 0, double Y = 0, string? Text = null);
public sealed partial class XsrUiRenderer
{
    public event Action<XsrUiEntityId, XsrUiInputObservation>? InputObserved;
    private void ObserveInput(XsrUiEntityId entity, XsrUiInputObservation value)
    {
        if (entity.IsAssigned && _tree.IsAlive(entity) && IsInVisibleTree(entity) && IsEnabled(_tree.GetComponent<XsrUiInput>(entity))) InputObserved?.Invoke(entity, value);
    }
}
