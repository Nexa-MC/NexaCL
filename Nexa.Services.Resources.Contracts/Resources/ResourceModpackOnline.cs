using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;

namespace Nexa.Services.Resources;

public static class ResourceModpackContract
{
    public static readonly XsrSemanticId Online = XsrSemanticId.Parse("resources.modpack.online");
    public static readonly XsrSemanticId Preview = XsrSemanticId.Parse("resources.modpack.preview");
    public static readonly XsrSemanticId Update = XsrSemanticId.Parse("resources.modpack.update");
}
public sealed record ResourceModpackOnlineQuery(string InstanceDirectory, string? OriginalArchivePath = null,
    bool MirrorFirst = true, bool Refresh = false);
public sealed record ResourceModpackOnline(ResourceModpackOnlineQuery Request, string? InstalledArchiveSha256,
    ResourceProject? Project, ResourceVersion? InstalledVersion, IReadOnlyList<ResourceVersion> Versions, string? Notice)
{
    public bool IsStale { get; init; }
    public ResourceVersion? UpdateVersion { get; init; }
    public string? OriginalArchivePath { get; init; }
}
public sealed record ResourceModpackPreviewQuery(ResourceModpackOnlineQuery Instance, ResourceReference Source,
    string VersionId, string InstalledArchiveSha256, bool IncludeOptional = false, bool ReplaceModifiedMods = false);
public sealed record ResourceModpackPreview(ResourceModpackPreviewQuery Request, ResourceVersion Version,
    MinecraftModpackUpdatePreview Installation);
public sealed record ResourceModpackUpdateCommand(ResourceModpackPreview Preview);
