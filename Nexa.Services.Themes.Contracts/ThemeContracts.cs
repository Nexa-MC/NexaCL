using Nexa.Xsr;

namespace Nexa.Services.Themes;

public enum ThemeSourceKind { BuiltIn, Package, Directory }
public enum ThemeOptionKind { Boolean, Number, Text, Color, Range, Select, File, Files, Directory }
public enum ThemeOptionControl { Automatic, Switch, CheckBox, Slider, Segmented, Input }
public enum ThemeResourceKind { Image, Icon, Font, Audio, Video }
public enum ThemeGrantScope { File, Directory }

public sealed record ThemeOptionValue(ThemeOptionKind Kind, string Value, IReadOnlyList<string>? Values = null);
public sealed record ThemeOptionChoice(string Value, IReadOnlyDictionary<string, string> Label);
public sealed record ThemeOptionDefinition(string Id, ThemeOptionKind Kind, ThemeOptionValue Default,
    IReadOnlyDictionary<string, string> Label, IReadOnlyDictionary<string, string> Description,
    string? Group, string? VisibleWhen, double? Minimum, double? Maximum, double? Step,
    IReadOnlyList<ThemeOptionChoice> Choices, ThemeResourceKind? ResourceKind = null)
{
    public ThemeOptionControl Control { get; init; }
}
public sealed record ThemeCapability(string Name, string? Fallback = null);
public sealed record ThemeAppearanceResource(string Expression, ThemeResourceKind Kind, bool Required = false, string? Fallback = null);
public sealed record ThemeAppearance(IReadOnlyDictionary<string, ThemeAppearanceResource> Resources,
    string? AccentColor = null, string? WindowMaterial = null, string? SplashMotion = null,
    IReadOnlyDictionary<string, string>? Properties = null);
public sealed record ThemeManifest(string Format, string Id, string Version, IReadOnlyDictionary<string, string> Name,
    int StyleApi, string Nss, IReadOnlyList<string> Styles, IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<ThemeCapability> OptionalCapabilities, IReadOnlyList<ThemeOptionDefinition> Options,
    ThemeAppearance Appearance, int OptionsVersion = 1);
public sealed record ThemeStyleSource(string Path, string Text);
public sealed record ThemeCatalogueEntry(string SourceIdentity, string SourceRevision, ThemeSourceKind SourceKind,
    ThemeManifest Manifest, string? DevelopmentDirectory, bool IsInstalled, string? InstalledDirectory = null);
public sealed record ThemeCatalogueSnapshot(long Revision, IReadOnlyList<ThemeCatalogueEntry> Themes);
public sealed record ThemeCandidate(Guid CandidateId, long Generation, string SourceIdentity, string SourceRevision,
    ThemeSourceKind SourceKind, ThemeManifest Manifest, IReadOnlyList<ThemeStyleSource> Styles,
    IReadOnlyDictionary<string, ThemeOptionValue> Options, IReadOnlyList<string> UnsupportedOptionalCapabilities)
{
    public string? PreviousVersion { get; init; }
    public bool IsDowngrade { get; init; }
}
public sealed record ThemeSelection(string SourceIdentity, string SourceRevision, string ThemeId, string Version, long Generation);
public sealed record ThemeResourceGrant(Guid GrantId, string SourceIdentity, ThemeResourceKind Kind,
    ThemeGrantScope Scope, string CanonicalPath, long Revision);
public sealed record ThemeOptionState(string SourceIdentity, int SchemaVersion, long Revision,
    IReadOnlyDictionary<string, ThemeOptionValue> Values, IReadOnlyDictionary<string, string> RetainedLegacyValues);
public sealed record ThemeCurrentSnapshot(long Revision, ThemeSelection? Selection, ThemeSelection? LastKnownGood,
    IReadOnlyList<ThemeOptionState> Options, IReadOnlyList<ThemeResourceGrant> Grants, bool RecoveredIncompleteTransaction);
public sealed record ThemeResourcePayload(Guid LeaseId, string SourceIdentity, long Generation,
    ThemeResourceKind Kind, string Name, string ContentType, byte[] Bytes, string? PreparedPath = null);
public sealed record ThemeResourceListPayload(string SourceIdentity, long Generation, IReadOnlyList<ThemeResourcePayload> Entries);
public sealed record ThemeOptionsExport(string Json);
public sealed record ThemeWatchSnapshot(string SourceIdentity, long Revision, string? Error, Guid? CandidateId = null);

public sealed record ThemeCatalogueQuery;
public sealed record ThemePrepareQuery(string SourcePath, ThemeSourceKind SourceKind, string? SourceIdentity = null,
    IReadOnlyDictionary<string, ThemeOptionValue>? OptionsOverride = null, long? ExpectedRevision = null, string? OptionsDocument = null)
{
    public bool UseCommittedSource { get; init; }
}
public sealed record ThemeCandidateQuery(Guid CandidateId);
public sealed record ThemeCurrentQuery;
public sealed record ThemeCommitCommand(Guid CandidateId, long ExpectedRevision, bool Preview = false)
{
    public bool AllowDowngrade { get; init; }
}
public sealed record ThemeRevertCommand(Guid CandidateId);
public sealed record ThemeOptionsSaveCommand(string SourceIdentity, IReadOnlyDictionary<string, ThemeOptionValue> Values, long ExpectedRevision);
public sealed record ThemeResourceGrantCommand(string SourceIdentity, ThemeResourceKind Kind, ThemeGrantScope Scope, string SelectedPath);
public sealed record ThemeResourceRevokeCommand(Guid GrantId);
public sealed record ThemeRemoveCommand(string SourceIdentity);
public sealed record ThemeResourceQuery(Guid CandidateId, string Expression, ThemeResourceKind Kind);
public sealed record ThemeResourceListQuery(Guid CandidateId, string Expression, ThemeResourceKind Kind);
public sealed record ThemeResourceReleaseCommand(Guid LeaseId);
public sealed record ThemeWatchCommand(string SourceIdentity, bool Enabled);
public sealed record ThemeWatchQuery(string SourceIdentity);
public sealed record ThemeOptionsExportQuery(string SourceIdentity);
public sealed record ThemeOptionsImportCommand(string SourceIdentity, string Json, long ExpectedRevision);
public sealed record ThemeOptionsResetCommand(string SourceIdentity, long ExpectedRevision);
public sealed record ThemeLegacyMigrationCommand(IReadOnlyDictionary<string, string> Values);

