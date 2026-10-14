namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>Original host presentation facts retained for authoring diagnostics, never theme state.</summary>
public sealed record NxmlAuthoredStyleSource(IReadOnlyList<NxmlStyleDeclaration> Declarations);
