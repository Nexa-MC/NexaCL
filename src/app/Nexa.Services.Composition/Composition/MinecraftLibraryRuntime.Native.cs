#if NEXA_NATIVE_SERVICES
using Nexa.Native.Minecraft.Client;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

public sealed class MinecraftLibraryRuntime(NativeMinecraftClient service, Task initialization, XsrCommandRouter commands) : IDisposable
{
    public NativeMinecraftClient Service { get; } = service;
    public Task Initialization { get; } = initialization;
    public XsrCommandRouter Commands { get; } = commands;
    public void Dispose() { }
}
public static class MinecraftLibraryRuntimeComposer
{
    public static MinecraftLibraryRuntime Compose(FoundationHost host, string defaultDirectory, IMinecraftInstanceSource? source = null, IXsrDispatchObserver? observer = null)
    {
        var client = host.NativeMinecraft;
        if (source is NativeMinecraftInstanceSource native && ReferenceEquals(native.Client, client)) source = null;
        if (source is not null) client.AttachLibrarySource(source);
        Task initialization = client.ConfigureLibraryAsync(defaultDirectory);
        _ = initialization.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        XsrCommandRouterBuilder commands = new();
        commands.Register<MinecraftLibraryRefreshCommand>(MinecraftLibraryRoutes.Refresh, async (_, token) => { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.RefreshAsync(token).ConfigureAwait(false); });
        commands.Register<MinecraftLibraryDirectoryCommand>(MinecraftLibraryRoutes.Directory, async (command, token) => { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.ChangeLibraryDirectoryAsync(command, token).ConfigureAwait(false); });
        commands.Register<MinecraftLibraryForgetCommand>(MinecraftLibraryRoutes.Forget, async (command, token) => { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.ForgetLibraryDirectoryAsync(command, token).ConfigureAwait(false); });
        commands.Register<MinecraftLibraryDeleteCommand>(MinecraftLibraryRoutes.Delete, async (command, token) => { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.DeleteLibraryInstanceAsync(command, token).ConfigureAwait(false); });
        commands.Register<MinecraftLibrarySelectCommand>(MinecraftLibraryRoutes.Select, async (command, token) => { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.SelectLibraryInstanceAsync(command, token).ConfigureAwait(false); });
        commands.Register<MinecraftLibraryRenameCommand>(MinecraftLibraryRoutes.Rename, async (command, token) => { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.RenameLibraryDirectoryAsync(command, token).ConfigureAwait(false); });
        return new(client, initialization, commands.Build(observer ?? new Observer()));
    }
    private sealed class Observer : IXsrDispatchObserver { public void OnCompleted(XsrDispatchObservation observation) { } }
}
#endif
