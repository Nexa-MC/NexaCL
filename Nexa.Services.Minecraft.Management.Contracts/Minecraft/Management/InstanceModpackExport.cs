using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Management;

public sealed record InstanceExportCategory(string Id, string Label, int FileCount, long Bytes);
public sealed record InstanceExportFile(string Path, string Category, long Bytes);
public sealed record InstanceModpackExportPreview(string InstanceDirectory, IReadOnlyList<InstanceExportCategory> Categories)
{
    public IReadOnlyList<InstanceExportFile> Files { get; init; } = [];
}
public sealed record InstanceModpackExportQuery(string InstanceDirectory);
public sealed record InstanceExportOptions(string Name, string Version, string Summary, IReadOnlyList<string> Categories, IReadOnlyList<string> ExcludedPaths);
public sealed record InstanceExportOptionsQuery(string Path);
public sealed record InstanceExportOptionsSave(string Path, InstanceExportOptions Options);
public sealed record InstanceModpackExportCommand(string InstanceDirectory, string DestinationPath, string Name, string Version,
    IReadOnlyList<string> Categories)
{
    public IReadOnlyList<string> ExcludedPaths { get; init; } = [];
    public string Summary { get; init; } = "";
}
public static class InstanceModpackExportContract
{
    public static readonly XsrSemanticId Preview = XsrSemanticId.Parse("minecraft.instance.modpack.export-preview");
    public static readonly XsrSemanticId Export = XsrSemanticId.Parse("minecraft.instance.modpack.export");
    public static readonly XsrSemanticId LoadOptions = XsrSemanticId.Parse("minecraft.instance.modpack.export-options.load");
    public static readonly XsrSemanticId SaveOptions = XsrSemanticId.Parse("minecraft.instance.modpack.export-options.save");
}
