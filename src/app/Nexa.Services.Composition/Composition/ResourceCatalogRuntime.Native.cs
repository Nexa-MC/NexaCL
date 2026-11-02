#if NEXA_NATIVE_SERVICES
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Resources;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

public sealed class ResourceCatalogRuntime(XsrQueryRouter queries, XsrCommandRouter commands, Task initialization) : IDisposable
{
    public XsrQueryRouter Queries { get; } = queries;
    public XsrCommandRouter Commands { get; } = commands;
    public Task Initialization { get; } = initialization;
    public void Dispose() { }
}
public static class ResourceCatalogRuntimeComposer
{
    public static ResourceCatalogRuntime Compose(IResourceCatalogSource? source = null, IXsrDispatchObserver? observer = null,
        FoundationHost? host = null, string? favoritesPath = null, IMinecraftInstallationPort? installer = null)
        => ComposeWithInstallation(source, observer, host, favoritesPath, installer, null);
    public static ResourceCatalogRuntime Compose(IResourceCatalogSource? source, IXsrDispatchObserver? observer,
        FoundationHost? host, string? favoritesPath, IMinecraftInstallationPort? installer, string? packagedCurseForgeApiKey)
        => ComposeWithInstallation(source, observer, host, favoritesPath, installer, packagedCurseForgeApiKey);
    public static ResourceCatalogRuntime ComposeWithInstallation(IResourceCatalogSource? source, IXsrDispatchObserver? observer,
        FoundationHost? host, string? favoritesPath, IMinecraftInstallationPort? installer, string? packagedCurseForgeApiKey)
    {
        ArgumentNullException.ThrowIfNull(host);
        var client = host.NativeMinecraft;
        if (source is not null) client.AttachResourceCatalogSource(source);
        async Task InitializeAsync()
        {
            await client.EnsureInstallationConfiguredAsync(RegionalPolicy.Current.CountryCode, packagedCurseForgeApiKey).ConfigureAwait(false);
            await client.ConfigureResourcesAsync(favoritesPath, packagedCurseForgeApiKey).ConfigureAwait(false);
        }
        Task initialization = InitializeAsync();
        _ = initialization.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        XsrCommandRouterBuilder commands = new(); XsrQueryRouterBuilder queries = new();
        NativeResourceRouters.Register(commands, queries, client, initialization);
        var dispatch = observer ?? new Observer(); return new(queries.Build(dispatch), commands.Build(dispatch), initialization);
    }
    private sealed class Observer : IXsrDispatchObserver { public void OnCompleted(XsrDispatchObservation observation) { } }
}
#endif
