using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private XsrCommandRouter? _installCatalogCommands, _installCommands;
    private XsrQueryRouter? _installQueries, _folderQueries, _installRecoveryQueries;
    public void BindInstall(XsrCommandRouter catalogCommands, XsrQueryRouter queries, XsrCommandRouter commands, XsrQueryRouter? folderQueries = null, XsrQueryRouter? recoveryQueries = null)
    { _installCatalogCommands = catalogCommands; _installQueries = queries; _installCommands = commands; _folderQueries = folderQueries; _installRecoveryQueries = recoveryQueries; }
    private sealed record PluginInstallDraft(PluginIdentity Owner, InstallDraft Public, MinecraftInstallEditSnapshot Original);
    private readonly Dictionary<EntityId, PluginInstallDraft> _installDrafts = [];
    private readonly Dictionary<EntityId, (PluginIdentity Owner, InstallPlan Plan)> _installPlans = [];
    private readonly Dictionary<EntityId, PluginIdentity> _installOperations = [];
    private static InstallLoader Loader(QualifiedId id) => Enum.GetValues<InstallLoader>().FirstOrDefault(v => "nexa:loader/" + v.ToString().ToLowerInvariant() == id.Value,
        (InstallLoader)(-1)) is var result && Enum.IsDefined(result) ? result : throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
    private static QualifiedId LoaderId(InstallLoader loader) => new("nexa:loader/" + loader.ToString().ToLowerInvariant());
    private async ValueTask<InstallCatalogSnapshot> ReadInstallCatalog(string game, InstallLoader? loader, CancellationToken token, bool refresh = false)
    {
        if (game.Length > 128 || game.Any(char.IsControl)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        await Command(_installCatalogCommands, InstallCatalogRoutes.Read, new InstallCatalogReadCommand(game, loader, refresh), token).ConfigureAwait(false);
        var state = store.Read<InstallCatalogState>(store.Resolve(InstallCatalogStateContract.StateKey), token);
        var catalog = state.IsAvailable ? state.Value?.Catalogs.FirstOrDefault(p => p.GameVersion == game && p.Loader == loader) : null;
        if (catalog is null || catalog.Loading || catalog.IsStale || catalog.Error is not null || catalog.Unsupported is not null) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        return catalog;
    }
    private PluginInstallDraft FindDraft(HostInvocation c, EntityId id)
    {
        lock (_planGate) return _installDrafts.TryGetValue(id, out var draft) && draft.Owner == c.Identity ? draft : throw new AbiHostException(AbiErrors.HANDLE_INVALID);
    }
    private async ValueTask<InstallDraft> ReadInstallDraft(HostInvocation c, EntityId id)
    {
        lock (_planGate) if (_installDrafts.TryGetValue(id, out var known) && known.Owner == c.Identity) return known.Public;
        var instance = FindInstance(c, id);
        var original = await Query<MinecraftInstallEditQuery, MinecraftInstallEditSnapshot>(_installQueries, MinecraftInstallEditContract.Query,
            new(instance.Snapshot.RootDirectory, instance.Instance.Id), c.Options.Cancellation).ConfigureAwait(false);
        var draft = new InstallDraft(new(Guid.NewGuid().ToString("N")), id, original.GameVersion,
            original.Selection.Select(p => new LoaderVersion(LoaderId(p.Loader), p.Version, original.GameVersion, false)).ToImmutableArray(), [], new(Guid.NewGuid().ToString("N")));
        lock (_planGate)
        {
            if (_installDrafts.Count >= 128) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            _installDrafts.Add(draft.Id, new(c.Identity, draft, original));
        }
        return draft;
    }
    private async ValueTask<(ValidationReport Report, InstallEligibilityResult Eligibility)> ValidateInstall(HostInvocation c, InstallDraft draft, CancellationToken token)
    {
        var owned = FindDraft(c, draft.Id);
        if (ProjectionRevision(owned.Public) != ProjectionRevision(draft)) throw new AbiHostException(AbiErrors.CONFLICT);
        FindInstance(c, draft.Instance);
        var selection = draft.Loaders.Select(p => new InstallBuildSelection(Loader(p.Loader), p.Version)).ToArray();
        var eligible = await Query<InstallEligibilityQuery, InstallEligibilityResult>(_installQueries, InstallEligibilityContract.Query,
            new(draft.GameVersion, selection, InstalledSelection: owned.Original.Selection), token).ConfigureAwait(false);
        var issues = ImmutableArray.CreateBuilder<ValidationIssue>();
        if (draft.OptionalFiles.Length != 0) issues.Add(Issue("OPTIONAL_FILES_UNAVAILABLE", "Optional resource installation requires a content transaction.", draft.Id.Value));
        if (draft.Loaders.Any(p => p.GameVersion != draft.GameVersion)) issues.Add(Issue("GAME_VERSION_MISMATCH", "A loader belongs to a different game version.", draft.Id.Value));
        if (eligible.Rejection is { } rejection) issues.Add(Issue("INSTALL_REJECTED", rejection, draft.Id.Value));
        if (eligible.CommitError is { } error) issues.Add(Issue("INSTALL_CONFLICT", error, draft.Id.Value));
        return (new(issues.Count == 0, issues.ToImmutable()), eligible);
    }
    private async ValueTask<InstallPlan> ResolveInstall(HostInvocation c, InstallDraft draft)
    {
        var owned = FindDraft(c, draft.Id);
        var validated = await ValidateInstall(c, draft, c.Options.Cancellation).ConfigureAwait(false);
        if (!validated.Report.Valid) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var eligibility = validated.Eligibility;
        var primary = eligibility.PrimaryLoader;
        string? build = eligibility.Selection.FirstOrDefault(p => p.Loader == primary)?.Version;
        var addons = eligibility.Selection.Where(p => InstallLoaderFacts.IsAddon(p.Loader)).Select(p => new MinecraftInstallAddon(p.Loader, p.Version)).ToArray();
        var command = new MinecraftInstallCommand(owned.Original.RootDirectory, draft.GameVersion, primary, build, addons, owned.Original.InstanceId, owned.Original.Fingerprint);
        var plan = Plan(c, "install/execute", InstallDraft.Write(draft), "Update instance " + owned.Original.InstanceId, async token =>
        {
            if (FindDraft(c, draft.Id).Public.Revision != draft.Revision) throw new AbiHostException(AbiErrors.CONFLICT);
            var current = await Query<MinecraftInstallEditQuery, MinecraftInstallEditSnapshot>(_installQueries, MinecraftInstallEditContract.Query, new(owned.Original.RootDirectory, owned.Original.InstanceId), token).ConfigureAwait(false);
            if (current.Fingerprint != owned.Original.Fingerprint) throw new AbiHostException(AbiErrors.CONFLICT);
            if (!(await ValidateInstall(c, draft, token).ConfigureAwait(false)).Report.Valid) throw new AbiHostException(AbiErrors.CONFLICT);
        }, async token =>
        {
            await Command(_installCommands, MinecraftInstallRoutes.Run, command, token).ConfigureAwait(false);
            await Command(libraryCommands, MinecraftLibraryRoutes.Refresh, new MinecraftLibraryRefreshCommand(), token).ConfigureAwait(false);
            return Empty.Write(new());
        });
        var result = new InstallPlan(plan, draft.Instance, [new(new("nexa:install/instance"), InstallDraft.Write(draft), [new("Apply the selected game and loader versions through the installation service.", [])], false)]);
        lock (_planGate)
        {
            if (_installPlans.Count >= 128) { _plans.Remove(plan.Id); throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE); }
            _installPlans.Add(plan.Id, (c.Identity, result));
        }
        return result;
    }
    private static readonly QualifiedId LoaderProviderKind = new("nexa:install/loader-catalog");
    private static readonly BusinessProviderMethod LoaderProviderMethod = new("ListVersions", Abi100Schemas.LoaderVersionRequest, Abi100Schemas.PageOfLoaderVersion);
    private IEnumerable<HostCapabilityRoute> InstallRoutes => [
        HostRoutes.Typed<ProviderDescriptor, RegistrationRef>("Minecraft.Install", "RegisterLoaderProvider", (c, v) => ValueTask.FromResult(Services.RegisterHostProvider(c, LoaderProviderKind, v, [LoaderProviderMethod]))),
        HostRoutes.Typed<PageRequest, Page<GameVersion>>("Minecraft.Install", "ListGameVersions", async (c, v) =>
        {
            var catalog = await ReadInstallCatalog("", null, c.Options.Cancellation).ConfigureAwait(false);
            if (catalog.Versions.Any(p => p.ReleasedAt is null)) catalog = await ReadInstallCatalog("", null, c.Options.Cancellation, true).ConfigureAwait(false);
            if (catalog.Versions.Any(p => p.ReleasedAt is null)) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            return ContentPage(catalog.Versions.Select(p => new GameVersion(p.Id, p.Detail, p.ReleasedAt!.Value)).ToImmutableArray(), v);
        }),
        HostRoutes.Typed<LoaderVersionRequest, Page<LoaderVersion>>("Minecraft.Install", "ListLoaderVersions", async (c, v) =>
        {
            if (!v.Loader.Value.StartsWith("nexa:loader/", StringComparison.Ordinal))
            {
                string scope = "loader:" + v.Loader.Value + ":" + v.GameVersion;
                var input = v with { Page = UnwrapCatalogCursor(c, v.Page, scope) };
                var result = Nexa.Plugin.Sdk.Abi100.Data.Page<LoaderVersion>.Read(await Services.QueryHostProvider(c, LoaderProviderKind, v.Loader, LoaderProviderMethod, LoaderVersionRequest.Write(input)).ConfigureAwait(false));
                foreach (var item in result.Items)
                {
                    _ = Loader(item.Loader);
                    if (item.GameVersion != v.GameVersion || item.Version.Length is < 1 or > 256 || item.Version.Any(char.IsControl)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                }
                return WrapCatalogPage(c, result, v.Page, scope);
            }
            var catalog = await ReadInstallCatalog(v.GameVersion, Loader(v.Loader), c.Options.Cancellation).ConfigureAwait(false);
            return ContentPage(catalog.Versions.Select(p => new LoaderVersion(v.Loader, p.Id, v.GameVersion, p.Stable)).ToImmutableArray(), v.Page);
        }),
        HostRoutes.Typed<FileRef, InstallerInspection>("Minecraft.Install", "InspectLocalInstaller", async (c, v) =>
        {
            string path = Application.Files.Resolve(c.Identity, v);
            var inspection = await Query<MinecraftFolderInspectQuery, MinecraftFolderInspection>(_folderQueries, MinecraftFolderImportContract.Inspect, new(path), c.Options.Cancellation).ConfigureAwait(false);
            return new(v, inspection.Jar?.Loader is { } loader ? LoaderId(loader) : null, inspection.Jar?.Build,
                inspection.Jar is null ? [Issue("NOT_A_LOADER_INSTALLER", "The file is not a recognized local loader installer.", v.Token.ToString("N"))] : []);
        }),
        HostRoutes.Typed<EntityRequest, InstallDraft>("Minecraft.Install", "ReadDraft", (c, v) => ReadInstallDraft(c, v.Id)),
        HostRoutes.Typed<InstallDraftUpdate, InstallDraft>("Minecraft.Install", "UpdateDraft", (c, v) =>
        {
            lock (_planGate)
            {
                var draft = FindDraft(c, v.Draft.Id);
                if (draft.Public.Instance != v.Draft.Instance || draft.Public.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
                if (v.Draft.GameVersion.Length is < 1 or > 128 || v.Draft.Loaders.Length > 16 || v.Draft.OptionalFiles.Length > 256 || v.Draft.Loaders.Select(p => p.Loader).Distinct().Count() != v.Draft.Loaders.Length)
                    throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                foreach (var loader in v.Draft.Loaders) { _ = Loader(loader.Loader); if (loader.Version.Length is < 1 or > 256) throw new AbiHostException(AbiErrors.SCHEMA_INVALID); }
                var updated = v.Draft with { Revision = new(Guid.NewGuid().ToString("N")) };
                _installDrafts[updated.Id] = draft with { Public = updated }; return ValueTask.FromResult(updated);
            }
        }),
        HostRoutes.Typed<InstallDraft, ValidationReport>("Minecraft.Install", "ValidateDraft", async (c, v) => (await ValidateInstall(c, v, c.Options.Cancellation).ConfigureAwait(false)).Report),
        HostRoutes.Typed<InstallDraft, InstallPlan>("Minecraft.Install", "ResolvePlan", ResolveInstall),
        HostRoutes.Typed<EntityRequest, InstallPlan>("Minecraft.Install", "PreviewPlan", (c, v) =>
        {
            lock (_planGate) return ValueTask.FromResult(_installPlans.TryGetValue(v.Id, out var plan) && plan.Owner == c.Identity ? plan.Plan : throw new AbiHostException(AbiErrors.HANDLE_INVALID));
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Install", "ExecutePlan", (c, v) =>
        {
            var operation = ExecutePlan(c, v, "install/execute", "nexa.minecraft.install.write", Abi100Schemas.Empty);
            lock (_planGate) _installOperations[operation.Id] = c.Identity; return ValueTask.FromResult(operation);
        }),
        HostRoutes.Typed<OperationRequest, OperationRef>("Minecraft.Install", "Cancel", async (c, v) =>
        {
            lock (_planGate) if (_installOperations.GetValueOrDefault(v.Operation) != c.Identity) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            return await CancelOwnedOperation(c, v.Operation).ConfigureAwait(false);
        })
    ];
}
