using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public enum HostFilePickKind { OpenFile = 0, Directory = 1, SaveFile = 2 }
public sealed record HostFilePick(HostFilePickKind Kind, string Title, string[] Extensions);
public sealed record HostFileSelection(byte[]? Utf8Path);
public static class SidecarFilePickerRoutes
{
    public static readonly XsrSemanticId Pick = XsrSemanticId.Parse("interaction.filepicker.pick");
}
