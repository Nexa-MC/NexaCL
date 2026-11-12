using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Process;

public enum MinecraftProcessInteraction { RequestClose, RevealWindow }
public sealed record MinecraftProcessInteractionCommand(Guid SessionId, MinecraftProcessInteraction Interaction);
public sealed record MinecraftProcessMetricsQuery(Guid SessionId);
public sealed record MinecraftProcessMetrics(long WorkingSetBytes, double CpuPercent, DateTimeOffset ObservedAt);
public static class MinecraftProcessInteractionContract
{
    public static readonly XsrSemanticId Interact = XsrSemanticId.Parse("minecraft.process.interact");
    public static readonly XsrSemanticId Metrics = XsrSemanticId.Parse("minecraft.process.metrics");
}
