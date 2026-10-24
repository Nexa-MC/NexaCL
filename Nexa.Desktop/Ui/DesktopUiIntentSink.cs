using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

/// <summary>
/// The Desktop composition-edge dispatcher for semantic UI intent. The shell itself owns local
/// selection; product vertical slices subscribe explicit typed bindings here without letting a
/// backend call services directly.
/// </summary>
internal sealed class DesktopUiIntentSink : IXsrUiIntentSink
{
    private Func<DesktopUiIntent, bool>[] _routes = [];
    public event EventHandler<DesktopUiIntentEventArgs>? IntentReceived;
    public event EventHandler<DesktopUiIntentEventArgs>? IntentEmitted;

    internal IDisposable Route(Func<DesktopUiIntent, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _routes = [.. _routes, handler];
        return new RouteLease(this, handler);
    }

    public void Emit(XsrSemanticId command, XsrUiEntityId source, XsrCorrelationId correlationId)
    {
        var intent = new DesktopUiIntent(command, source, correlationId);
        var args = new DesktopUiIntentEventArgs(intent);
        IntentReceived?.Invoke(this, args);
        foreach (var route in _routes) if (route(intent)) return;
        IntentEmitted?.Invoke(this, args);
    }

    private sealed class RouteLease(DesktopUiIntentSink owner, Func<DesktopUiIntent, bool> handler) : IDisposable
    {
        public void Dispose() => owner._routes = owner._routes.Where(route => route != handler).ToArray();
    }
}

/// <summary>One semantic intent that crossed the UI.Next-to-Desktop composition boundary.</summary>
internal readonly record struct DesktopUiIntent(
    XsrSemanticId Command,
    XsrUiEntityId Source,
    XsrCorrelationId CorrelationId);

/// <summary>Event data for one semantic UI intent at the Desktop composition edge.</summary>
internal sealed class DesktopUiIntentEventArgs(DesktopUiIntent intent) : EventArgs
{
    public DesktopUiIntent Intent { get; } = intent;
}
