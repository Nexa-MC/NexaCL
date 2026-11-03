



using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Install;

public sealed record MinecraftModpackPreview(string Path, string Sha256, string Name, string Version,
    string Format, string Game, InstallLoader? Loader, string? Build, string InstanceId, int RequiredFiles, int OptionalFiles)
{
    public IReadOnlyList<MinecraftModpackOptionalFile> OptionalEntries { get; init; } = [];
}
public sealed record MinecraftModpackOptionalFile(string Id, string Name);
public sealed record MinecraftModpackCommand(MinecraftModpackPreview Pack, string RootDirectory, bool IncludeOptional = false)
{
    public IReadOnlyList<string> ExcludedOptionalFiles { get; init; } = [];
}
public static class MinecraftModpackContract
{
    public static readonly XsrSemanticId Install = XsrSemanticId.Parse("minecraft.modpack.install");
}
