#if NEXA_NATIVE_SERVICES
using Nexa.Native.Minecraft.Client;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

public sealed class MinecraftInstallRuntime(NativeMinecraftClient service, Task initialization, XsrCommandRouter commands) : IDisposable
{
    public NativeMinecraftClient Service { get; } = service;
    public Task Initialization { get; } = initialization;
    public XsrCommandRouter Commands { get; } = commands;
    public void Dispose() { }
}
public static class MinecraftInstallRuntimeComposer
{
    public static MinecraftInstallRuntime Compose(FoundationHost host, IInstallCatalogSource? catalogSource = null,
        IXsrDispatchObserver? observer = null, string? packagedCurseForgeApiKey = null)
    {
        var client = host.NativeMinecraft;
        if (catalogSource is not null) client.AttachCatalogSource(catalogSource);
        async Task InitializeAsync()
        {
            await client.EnsureInstallationConfiguredAsync(RegionalPolicy.Current.CountryCode, packagedCurseForgeApiKey).ConfigureAwait(false);
            if (catalogSource is not null) await client.EnsureCatalogConfiguredAsync().ConfigureAwait(false);
        }
        Task initialization = InitializeAsync();
        _ = initialization.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        XsrCommandRouterBuilder commands = new();
        commands.Register<MinecraftInstallStopCommand>(MinecraftInstallRoutes.Stop, async (command, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.StopInstallationAsync(command.Pause, token).ConfigureAwait(false); });
        commands.Register<MinecraftInstallRecoveryCommand>(MinecraftInstallRoutes.Recover, async (command, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.RecoverInstallationAsync(command, token).ConfigureAwait(false); });
        commands.Register<MinecraftInstallCommand>(MinecraftInstallRoutes.Run, async (command, token) =>
        {
            await initialization.WaitAsync(token).ConfigureAwait(false);
            var result = await client.InstallMinecraftAsync(command, token).ConfigureAwait(false);
            return result.IsSuccess ? XsrResult.Success() : XsrResult.Failure(result.Error!);
        });
        return new(client, initialization, commands.Build(observer ?? new Observer()));
    }
    private sealed class Observer : IXsrDispatchObserver { public void OnCompleted(XsrDispatchObservation observation) { } }
}
#endif
