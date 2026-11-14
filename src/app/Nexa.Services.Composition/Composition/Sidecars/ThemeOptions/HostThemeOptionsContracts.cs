using Nexa.Services.Themes;
using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostThemeOptionsImport(string SourceIdentity, long ExpectedRevision, string ThemeId,
    int SchemaVersion, IReadOnlyDictionary<string, ThemeOptionValue> Values);
public sealed record SidecarThemeOptionsActions(
    Func<ThemeOptionsSaveCommand, CancellationToken, Task<XsrResult>> Save,
    Func<ThemeOptionsResetCommand, CancellationToken, Task<XsrResult>> Reset,
    Func<HostThemeOptionsImport, CancellationToken, Task<XsrResult>> Import);
public static class SidecarThemeOptionsRoutes
{
    public static readonly XsrSemanticId Save = XsrSemanticId.Parse("themes.options.save");
    public static readonly XsrSemanticId Reset = XsrSemanticId.Parse("themes.options.reset");
    public static readonly XsrSemanticId Import = XsrSemanticId.Parse("themes.options.import");
}
