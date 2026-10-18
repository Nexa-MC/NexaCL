using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Install;

public sealed record MinecraftModpackUpdateQuery(string InstanceDirectory, string ArchivePath,
    string? OriginalArchivePath = null, bool IncludeOptional = false, bool ReplaceModifiedMods = false);
public sealed record MinecraftModpackUpdatePreview(MinecraftModpackUpdateQuery Request, MinecraftModpackPreview Pack,
    string Revision, IReadOnlyList<string> Added, IReadOnlyList<string> Updated, IReadOnlyList<string> Removed,
    IReadOnlyList<string> Preserved, IReadOnlyList<string> Conflicts);
public sealed record MinecraftModpackUpdateCommand(MinecraftModpackUpdatePreview Preview);

public static class MinecraftModpackUpdateContract
{
    public static readonly XsrSemanticId Preview = XsrSemanticId.Parse("minecraft.modpack.update.preview");
    public static readonly XsrSemanticId Apply = XsrSemanticId.Parse("minecraft.modpack.update.apply");
}
