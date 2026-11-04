using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Management;

public sealed record InstanceResourcePack(string Name, bool Enabled, int Order, string FormatRange, bool? Compatible, string Problem);
public sealed record InstanceResourcePackConfiguration(string Revision, IReadOnlyList<InstanceResourcePack> Packs, string TargetFormat, string Problem);
public sealed record InstanceResourcePackCommand(string InstanceDirectory, string ExpectedRevision, string Name, bool Enabled, int Order, bool AllowIncompatible = false);
public static class InstanceResourcePackContract
{
    public static readonly XsrSemanticId Set = XsrSemanticId.Parse("minecraft.instance.resource-pack.set");
}
