using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostShortcutSet(long ExpectedRevision, HostShortcutBinding[] Bindings);
public sealed record HostShortcutBinding(int Document, string Gesture, string Command, string Argument);
public static class SidecarShortcutControlRoutes
{
    public static readonly XsrSemanticId Replace = XsrSemanticId.Parse("presentation.shortcuts.replace");
}
