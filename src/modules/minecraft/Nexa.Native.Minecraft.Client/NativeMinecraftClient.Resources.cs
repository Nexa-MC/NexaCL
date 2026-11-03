using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Resources;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    public async ValueTask<XsrResult<ResourceNetworkPolicySnapshot>> QueryResourceNetworkPolicyQueryAsync(ResourceNetworkPolicyQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceNetworkPolicyQuery, writer => writer.ResourceNetworkPolicyQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceNetworkPolicySnapshot() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceNetworkPolicySnapshot>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceSearchResult>> QueryResourceSearchQueryAsync(ResourceSearchQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceSearchQuery, writer => writer.ResourceSearchQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceSearchResult() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceSearchResult>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceDetail>> QueryResourceDetailQueryAsync(ResourceDetailQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceDetailQuery, writer => writer.ResourceDetailQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceDetail() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceDetail>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceChangelog>> QueryResourceChangelogQueryAsync(ResourceChangelogQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceChangelogQuery, writer => writer.ResourceChangelogQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceChangelog() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceChangelog>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceIconResult>> QueryResourceIconQueryAsync(ResourceIconQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceIconQuery, writer => writer.ResourceIconQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceIconResult() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceIconResult>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceTranslation>> QueryResourceTranslationQueryAsync(ResourceTranslationQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceTranslationQuery, writer => writer.ResourceTranslationQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceTranslation() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceTranslation>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceFavoritesSnapshot>> QueryResourceFavoritesQueryAsync(ResourceFavoritesQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceFavoritesQuery, writer => writer.ResourceFavoritesQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceFavoritesSnapshot() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceFavoritesSnapshot>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceContentOnline>> QueryResourceContentOnlineQueryAsync(ResourceContentOnlineQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceContentOnlineQuery, writer => writer.ResourceContentOnlineQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceContentOnline() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceContentOnline>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceContentOnlineBatch>> QueryResourceContentOnlineBatchQueryAsync(ResourceContentOnlineBatchQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceContentOnlineBatchQuery, writer => writer.ResourceContentOnlineBatchQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceContentOnlineBatch() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceContentOnlineBatch>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceInstanceContext>> QueryResourceInstanceQueryAsync(ResourceInstanceQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceInstanceQuery, writer => writer.ResourceInstanceQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceInstanceContext() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceInstanceContext>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceInstallPlan>> QueryResourceModPlanQueryAsync(ResourceModPlanQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceModPlanQuery, writer => writer.ResourceModPlanQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceInstallPlan() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceInstallPlan>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceModpackOnline>> QueryResourceModpackOnlineQueryAsync(ResourceModpackOnlineQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceModpackOnlineQuery, writer => writer.ResourceModpackOnlineQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceModpackOnline() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceModpackOnline>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<MinecraftModpackPreview>> InspectResourceModpackAsync(ResourceModpackInspectQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceInspectModpack, writer => writer.ResourceModpackInspectQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.MinecraftModpackPreview() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<MinecraftModpackPreview>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<ResourceModpackPreview>> QueryResourceModpackPreviewQueryAsync(ResourceModpackPreviewQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceModpackPreviewQuery, writer => writer.ResourceModpackPreviewQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.ResourceModpackPreview() ?? throw new InvalidDataException("Missing resource result.")) : XsrResult.Failure<ResourceModpackPreview>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult> DispatchResourceFavoriteCommandAsync(ResourceFavoriteCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceFavoriteCommand, writer => writer.ResourceFavoriteCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchResourceContentUpdateCommandAsync(ResourceContentUpdateCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceContentUpdateCommand, writer => writer.ResourceContentUpdateCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchResourceContentUpdateBatchCommandAsync(ResourceContentUpdateBatchCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceContentUpdateBatchCommand, writer => writer.ResourceContentUpdateBatchCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchResourceModInstallCommandAsync(ResourceModInstallCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceModInstallCommand, writer => writer.ResourceModInstallCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchResourceDownloadCommandAsync(ResourceDownloadCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceDownloadCommand, writer => writer.ResourceDownloadCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchResourceModpackInstallCommandAsync(ResourceModpackInstallCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceModpackInstallCommand, writer => writer.ResourceModpackInstallCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchResourceModpackUpdateCommandAsync(ResourceModpackUpdateCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceResourceModpackUpdateCommand, writer => writer.ResourceModpackUpdateCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
}
