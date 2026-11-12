using System.Collections.Immutable;
using System.Text;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Themes;
using Nexa.Xsr;

namespace Nexa.Services.Composition.Plugins;

public sealed record PluginThemeEffects(
    Func<ThemeOptionsSaveCommand, CancellationToken, Task<XsrResult>> Save,
    Func<ThemeOptionsResetCommand, CancellationToken, Task<XsrResult>> Reset,
    Func<ThemeOptionsImportCommand, CancellationToken, Task<XsrResult>> Import,
    Func<string, string, long, bool, CancellationToken, Task<XsrResult<long>>> Activate,
    Func<long, CancellationToken, Task<XsrResult>> Revert);

public sealed partial class PluginBusinessRoutes
{
    private PluginThemeEffects? _themeEffects;
    public void BindThemes(PluginThemeEffects effects) => _themeEffects = effects;
    private PluginThemeEffects ThemeEffects => _themeEffects ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
    private ValueTask<ThemeCatalogueSnapshot> ThemeCatalogue(CancellationToken token) => Query<ThemeCatalogueQuery, ThemeCatalogueSnapshot>(foundationQueries, ThemesContract.Catalogue, new(), token);
    private ValueTask<ThemeCurrentSnapshot> ThemeCurrent(CancellationToken token) => Query<ThemeCurrentQuery, ThemeCurrentSnapshot>(foundationQueries, ThemesContract.Current, new(), token);
    private EntityId ThemeId(HostInvocation c, ThemeCatalogueEntry theme) => new(Token(c, "theme:" + theme.SourceIdentity));
    private async ValueTask<ThemeCatalogueEntry> FindTheme(HostInvocation c, EntityId id)
    {
        var catalogue = await ThemeCatalogue(c.Options.Cancellation).ConfigureAwait(false);
        return catalogue.Themes.FirstOrDefault(t => ThemeId(c, t) == id) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
    }
    private ThemeSummary ProjectTheme(HostInvocation c, ThemeCatalogueEntry theme, ThemeCurrentSnapshot current) => new(ThemeId(c, theme),
        theme.Manifest.Name.GetValueOrDefault("en-US") ?? theme.Manifest.Name.Values.FirstOrDefault() ?? theme.Manifest.Id, theme.Manifest.Version,
        current.Selection?.SourceIdentity == theme.SourceIdentity);
    private ThemeOptions ProjectOptions(HostInvocation c, ThemeCatalogueEntry theme, ThemeCurrentSnapshot current)
    {
        var options = current.Options.FirstOrDefault(o => o.SourceIdentity == theme.SourceIdentity);
        var values = options?.Values ?? theme.Manifest.Options.ToDictionary(o => o.Id, o => o.Default);
        return new(ThemeId(c, theme), values.Select(p => new NamedValue(p.Key, DataCodec.Map(("kind", new AbiValue.Utf8String(p.Value.Kind.ToString())),
            ("value", new AbiValue.Utf8String(p.Value.Value)), ("values", new AbiValue.Array((p.Value.Values ?? []).Select(v => (AbiValue)new AbiValue.Utf8String(v)).ToImmutableArray()))))).ToImmutableArray(),
            new(Token(c, "theme-options:" + theme.SourceIdentity + ":" + current.Revision)));
    }
    private static Dictionary<string, ThemeOptionValue> ParseOptions(ThemeOptions options)
    {
        if (options.Values.Length > 256 || options.Values.Select(v => v.Name).Distinct(StringComparer.Ordinal).Count() != options.Values.Length) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        return options.Values.ToDictionary(p => p.Name, p =>
        {
            var map = DataCodec.ReadMap(p.Value);
            if (!Enum.TryParse<ThemeOptionKind>(DataCodec.ReadString(DataCodec.Field(map, "kind")), out var kind) || !Enum.IsDefined(kind)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            string value = DataCodec.ReadString(DataCodec.Field(map, "value"));
            var values = DataCodec.ReadArray(DataCodec.Field(map, "values"), (v, _) => DataCodec.ReadString(v), 0);
            if (kind != ThemeOptionKind.Files && values.Length != 0) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            return new ThemeOptionValue(kind, value, kind == ThemeOptionKind.Files ? values : null);
        }, StringComparer.Ordinal);
    }
    private async ValueTask<ThemeOptions> SaveThemeOptions(HostInvocation c, ThemeOptionChange change)
    {
        var theme = await FindTheme(c, change.Options.Theme).ConfigureAwait(false); var current = await ThemeCurrent(c.Options.Cancellation).ConfigureAwait(false);
        if (change.ExpectedRevision != ProjectOptions(c, theme, current).Revision || change.Options.Revision != change.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
        var values = ParseOptions(change.Options);
        await Confirm(c, "themes/options", "Change theme options for " + theme.Manifest.Id, ThemeOptionChange.Write(change), c.Options.Cancellation).ConfigureAwait(false);
        Require(await ThemeEffects.Save(new(theme.SourceIdentity, values, current.Revision), c.Options.Cancellation).ConfigureAwait(false));
        return ProjectOptions(c, theme, await ThemeCurrent(c.Options.Cancellation).ConfigureAwait(false));
    }
    private IEnumerable<HostCapabilityRoute> ThemeRoutes => ThemeActivationRoutes.Concat(ThemeReadRoutes).Concat(ThemeResourceRoutes);
    private IEnumerable<HostCapabilityRoute> ThemeReadRoutes => [
        HostRoutes.Typed<PageRequest, Page<ThemeSummary>>("Themes", "List", async (c, v) =>
        {
            var catalogue = await ThemeCatalogue(c.Options.Cancellation).ConfigureAwait(false); var current = await ThemeCurrent(c.Options.Cancellation).ConfigureAwait(false);
            return Page(c, v, catalogue.Themes.Select(t => ProjectTheme(c, t, current)).ToImmutableArray(), "themes:" + current.Revision, catalogue.Revision);
        }),
        HostRoutes.Typed<EntityRequest, ThemeSummary>("Themes", "Read", async (c, v) => ProjectTheme(c, await FindTheme(c, v.Id).ConfigureAwait(false), await ThemeCurrent(c.Options.Cancellation).ConfigureAwait(false))),
        HostRoutes.Typed<EntityRequest, ThemeOptions>("Themes", "ReadOptions", async (c, v) => ProjectOptions(c, await FindTheme(c, v.Id).ConfigureAwait(false), await ThemeCurrent(c.Options.Cancellation).ConfigureAwait(false))),
        HostRoutes.Typed<ThemeOptionChange, ThemeOptions>("Themes", "SaveOptions", SaveThemeOptions),
        HostRoutes.Typed<EntityRequest, ThemeOptions>("Themes", "ResetOptions", async (c, v) =>
        {
            var theme = await FindTheme(c, v.Id).ConfigureAwait(false); var current = await ThemeCurrent(c.Options.Cancellation).ConfigureAwait(false);
            await Confirm(c, "themes/reset-options", "Reset theme options for " + theme.Manifest.Id, EntityRequest.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            Require(await ThemeEffects.Reset(new(theme.SourceIdentity, current.Revision), c.Options.Cancellation).ConfigureAwait(false));
            return ProjectOptions(c, theme, await ThemeCurrent(c.Options.Cancellation).ConfigureAwait(false));
        }),
        HostRoutes.Typed<ThemeTransfer, OperationRef>("Themes", "ImportOptions", async (c, v) =>
        {
            var theme = await FindTheme(c, v.Theme).ConfigureAwait(false); var current = await ThemeCurrent(c.Options.Cancellation).ConfigureAwait(false);
            var stat = await Application.Files.StatAsync(c.Identity, v.File, c.Options.Cancellation).ConfigureAwait(false);
            if (stat.Size > 4 * 1024 * 1024 || stat.IsDirectory) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            string json = new UTF8Encoding(false, true).GetString(await Application.Files.ReadAsync(c.Identity, new(v.File, 0, stat.Size), c.Options.Cancellation).ConfigureAwait(false));
            return Services.StartHostOperation(c, "nexa.themes.write", Abi100Schemas.ThemeOptions, async token =>
            {
                await Confirm(c, "themes/import-options", "Import options for " + theme.Manifest.Id, ThemeTransfer.Write(v), token).ConfigureAwait(false);
                Require(await ThemeEffects.Import(new(theme.SourceIdentity, json, current.Revision), token).ConfigureAwait(false));
                return AbiReply<AbiValue>.Ok(ThemeOptions.Write(ProjectOptions(c, theme, await ThemeCurrent(token).ConfigureAwait(false))));
            });
        }),
        HostRoutes.Typed<ThemeTransfer, OperationRef>("Themes", "ExportOptions", async (c, v) =>
        {
            var theme = await FindTheme(c, v.Theme).ConfigureAwait(false);
            var destination = await Application.Files.StatAsync(c.Identity, v.File, c.Options.Cancellation).ConfigureAwait(false);
            Application.Files.Resolve(c.Identity, v.File, true);
            var document = await Query<ThemeOptionsExportQuery, ThemeOptionsExport>(foundationQueries, ThemesContract.ExportOptions, new(theme.SourceIdentity), c.Options.Cancellation).ConfigureAwait(false);
            byte[] bytes = Encoding.UTF8.GetBytes(document.Json);
            return Services.StartHostOperation(c, "nexa.themes.write", Abi100Schemas.FileStat, async token =>
            {
                await Confirm(c, "themes/export-options", "Export options for " + theme.Manifest.Id, ThemeTransfer.Write(v), token).ConfigureAwait(false);
                var blob = Application.AddBlob(c.Identity, bytes);
                var result = await Application.Files.WriteAsync(c.Identity, new(v.File, blob, destination.Revision), bytes, token).ConfigureAwait(false);
                return AbiReply<AbiValue>.Ok(FileStat.Write(result));
            });
        })
    ];
}
