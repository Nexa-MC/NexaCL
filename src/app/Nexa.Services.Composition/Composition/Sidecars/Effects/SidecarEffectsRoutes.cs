using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public static class SidecarEffectsRoutes
{
    public static readonly XsrSemanticId AccountEligibility = XsrSemanticId.Parse("accounts.launch.eligibility");
    public static readonly XsrSemanticId AccountRefresh = XsrSemanticId.Parse("accounts.session.refresh");
    public static readonly XsrSemanticId JavaInspect = XsrSemanticId.Parse("java.executable.inspect");
    public static readonly XsrSemanticId JavaSelect = XsrSemanticId.Parse("java.selection.resolve");
    public static readonly XsrSemanticId JavaRequirements = XsrSemanticId.Parse("java.requirements.read");
    public static readonly XsrSemanticId Power = XsrSemanticId.Parse("platform.power.read");
    public static readonly XsrSemanticId Preflight = XsrSemanticId.Parse("minecraft.preflight.analyze");
    public static readonly XsrSemanticId SettingsDefinitions = XsrSemanticId.Parse("settings.definitions.read");
    public static readonly XsrSemanticId SettingsValidate = XsrSemanticId.Parse("settings.override.validate");
    public static readonly XsrSemanticId WorldSnapshots = XsrSemanticId.Parse("minecraft.world.snapshots.read");
}
