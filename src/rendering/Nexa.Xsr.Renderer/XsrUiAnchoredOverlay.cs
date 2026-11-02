namespace Nexa.Xsr.Renderer;

/// <summary>Places a floating panel directly below an already arranged sibling anchor.</summary>
public sealed record XsrUiAnchoredOverlay(XsrUiEntityId Anchor, double Gap = 4);
