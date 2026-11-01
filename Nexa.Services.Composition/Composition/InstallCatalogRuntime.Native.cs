#if NEXA_NATIVE_SERVICES
using Nexa.Native.Minecraft.Client;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

public sealed class InstallCatalogRuntime(Task initialization, XsrCommandRouter commands, XsrQueryRouter queries) : IDisposable
{
    public Task Initialization { get; } = initialization;
    public XsrCommandRouter Commands { get; } = commands;
    public XsrQueryRouter Queries { get; } = queries;
    public void Dispose() { }
}
public static class InstallCatalogRuntimeComposer
{
    public static InstallCatalogRuntime Compose(FoundationHost host, IInstallCatalogSource? source = null, IXsrDispatchObserver? observer = null,
        string? packagedCurseForgeApiKey = null)
    {
        NativeMinecraftClient client = host.NativeMinecraft;
        if (source is not null) client.AttachCatalogSource(source);
        async Task InitializeAsync()
        {
            await client.EnsureInstallationConfiguredAsync(RegionalPolicy.Current.CountryCode, packagedCurseForgeApiKey).ConfigureAwait(false);
            await client.EnsureCatalogConfiguredAsync().ConfigureAwait(false);
        }
        Task initialization = InitializeAsync();
        _ = initialization.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        XsrCommandRouterBuilder commands = new(); XsrQueryRouterBuilder queries = new();
        commands.Register<InstallDraftChange>(MinecraftInstallDraftContract.Change, async (change, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.ChangeInstallationDraftAsync(change, token).ConfigureAwait(false); });
        commands.Register<InstallDraftReplace>(MinecraftInstallDraftContract.Replace, async (change, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.ReplaceInstallationDraftAsync(change, token).ConfigureAwait(false); });
        commands.Register<InstallCatalogReadCommand>(InstallCatalogRoutes.Read, async (query, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.ReadCatalogAsync(query, token).ConfigureAwait(false); });
        commands.Register<InstallCatalogPrefetchCommand>(InstallCatalogRoutes.Prefetch, async (query, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.PrefetchCatalogAsync(query, token).ConfigureAwait(false); });
        queries.Register<InstallEligibilityQuery, InstallEligibilityResult>(InstallEligibilityContract.Query, async (query, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return XsrResult.Success(await client.ReadInstallationEligibilityAsync(query, token).ConfigureAwait(false)); });
        queries.Register<MinecraftInstallEditQuery, MinecraftInstallEditSnapshot>(MinecraftInstallEditContract.Query, async (query, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return XsrResult.Success(await client.ReadInstallationAsync(query, token).ConfigureAwait(false)); });
        queries.Register<MinecraftInstallEditPlanQuery, MinecraftInstallEditPlan>(MinecraftInstallEditPlanContract.Query, async (query, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return XsrResult.Success(await client.ReadInstallationPlanAsync(query, token).ConfigureAwait(false)); });
        var dispatch = observer ?? new Observer(); return new(initialization, commands.Build(dispatch), queries.Build(dispatch));
    }
    private sealed class Observer : IXsrDispatchObserver { public void OnCompleted(XsrDispatchObservation observation) { } }
}
#endif
