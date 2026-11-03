#if NEXA_NATIVE_SERVICES
using Nexa.Native.Minecraft.Client;
using Nexa.Services.Resources;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr.Runtime;
namespace Nexa.Services.Composition;
internal static class NativeResourceRouters
{
    internal static void Register(XsrCommandRouterBuilder commands, XsrQueryRouterBuilder queries, NativeMinecraftClient client, Task initialization)
    {
        queries.Register<ResourceNetworkPolicyQuery, ResourceNetworkPolicySnapshot>(ResourceCatalogContract.NetworkPolicy, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceNetworkPolicyQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceSearchQuery, ResourceSearchResult>(ResourceCatalogContract.Search, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceSearchQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceDetailQuery, ResourceDetail>(ResourceCatalogContract.Detail, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceDetailQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceChangelogQuery, ResourceChangelog>(ResourceCatalogContract.Changelog, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceChangelogQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceIconQuery, ResourceIconResult>(ResourceCatalogContract.Icon, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceIconQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceTranslationQuery, ResourceTranslation>(ResourceCatalogContract.Translate, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceTranslationQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceFavoritesQuery, ResourceFavoritesSnapshot>(ResourceCatalogContract.Favorites, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceFavoritesQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceContentOnlineQuery, ResourceContentOnline>(ResourceCatalogContract.ContentOnline, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceContentOnlineQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceContentOnlineBatchQuery, ResourceContentOnlineBatch>(ResourceCatalogContract.ContentOnlineBatch, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceContentOnlineBatchQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceInstanceQuery, ResourceInstanceContext>(ResourceCatalogContract.Instance, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceInstanceQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceModPlanQuery, ResourceInstallPlan>(ResourceCatalogContract.PlanMod, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceModPlanQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceModpackOnlineQuery, ResourceModpackOnline>(ResourceModpackContract.Online, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceModpackOnlineQueryAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceModpackInspectQuery, MinecraftModpackPreview>(ResourceModpackContract.Inspect, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.InspectResourceModpackAsync(value, token).ConfigureAwait(false); });
        queries.Register<ResourceModpackPreviewQuery, ResourceModpackPreview>(ResourceModpackContract.Preview, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.QueryResourceModpackPreviewQueryAsync(value, token).ConfigureAwait(false); });
        commands.Register<ResourceFavoriteCommand>(ResourceCatalogContract.Favorite, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.DispatchResourceFavoriteCommandAsync(value, token).ConfigureAwait(false); });
        commands.Register<ResourceContentUpdateCommand>(ResourceCatalogContract.UpdateContent, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.DispatchResourceContentUpdateCommandAsync(value, token).ConfigureAwait(false); });
        commands.Register<ResourceContentUpdateBatchCommand>(ResourceCatalogContract.UpdateContentBatch, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.DispatchResourceContentUpdateBatchCommandAsync(value, token).ConfigureAwait(false); });
        commands.Register<ResourceModInstallCommand>(ResourceCatalogContract.InstallMod, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.DispatchResourceModInstallCommandAsync(value, token).ConfigureAwait(false); });
        commands.Register<ResourceDownloadCommand>(ResourceCatalogContract.Download, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.DispatchResourceDownloadCommandAsync(value, token).ConfigureAwait(false); });
        commands.Register<ResourceModpackInstallCommand>(ResourceModpackContract.Install, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.DispatchResourceModpackInstallCommandAsync(value, token).ConfigureAwait(false); });
        commands.Register<ResourceModpackUpdateCommand>(ResourceModpackContract.Update, async (value, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.DispatchResourceModpackUpdateCommandAsync(value, token).ConfigureAwait(false); });
    }
}
#endif
