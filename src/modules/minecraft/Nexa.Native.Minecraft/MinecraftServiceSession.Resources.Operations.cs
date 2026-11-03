using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Resources;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private async Task<byte[]> ExecuteResourceOperationAsync(int operation, ServiceWireReader reader, CancellationToken token)
    {
        if (_resourceSource is null) throw new InvalidOperationException("Resource runtime is not configured.");
        await RefreshResourceSettingsAsync(token).ConfigureAwait(false);
        using var writer = new ServiceWireWriter();
        switch (operation)
        {
            case MinecraftServiceProtocol.ResourceResourceNetworkPolicyQuery:
                {
                    var value = reader.ResourceNetworkPolicyQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await Task.FromResult(new ResourceNetworkPolicySnapshot(ResourcePriority(), ResourceAutoDependencies(), _resourceTransport!.LastResolution)).ConfigureAwait(false); writer.XsrError(null); writer.ResourceNetworkPolicySnapshot(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceSearchQuery:
                {
                    var value = reader.ResourceSearchQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await _resourceSource!.SearchAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceSearchResult(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceDetailQuery:
                {
                    var value = reader.ResourceDetailQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await _resourceSource!.DetailAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceDetail(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceChangelogQuery:
                {
                    var value = reader.ResourceChangelogQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await ReadResourceChangelogAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceChangelog(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceIconQuery:
                {
                    var value = reader.ResourceIconQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await _resourceIcons!.ReadAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceIconResult(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceTranslationQuery:
                {
                    var value = reader.ResourceTranslationQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await _resourceTranslations!.ReadAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceTranslation(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceFavoritesQuery:
                {
                    var value = reader.ResourceFavoritesQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await _resourceFavorites!.ReadAsync(token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceFavoritesSnapshot(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceContentOnlineQuery:
                {
                    var value = reader.ResourceContentOnlineQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await _resourceContent!.ReadAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceContentOnline(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceContentOnlineBatchQuery:
                {
                    var value = reader.ResourceContentOnlineBatchQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await _resourceContent!.ReadBatchAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceContentOnlineBatch(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceInstanceQuery:
                {
                    var value = reader.ResourceInstanceQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await _resourceInstances!.ReadAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceInstanceContext(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceModPlanQuery:
                {
                    var value = reader.ResourceModPlanQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await PreviewResourcePlanAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceInstallPlan(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceModpackOnlineQuery:
                {
                    var value = reader.ResourceModpackOnlineQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await _resourcePacks!.ReadAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceModpackOnline(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceInspectModpack:
                {
                    var value = reader.ResourceModpackInspectQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await _resourcePacks!.InspectAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.MinecraftModpackPreview(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceModpackPreviewQuery:
                {
                    var value = reader.ResourceModpackPreviewQuery() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        var result = await _resourcePacks!.PreviewAsync(value, token).ConfigureAwait(false); writer.XsrError(null); writer.ResourceModpackPreview(result);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceFavoriteCommand:
                {
                    var value = reader.ResourceFavoriteCommand() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        await _resourceFavorites!.SetAsync(value, token).ConfigureAwait(false); writer.XsrError(null);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceContentUpdateCommand:
                {
                    var value = reader.ResourceContentUpdateCommand() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        await _resourceUpdates!.UpdateAsync(value, token).ConfigureAwait(false); writer.XsrError(null);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceContentUpdateBatchCommand:
                {
                    var value = reader.ResourceContentUpdateBatchCommand() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        await _resourceUpdates!.UpdateBatchAsync(value, token).ConfigureAwait(false); writer.XsrError(null);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceModInstallCommand:
                {
                    var value = reader.ResourceModInstallCommand() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        await _resourceMods!.InstallAsync(value, token).ConfigureAwait(false); writer.XsrError(null);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceDownloadCommand:
                {
                    var value = reader.ResourceDownloadCommand() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        await _resourceDownloads!.DownloadAsync(value, token).ConfigureAwait(false); writer.XsrError(null);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceModpackInstallCommand:
                {
                    var value = reader.ResourceModpackInstallCommand() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        await InstallResourcePackAsync(value, token).ConfigureAwait(false); writer.XsrError(null);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            case MinecraftServiceProtocol.ResourceResourceModpackUpdateCommand:
                {
                    var value = reader.ResourceModpackUpdateCommand() ?? throw new InvalidDataException("Missing resource request."); reader.End();
                    try
                    {
                        await UpdateResourcePackAsync(value, token).ConfigureAwait(false); writer.XsrError(null);
                    }
                    catch (Exception error) when (ExpectedResourceError(operation, error, token))
                    { writer.XsrError(ResourceError(operation, error)); }
                    break;
                }
            default: throw new InvalidDataException("Unknown resource operation.");
        }
        return writer.Finish();
    }
}
