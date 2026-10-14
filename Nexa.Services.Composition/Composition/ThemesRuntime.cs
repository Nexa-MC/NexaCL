using Nexa.Services.Themes;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

public static class ThemesRuntime
{
    public static ThemePackageService Register(XsrQueryRouterBuilder queries, XsrCommandRouterBuilder commands,
        string dataRoot, string? builtinThemeRoot, IEnumerable<string> hostCapabilities)
    {
        var service = new ThemePackageService(dataRoot, builtinThemeRoot, hostCapabilities); Register(queries, commands, service); return service;
    }
    public static void Register(XsrQueryRouterBuilder queries, XsrCommandRouterBuilder commands, IThemePackageService service)
    {
        ArgumentNullException.ThrowIfNull(queries); ArgumentNullException.ThrowIfNull(commands); ArgumentNullException.ThrowIfNull(service);
        queries.Register<ThemeCatalogueQuery, ThemeCatalogueSnapshot>(ThemesContract.Catalogue, service.CatalogueAsync);
        queries.Register<ThemePrepareQuery, ThemeCandidate>(ThemesContract.Prepare, service.PrepareAsync);
        queries.Register<ThemeCandidateQuery, ThemeCandidate>(ThemesContract.Candidate, service.CandidateAsync);
        queries.Register<ThemeCurrentQuery, ThemeCurrentSnapshot>(ThemesContract.Current, service.CurrentAsync);
        queries.Register<ThemeResourceQuery, ThemeResourcePayload>(ThemesContract.ReadResource, service.ReadResourceAsync);
        queries.Register<ThemeResourceListQuery, ThemeResourceListPayload>(ThemesContract.ListResources, service.ListResourcesAsync);
        queries.Register<ThemeWatchQuery, ThemeWatchSnapshot>(ThemesContract.WatchStatus, service.WatchStatusAsync);
        queries.Register<ThemeOptionsExportQuery, ThemeOptionsExport>(ThemesContract.ExportOptions, service.ExportOptionsAsync);
        commands.Register<ThemeCommitCommand>(ThemesContract.Commit, service.CommitAsync);
        commands.Register<ThemeRevertCommand>(ThemesContract.Revert, service.RevertAsync);
        commands.Register<ThemeOptionsSaveCommand>(ThemesContract.SaveOptions, service.SaveOptionsAsync);
        commands.Register<ThemeResourceGrantCommand>(ThemesContract.Grant, service.GrantAsync);
        commands.Register<ThemeResourceRevokeCommand>(ThemesContract.Revoke, service.RevokeAsync);
        commands.Register<ThemeRemoveCommand>(ThemesContract.Remove, service.RemoveAsync);
        commands.Register<ThemeResourceReleaseCommand>(ThemesContract.ReleaseResource, service.ReleaseResourceAsync);
        commands.Register<ThemeWatchCommand>(ThemesContract.Watch, service.WatchAsync);
        commands.Register<ThemeOptionsImportCommand>(ThemesContract.ImportOptions, service.ImportOptionsAsync);
        commands.Register<ThemeOptionsResetCommand>(ThemesContract.ResetOptions, service.ResetOptionsAsync);
        commands.Register<ThemeLegacyMigrationCommand>(ThemesContract.Migrate, service.MigrateAsync);
    }
}
