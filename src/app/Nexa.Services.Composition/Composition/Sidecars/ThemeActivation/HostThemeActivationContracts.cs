using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostThemeActivation(string SourceIdentity, string SourceRevision, long ExpectedRevision);
public sealed record HostThemePreview(HostThemeActivation Theme, long LeaseId);
public sealed record HostThemePreviewEnd(long LeaseId);
public sealed record SidecarThemeActivationActions(
    Func<HostThemeActivation, bool, CancellationToken, Task<XsrResult<long>>> Activate,
    Func<long, CancellationToken, Task<XsrResult>> EndPreview);
public static class SidecarThemeActivationRoutes
{
    public static readonly XsrSemanticId Activate = XsrSemanticId.Parse("themes.activation.commit");
    public static readonly XsrSemanticId Preview = XsrSemanticId.Parse("themes.activation.preview");
    public static readonly XsrSemanticId EndPreview = XsrSemanticId.Parse("themes.activation.end_preview");
}
