using Nexa.Xsr;
namespace Nexa.Services.Minecraft.Management;

public sealed record ContentInspectionQuery(string Path, string ArchiveEntry = "", string Node = "", int Offset = 0);
public sealed record ContentInspectionItem(string Id, string Name, string Type, string Value, bool Expandable);
public sealed record ContentInspectionSnapshot(string Format, string Revision, string Summary, string Text,
    byte[] Image, IReadOnlyList<ContentInspectionItem> Items, int Total);
public static class ContentInspectionContract
{
    public static readonly XsrSemanticId Read = XsrSemanticId.Parse("minecraft.content.inspect");
}
