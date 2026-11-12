



namespace Nexa.Services.Minecraft.Install;

public sealed record MinecraftInstallRecoveryCommand(IReadOnlyList<string> RootDirectories);

public sealed record MinecraftInstallRecoveryRecord(string RootDirectory, string Identity, string Instance, string Revision);
public sealed record MinecraftInstallRecoveryListing(IReadOnlyList<MinecraftInstallRecoveryRecord> Items);
public sealed record MinecraftInstallRecoveryListQuery(IReadOnlyList<string> RootDirectories);
public sealed record MinecraftInstallRecoveryRecordCommand(string RootDirectory, string Identity, string ExpectedRevision, bool Discard);
public static class MinecraftInstallRecoveryRecordsContract
{
    public static readonly Nexa.Xsr.XsrSemanticId List = Nexa.Xsr.XsrSemanticId.Parse("minecraft.install.recovery.list");
    public static readonly Nexa.Xsr.XsrSemanticId Apply = Nexa.Xsr.XsrSemanticId.Parse("minecraft.install.recovery.apply");
}
