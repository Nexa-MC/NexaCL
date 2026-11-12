using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Java;

public static class JavaRuntimeInventoryContract
{
    public static readonly XsrSemanticId Query = XsrSemanticId.Parse("java.runtime.inventory.read");
    public static readonly XsrSemanticId Manage = XsrSemanticId.Parse("java.runtime.registry.manage");
    public static readonly XsrSemanticId InstallArchive = XsrSemanticId.Parse("java.runtime.archive.install");
    public const string RegistryKey = "NexaJavaRuntimeRegistry";
}
public sealed record JavaRuntimeInventoryQuery(bool Refresh = false);
public sealed record JavaRuntimeInventorySnapshot(IReadOnlyList<JavaRuntimeCandidate> Runtimes)
{
    public long RegistryRevision { get; init; }
    public IReadOnlyList<JavaRuntimeRegistration> Registrations { get; init; } = [];
    public IReadOnlyList<JavaRuntimeManagedEntry> ManagedRuntimes { get; init; } = [];
}
public sealed record JavaRuntimeRegistration(string Executable, bool Enabled = true, bool Custom = true);
public sealed record JavaRuntimeRegistrySnapshot(long Revision, IReadOnlyList<JavaRuntimeRegistration> Registrations);
public sealed record JavaRuntimeManagedEntry(string Executable, string Directory, string Identity);
public enum JavaRuntimeManagementAction { Add, Remove, Enable, Disable, DeleteManaged }
public sealed record JavaRuntimeManageCommand(string Executable, JavaRuntimeManagementAction Action, long ExpectedRevision)
{
    public string? ExpectedManagedIdentity { get; init; }
}
public interface IJavaRuntimeRegistrationStore
{
    JavaRuntimeRegistrySnapshot Read();
    XsrResult Write(long expectedRevision, IReadOnlyList<JavaRuntimeRegistration> registrations);
}

/// <summary>Canonical registration access for owners hosted in a separate runtime.</summary>
public interface IAsyncJavaRuntimeRegistrationStore
{
    ValueTask<JavaRuntimeRegistrySnapshot> ReadAsync(CancellationToken token = default);
    ValueTask<XsrResult> WriteAsync(long expectedRevision, IReadOnlyList<JavaRuntimeRegistration> registrations,
        CancellationToken token = default);
}

public sealed record JavaArchiveInstallCommand(string Archive, string Destination, long Size, string Sha256, int Major, string Architecture, long ExpectedRegistryRevision);
