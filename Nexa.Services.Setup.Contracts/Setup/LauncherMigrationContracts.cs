namespace Nexa.Services.Setup;

public enum LauncherMigrationSourceKind { Auto, Nexa, Prism, Modrinth, CurseForge, Hmcl, Xmcl, AtLauncher, Minecraft }

public sealed record LauncherMigrationQuery(LauncherMigrationSourceKind SourceKind, string SourceDirectory, string DestinationDirectory);
public sealed record LauncherMigrationInstance(string Id, string Name, string SourceDirectory, string GameVersion,
    string? Loader, string? LoaderBuild, long Bytes, int Files, bool CanImport, string? UnsupportedReason,
    IReadOnlyList<string> Warnings);
public sealed record LauncherMigrationPreview(LauncherMigrationQuery Query, string Revision,
    IReadOnlyList<LauncherMigrationInstance> Instances, IReadOnlyList<string> Warnings);
public sealed record LauncherMigrationInstallRequest(string DestinationRoot, string InstanceId, string GameVersion,
    string? Loader, string? LoaderBuild);
public sealed record LauncherMigrationProgress(string Stage, string InstanceName, long CompletedBytes, long TotalBytes);
public sealed record LauncherMigrationImportedInstance(string Id, string Name, string MinecraftRootDirectory, string InstanceId);
public sealed record LauncherMigrationReceipt(string Identity, string DestinationDirectory,
    IReadOnlyList<LauncherMigrationImportedInstance> Instances);
public sealed record LauncherMigrationApplyCommand(LauncherMigrationPreview Preview, IReadOnlyList<string> SelectedInstanceIds);
public sealed record LauncherMigrationUndoQuery(string DestinationDirectory);
public sealed record LauncherMigrationUndoCommand(LauncherMigrationReceipt Receipt);

/// <summary>Authenticates migration receipts using host-owned storage outside the imported directory.</summary>
public interface ILauncherMigrationReceiptAuthority
{
    Task AuthorizeAsync(string receiptPath, ReadOnlyMemory<byte> bytes, CancellationToken token);
    void Verify(string receiptPath, ReadOnlyMemory<byte> bytes);
}

public static class LauncherMigrationContract
{
    public static readonly Nexa.Xsr.XsrSemanticId Preview = Nexa.Xsr.XsrSemanticId.Parse("setup.migration.launcher.preview");
    public static readonly Nexa.Xsr.XsrSemanticId Apply = Nexa.Xsr.XsrSemanticId.Parse("setup.migration.launcher.apply");
    public static readonly Nexa.Xsr.XsrSemanticId ReadReceipt = Nexa.Xsr.XsrSemanticId.Parse("setup.migration.launcher.receipt");
    public static readonly Nexa.Xsr.XsrSemanticId Undo = Nexa.Xsr.XsrSemanticId.Parse("setup.migration.launcher.undo");
}
