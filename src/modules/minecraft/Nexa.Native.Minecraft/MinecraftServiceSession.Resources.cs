using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft;
using Nexa.Services.Resources;
using Nexa.Services.Settings;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private HttpClient? _resourceHttp;
    private ResourceProviderHttp? _resourceTransport;
    private ResourceOnlineInformationCache? _resourceInformation;
    private IResourceCatalogSource? _resourceSource;
    private ResourceTranslationService? _resourceTranslations;
    private ResourceIconService? _resourceIcons;
    private ResourceFavoritesService? _resourceFavorites;
    private ResourceDownloadService? _resourceDownloads;
    private ResourceInstanceService? _resourceInstances;
    private ResourceContentOnlineService? _resourceContent;
    private ResourceContentUpdateService? _resourceUpdates;
    private ResourceModpackOnlineService? _resourcePacks;
    private ResourceModInstallService? _resourceMods;
    private SettingsEffectiveSnapshot? _resourcePolicy;
    private readonly object _resourcePolicyGate = new();
    private string ResourcePriority() => _installationRegion!.IsMainlandChina
        ? Volatile.Read(ref _resourcePolicy)?.Values.FirstOrDefault(item => item.Key == "network.resource-source")?.Value.Value ?? "follow-request" : "official-first";
    private bool ResourceAutoDependencies() => Volatile.Read(ref _resourcePolicy)?.Values.FirstOrDefault(item => item.Key == "network.auto-install-dependencies")?.Value.Value != "false";
    private async Task RefreshResourceSettingsAsync(CancellationToken token)
    {
        var result = await _settings.ReadAsync(new(), token).ConfigureAwait(false);
        if (!result.IsSuccess) throw new IOException(result.Error?.Message ?? "无法读取资源网络偏好。");
        lock (_resourcePolicyGate)
            if (_resourcePolicy is null || result.Value!.Revision >= _resourcePolicy.Revision) Volatile.Write(ref _resourcePolicy, result.Value);
    }
    private async Task<byte[]> ConfigureResourcesAsync(ServiceWireReader reader, CancellationToken token)
    {
        string? favorites = reader.String(), key = reader.String(); bool reverse = reader.Boolean(); reader.End();
        if (favorites is not null && !Path.IsPathFullyQualified(favorites) || key?.Length > 4096) throw new InvalidDataException("Invalid resource configuration.");
        await _initialization.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing), this);
            if (_resourceSource is not null) throw new InvalidOperationException("Resources are already configured.");
            var installation = _installation ?? throw new InvalidOperationException("Installation is not configured.");
            var region = _installationRegion!;
            _resourceHttp = CreateInstallationHttp(false); _resourceHttp.Timeout = TimeSpan.FromSeconds(45);
            _resourceTransport = new(_resourceHttp, ResourceProviderHttp.ResolveApiKey(key)) { SourcePriority = ResourcePriority, CountryPolicy = region };
            string? cache = _catalogCacheDirectory is null ? null : Path.GetDirectoryName(_catalogCacheDirectory);
            _resourceInformation = new(_cache, cache is null ? null : Path.Combine(cache, "resource-information"));
            _resourceSource = reverse ? new ResourceSource(this) : new MergedResourceCatalog(new ResourceCatalogService(_resourceHttp, _resourceTransport),
                new CurseForgeResourceCatalog(_resourceTransport), _resourceInformation, () => _resourceTransport.CachePolicyIdentity);
            _resourceTranslations = new(_resourceTransport, _resourceInformation);
            _resourceIcons = new(_resourceHttp, _cache) { WorkScheduler = _work, SourcePriority = ResourcePriority, CountryPolicy = region };
            _resourceFavorites = new(favorites);
            _resourceDownloads = new(_resourceSource, _sessionDownloads, _tasks, _resourceHttp) { SourcePriority = ResourcePriority, CountryPolicy = region };
            _resourceInstances = new(_resourceTransport, _resourceInformation);
            _resourceContent = new(_resourceInstances, _resourceSource, _resourceTranslations);
            _resourceUpdates = new(_resourceContent, _resourceDownloads, _store);
            _resourcePacks = new(installation, _resourceInstances, _resourceSource, _resourceDownloads, _resourceInformation, Path.Combine(cache ?? Path.GetTempPath(), "modpack-archives"));
            _resourceMods = new(_resourceSource, _resourceInstances, _resourceDownloads, _localJars!, _tasks) { AutoInstallDependencies = ResourceAutoDependencies };
            return [];
        }
        finally { _initialization.Release(); }
    }
    private async Task<ResourceChangelog> ReadResourceChangelogAsync(ResourceChangelogQuery query, CancellationToken token)
        => _resourceSource is IResourceChangelogSource logs ? await logs.ReadChangelogAsync(query, token).ConfigureAwait(false) : new("");
    private async Task<ResourceInstallPlan> PreviewResourcePlanAsync(ResourceModPlanQuery query, CancellationToken token)
        => await new ResourceDependencyPlanner(_resourceSource!) { AutoInstallDependencies = ResourceAutoDependencies() }
            .PreviewAsync(query.Command, await _resourceInstances!.ReadAsync(query.Command.Instance, token).ConfigureAwait(false), token).ConfigureAwait(false);
    private async Task InstallResourcePackAsync(ResourceModpackInstallCommand command, CancellationToken token)
    {
        var result = await _resourcePacks!.InstallAsync(command, token).ConfigureAwait(false);
        if (!result.IsSuccess) throw new ResourceFailure(result.Error!);
    }
    private async Task UpdateResourcePackAsync(ResourceModpackUpdateCommand command, CancellationToken token)
    {
        var result = await _resourcePacks!.UpdateAsync(command, token).ConfigureAwait(false);
        if (!result.IsSuccess) throw new ResourceFailure(result.Error!);
    }
    private sealed class ResourceFailure(XsrError error) : Exception(error.Message) { internal XsrError Error { get; } = error; }
    private static bool ExpectedResourceError(int operation, Exception error, CancellationToken token)
    {
        if (error is ResourceFailure) return true;
        if (operation is MinecraftServiceProtocol.ResourceResourceModpackInstallCommand or MinecraftServiceProtocol.ResourceResourceModpackUpdateCommand
            or MinecraftServiceProtocol.ResourceInspectModpack or MinecraftServiceProtocol.ResourceResourceModpackOnlineQuery or MinecraftServiceProtocol.ResourceResourceModpackPreviewQuery)
            return !token.IsCancellationRequested && (ResourceOnlineInformationCache.Recoverable(error) || error is ArgumentException);
        if (operation is MinecraftServiceProtocol.ResourceResourceModPlanQuery or MinecraftServiceProtocol.ResourceResourceModInstallCommand or MinecraftServiceProtocol.ResourceResourceDownloadCommand)
            return error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException;
        if (operation is MinecraftServiceProtocol.ResourceResourceContentUpdateCommand or MinecraftServiceProtocol.ResourceResourceContentUpdateBatchCommand)
            return error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                || operation == MinecraftServiceProtocol.ResourceResourceContentUpdateBatchCommand && error is AggregateException;
        return false;
    }
    private static XsrError ResourceError(int operation, Exception error)
    {
        if (error is ResourceFailure failure) return failure.Error;
        string? code = operation switch
        {
            MinecraftServiceProtocol.ResourceResourceModPlanQuery => "resources.plan.failed",
            MinecraftServiceProtocol.ResourceResourceModInstallCommand => "resources.install.failed",
            MinecraftServiceProtocol.ResourceResourceDownloadCommand => "resources.download.failed",
            MinecraftServiceProtocol.ResourceResourceContentUpdateCommand or MinecraftServiceProtocol.ResourceResourceContentUpdateBatchCommand => "resources.update.failed",
            _ => null
        };
        return code is null ? MinecraftErrors.InvalidRequest(error.Message) : new(XsrErrorKind.Rejected, XsrSemanticId.Parse(code), error.Message);
    }
    private sealed class ResourceSource(MinecraftServiceSession owner) : IResourceCatalogSource, IResourceChangelogSource, IResourceFileSource
    {
        public async Task<ResourceSearchResult> SearchAsync(ResourceSearchQuery query, CancellationToken token)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.ResourceSearch); writer.ResourceSearchQuery(query);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); var value = reader.ResourceSearchResult() ?? throw new InvalidDataException("Missing resource search."); reader.End(); return value;
        }
        public async Task<ResourceDetail> DetailAsync(ResourceDetailQuery query, CancellationToken token)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.ResourceDetail); writer.ResourceDetailQuery(query);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); var value = reader.ResourceDetail() ?? throw new InvalidDataException("Missing resource detail."); reader.End(); return value;
        }
        public async Task<ResourceChangelog> ReadChangelogAsync(ResourceChangelogQuery query, CancellationToken token)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.ResourceChangelog); writer.ResourceChangelogQuery(query);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); var value = reader.ResourceChangelog() ?? throw new InvalidDataException("Missing resource changelog."); reader.End(); return value;
        }
        public async Task<ResourceVersion?> ReadVersionAsync(ResourceDownloadCommand command, CancellationToken token)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.ResourceVersion); writer.ResourceDownloadCommand(command);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); var value = reader.ResourceVersion(); reader.End(); return value;
        }
    }
}
