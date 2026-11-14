using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostConfirmationRequest(string Title, byte[] Utf8Details);
public sealed record HostConfirmationDecision(bool Accepted);
public static class SidecarConfirmationRoutes
{
    public static readonly XsrSemanticId Ask = XsrSemanticId.Parse("interaction.confirmation.ask");
}
