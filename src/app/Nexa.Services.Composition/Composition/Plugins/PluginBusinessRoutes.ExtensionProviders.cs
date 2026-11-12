using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private static readonly QualifiedId DiagnosticProviderKind = new("nexa:diagnostics/rules");
    private static readonly QualifiedId TranslationProviderKind = new("nexa:catalog/translation");
    private static readonly QualifiedId FormatProviderKind = new("nexa:content/format");
    private static readonly BusinessProviderMethod DiagnosticProviderMethod = new("Analyze", Abi100Schemas.InstanceSummary, Abi100Schemas.DiagnosticAnalysis);
    private static readonly BusinessProviderMethod TranslationProviderMethod = new("Translate", Abi100Schemas.CatalogTranslationInput, Abi100Schemas.CatalogTranslation);
    private static readonly BusinessProviderMethod FormatProviderMethod = new("Inspect", Abi100Schemas.ContentInspectionInput, Abi100Schemas.ContentInspection);
    private IEnumerable<HostCapabilityRoute> ExtensionProviderRoutes => [
        HostRoutes.Typed<ProviderDescriptor, RegistrationRef>("Minecraft.Diagnostics", "RegisterRuleProvider", (c, v) => ValueTask.FromResult(Services.RegisterHostProvider(c, DiagnosticProviderKind, v, [DiagnosticProviderMethod]))),
        HostRoutes.Typed<ProviderInstanceQuery, DiagnosticAnalysis>("Minecraft.Diagnostics", "AnalyzeWithProvider", async (c, v) =>
        {
            var instance = FindInstance(c, v.Instance).Summary;
            var result = DiagnosticAnalysis.Read(await Services.QueryHostProvider(c, DiagnosticProviderKind, v.Provider, DiagnosticProviderMethod, InstanceSummary.Write(instance)).ConfigureAwait(false));
            if (result.Instance != instance.Id || result.Reports.Length > 128 || result.Issues.Length > 128) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            if (FindInstance(c, v.Instance).Summary.Revision != instance.Revision) throw new AbiHostException(AbiErrors.STALE_REVISION);
            return result with { Reports = result.Reports.Select(p => p with { Code = v.Provider.Value + "/" + p.Code, CorrelationId = c.Options.CorrelationId, OperationId = c.Options.OperationId }).ToImmutableArray() };
        }),
        HostRoutes.Typed<ProviderDescriptor, RegistrationRef>("Catalog", "RegisterTranslationProvider", (c, v) => ValueTask.FromResult(Services.RegisterHostProvider(c, TranslationProviderKind, v, [TranslationProviderMethod]))),
        HostRoutes.Typed<CatalogTranslationRequest, CatalogProject>("Catalog", "ReadTranslatedProject", async (c, v) =>
        {
            if (v.Locale.Length is < 2 or > 64 || v.Locale.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-')) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            CatalogProject project;
            if (CustomCatalog(v.Resource.Provider))
            {
                project = CatalogProject.Read(await QueryCatalogProvider(c, v.Resource.Provider, "ReadProject", ResourceRef.Write(v.Resource)).ConfigureAwait(false));
                CheckCatalogProject(project, v.Resource.Provider);
                if (project.Resource != v.Resource) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            }
            else project = ProjectResource((await CatalogDetail(v.Resource, c.Options.Cancellation).ConfigureAwait(false)).Project, CatalogReference(v.Resource));
            var translated = CatalogTranslation.Read(await Services.QueryHostProvider(c, TranslationProviderKind, v.Provider, TranslationProviderMethod,
                CatalogTranslationInput.Write(new(project, v.Locale))).ConfigureAwait(false));
            if (translated.Name.Length is < 1 or > 1024 || translated.Summary.Length > 65536) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            return project with { Name = translated.Name, Summary = translated.Summary };
        }),
        HostRoutes.Typed<ProviderDescriptor, RegistrationRef>("Minecraft.Content", "RegisterFormatProvider", (c, v) => ValueTask.FromResult(Services.RegisterHostProvider(c, FormatProviderKind, v, [FormatProviderMethod]))),
        HostRoutes.Typed<ContentInspectionRequest, ContentInspection>("Minecraft.Content", "InspectWithProvider", async (c, v) =>
        {
            var stat = await Application.Files.StatAsync(c.Identity, v.File, c.Options.Cancellation).ConfigureAwait(false);
            if (stat.IsDirectory || stat.Size > 65536) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var bytes = await Application.Files.ReadAsync(c.Identity, new(v.File, 0, stat.Size), c.Options.Cancellation).ConfigureAwait(false);
            var result = ContentInspection.Read(await Services.QueryHostProvider(c, FormatProviderKind, v.Provider, FormatProviderMethod,
                ContentInspectionInput.Write(new(stat.Name, new AbiValue.Bytes(bytes.ToImmutableArray())))).ConfigureAwait(false));
            if (result.Properties.Length > 256 || result.Issues.Length > 128 || result.Properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != result.Properties.Length)
                throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            if ((await Application.Files.StatAsync(c.Identity, v.File, c.Options.Cancellation).ConfigureAwait(false)).Revision != stat.Revision) throw new AbiHostException(AbiErrors.STALE_REVISION);
            return result;
        }),
        HostRoutes.Typed<ResourceCollection, RegistrationRef>("Catalog", "RegisterCollection", (c, v) =>
        {
            if (v.Resources.Length > 256 || v.Tags.Length > 64 || v.Resources.Distinct().Count() != v.Resources.Length || v.Tags.Distinct().Count() != v.Tags.Length) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            foreach (var resource in v.Resources) CheckCatalogReference(resource, resource.Provider);
            return ValueTask.FromResult(Application.Runtime.Definitions.Stage(c, [new(v.Id, new("nexa:catalog/collections"), Abi100Schemas.ResourceCollection, ResourceCollection.Write(v), [])]).Single());
        }),
        HostRoutes.Typed<PageRequest, Page<ResourceCollection>>("Catalog", "ListCollections", (_, v) => ValueTask.FromResult(ContentPage(
            Application.Runtime.Definitions.Entries.Where(e => e.Definition.Registry.Value == "nexa:catalog/collections").OrderBy(e => e.Definition.Id.Value, StringComparer.Ordinal)
                .Select(e => ResourceCollection.Read(e.Definition.Body)).ToImmutableArray(), v))),
        HostRoutes.Typed<QualifiedRequest, ResourceCollection>("Catalog", "ReadCollection", (_, v) => ValueTask.FromResult(ResourceCollection.Read(
            (Application.Runtime.Definitions.Entries.FirstOrDefault(e => e.Definition.Registry.Value == "nexa:catalog/collections" && e.Definition.Id == v.Id)
                ?? throw new AbiHostException(AbiErrors.NOT_FOUND)).Definition.Body)))
    ];
}
