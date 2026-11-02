using Nexa.Xsr;

namespace Nexa.Services.Minecraft;

public sealed record MinecraftLibraryConfiguration(int SchemaVersion, string ActiveDirectory, IReadOnlyList<MinecraftLibraryDirectory> Directories);
public sealed record MinecraftLibraryStoredConfiguration(string Revision, MinecraftLibraryConfiguration? Configuration, XsrError? Error = null);
public sealed record MinecraftLibrarySave(string ExpectedRevision, MinecraftLibraryConfiguration Configuration);
public sealed record MinecraftLibraryRememberRenamedCommand(string Root, string Previous, string Current);
public interface IMinecraftLibraryPersistence
{
    ValueTask<MinecraftLibraryStoredConfiguration> ReadAsync(CancellationToken token = default);
    ValueTask<XsrResult<MinecraftLibraryStoredConfiguration>> SaveAsync(MinecraftLibrarySave command, CancellationToken token = default);
}
public interface IMinecraftLibraryPort
{
    Task<XsrResult> RefreshAsync(CancellationToken cancellationToken = default);
    Task<XsrResult> RememberRenamedAsync(string root, string previous, string current, CancellationToken cancellationToken = default);
}