public interface IThemePackageService : IDisposable
{
    event Action<ThemeWatchSnapshot>? WatchChanged;
    ValueTask<XsrResult<ThemeCatalogueSnapshot>> CatalogueAsync(ThemeCatalogueQuery query, CancellationToken token = default);
    ValueTask<XsrResult<ThemeCandidate>> PrepareAsync(ThemePrepareQuery query, CancellationToken token = default);
    ValueTask<XsrResult<ThemeCandidate>> CandidateAsync(ThemeCandidateQuery query, CancellationToken token = default);
    ValueTask<XsrResult<ThemeCurrentSnapshot>> CurrentAsync(ThemeCurrentQuery query, CancellationToken token = default);
    ValueTask<XsrResult> CommitAsync(ThemeCommitCommand command, CancellationToken token = default);
    ValueTask<XsrResult> RevertAsync(ThemeRevertCommand command, CancellationToken token = default);
    ValueTask<XsrResult> SaveOptionsAsync(ThemeOptionsSaveCommand command, CancellationToken token = default);
    ValueTask<XsrResult> GrantAsync(ThemeResourceGrantCommand command, CancellationToken token = default);
    ValueTask<XsrResult> RevokeAsync(ThemeResourceRevokeCommand command, CancellationToken token = default);
    ValueTask<XsrResult> RemoveAsync(ThemeRemoveCommand command, CancellationToken token = default);
    ValueTask<XsrResult<ThemeResourcePayload>> ReadResourceAsync(ThemeResourceQuery query, CancellationToken token = default);
    ValueTask<XsrResult<ThemeResourceListPayload>> ListResourcesAsync(ThemeResourceListQuery query, CancellationToken token = default);
    ValueTask<XsrResult> ReleaseResourceAsync(ThemeResourceReleaseCommand command, CancellationToken token = default);
    ValueTask<XsrResult> WatchAsync(ThemeWatchCommand command, CancellationToken token = default);
    ValueTask<XsrResult<ThemeWatchSnapshot>> WatchStatusAsync(ThemeWatchQuery query, CancellationToken token = default);
    ValueTask<XsrResult<ThemeOptionsExport>> ExportOptionsAsync(ThemeOptionsExportQuery query, CancellationToken token = default);
    ValueTask<XsrResult> ImportOptionsAsync(ThemeOptionsImportCommand command, CancellationToken token = default);
    ValueTask<XsrResult> ResetOptionsAsync(ThemeOptionsResetCommand command, CancellationToken token = default);
    ValueTask<XsrResult> MigrateAsync(ThemeLegacyMigrationCommand command, CancellationToken token = default);
}

public static class ThemesContract
{
    public static readonly XsrSemanticId Catalogue = XsrSemanticId.Parse("themes.catalogue");
    public static readonly XsrSemanticId Prepare = XsrSemanticId.Parse("themes.prepare");
    public static readonly XsrSemanticId Candidate = XsrSemanticId.Parse("themes.candidate");
    public static readonly XsrSemanticId Current = XsrSemanticId.Parse("themes.current");
    public static readonly XsrSemanticId Commit = XsrSemanticId.Parse("themes.commit");
    public static readonly XsrSemanticId Revert = XsrSemanticId.Parse("themes.revert");
    public static readonly XsrSemanticId SaveOptions = XsrSemanticId.Parse("themes.options.save");
    public static readonly XsrSemanticId Grant = XsrSemanticId.Parse("themes.resource.grant");
    public static readonly XsrSemanticId Revoke = XsrSemanticId.Parse("themes.resource.revoke");
    public static readonly XsrSemanticId Remove = XsrSemanticId.Parse("themes.remove");
    public static readonly XsrSemanticId ReadResource = XsrSemanticId.Parse("themes.resource.read");
    public static readonly XsrSemanticId ListResources = XsrSemanticId.Parse("themes.resource.list");
    public static readonly XsrSemanticId ReleaseResource = XsrSemanticId.Parse("themes.resource.release");
    public static readonly XsrSemanticId Watch = XsrSemanticId.Parse("themes.watch");
    public static readonly XsrSemanticId WatchStatus = XsrSemanticId.Parse("themes.watch.status");
    public static readonly XsrSemanticId ExportOptions = XsrSemanticId.Parse("themes.options.export");
    public static readonly XsrSemanticId ImportOptions = XsrSemanticId.Parse("themes.options.import");
    public static readonly XsrSemanticId ResetOptions = XsrSemanticId.Parse("themes.options.reset");
    public static readonly XsrSemanticId Migrate = XsrSemanticId.Parse("themes.legacy.migrate");
}
