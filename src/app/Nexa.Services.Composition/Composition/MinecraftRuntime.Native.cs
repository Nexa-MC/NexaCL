#if NEXA_NATIVE_SERVICES
using Nexa.Native.Minecraft.Client;
using Nexa.Services.Capabilities;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

public sealed class MinecraftRuntime : IDisposable
{
    internal MinecraftRuntime(NativeMinecraftClient client, Task initialization, XsrCommandRouter commands, XsrQueryRouter queries)
    { LaunchCoordinator = client; Initialization = initialization; Commands = commands; Queries = queries; Instances = new NativeMinecraftInstanceSource(client); }
    public NativeMinecraftClient LaunchCoordinator { get; }
    public Task Initialization { get; }
    public IMinecraftInstanceSource Instances { get; }
    public XsrCommandRouter Commands { get; }
    public XsrQueryRouter Queries { get; }
    public void Dispose() { }
}

public static class MinecraftRuntimeComposer
{
    public static MinecraftRuntime Compose(FoundationHost host, string minecraftRootDirectory,
        IXsrDispatchObserver? observer = null, TimeProvider? timeProvider = null,
        Nexa.Services.Accounts.IAccountLaunchIdentityResolver? identityResolver = null,
        string? launcherVersion = null, IMinecraftWindowProbe? windowProbe = null,
        Action<int>? gameWindowAppeared = null, string? jvmHostExecutable = null, string? packagedCurseForgeApiKey = null,
        Func<CancellationToken, ValueTask<XsrResult>>? beforeProcessStart = null)
    {
        ArgumentNullException.ThrowIfNull(host); ArgumentException.ThrowIfNullOrWhiteSpace(minecraftRootDirectory);
        string root = Path.GetFullPath(minecraftRootDirectory); var client = host.NativeMinecraft;
        client.AttachLaunchHost(windowProbe ?? new MinecraftWindowProbe(), async (directory, instance, plan, token) =>
        {
            var settings = await Task.Run(() => JvmRunSettings.Read(plan.GameDirectory), token).ConfigureAwait(false);
            var snapshot = await host.MachineCapabilities.ReadAsync(new MachineCapabilityQuery(plan.InstanceDirectory, instance, directory)
            {
                JavaExecutablePath = plan.JavaExecutablePath, PlannedHeapMiB = plan.HeapLimitMiB,
                PlannedClasspathCount = plan.ClasspathEntries.Count, PlannedLoader = plan.ModLoader.Kind.ToString(),
                PlannedRenderDistance = settings.RenderDistance, RefreshInstance = true
            }, cancellationToken: token).ConfigureAwait(false);
            return CapabilityPreflightEngine.Evaluate(snapshot);
        }, gameWindowAppeared, beforeProcessStart);
        async Task InitializeAsync()
        {
            await client.EnsureInstallationConfiguredAsync(RegionalPolicy.Current.CountryCode, packagedCurseForgeApiKey).ConfigureAwait(false);
            await client.ConfigureLaunchAsync(root, launcherVersion ?? "2.0.0").ConfigureAwait(false);
        }
        Task initialization = InitializeAsync();
        host.MinecraftRemediations.BindNative(client);
        _ = initialization.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        XsrCommandRouterBuilder commands = new(); XsrQueryRouterBuilder queries = new();
        commands.Register<MinecraftStartCommand>(MinecraftRouteIds.Start, async (command, token) =>
        {
            await initialization.WaitAsync(token).ConfigureAwait(false);
            if (command.ExpectedAccountRosterRevision is { } revision)
                return await client.StartAuthorizedMinecraftAsync(command.InstanceId, command.AccountIndex, command.MinecraftRootDirectory ?? root,
                    command.ServerAddress, revision, command.ExpectedSettingsRevision, command.LaunchProfileId, token).ConfigureAwait(false);
            return await client.StartMinecraftAsync(command.InstanceId, command.AccountIndex, command.MinecraftRootDirectory ?? root, command.ServerAddress, token).ConfigureAwait(false);
        });
        commands.Register<MinecraftCancelLaunchCommand>(MinecraftRouteIds.LaunchCancel, async (command, token) =>
            await client.CancelLaunchAsync(command.AttemptId, token).ConfigureAwait(false) ? XsrResult.Success() : XsrResult.Failure(MinecraftErrors.InvalidRequest("no launch pipeline is running.")));
        commands.Register<MinecraftDecideJavaAcquisitionCommand>(MinecraftRouteIds.AcquireDecide, async (command, token) =>
            await client.DecideJavaAsync(command.Approve, command.AttemptId, token).ConfigureAwait(false) ? XsrResult.Success() : XsrResult.Failure(MinecraftErrors.InvalidRequest("no Java acquisition is pending.")));
        commands.Register<MinecraftSelectJavaVersionCommand>(MinecraftRouteIds.JavaVersionSelect, async (command, token) => await client.SelectJavaVersionAsync(command.Major, command.AttemptId, token).ConfigureAwait(false));
        commands.Register<MinecraftSelectJavaCommand>(MinecraftRouteIds.JavaSelect, async (command, token) => await client.SelectJavaPathAsync(command.Path, command.AttemptId, token).ConfigureAwait(false));
        commands.Register<MinecraftCancelProcessCommand>(MinecraftRouteIds.ProcessCancel, async (command, token) => await client.CancelProcessAsync(command.SessionId, token).ConfigureAwait(false));
        commands.Register<LaunchPreflightDecision>(LaunchPreflightContract.DecisionCommand, async (command, token) =>
            await client.DecidePreflightAsync(command, token).ConfigureAwait(false) ? XsrResult.Success() : XsrResult.Failure(MinecraftErrors.InvalidRequest("预检决定已失效或不可绕过。")));
        commands.Register<JavaInstallStopCommand>(JavaInstallRoutes.Stop, async (command, token) => await client.StopJavaAsync(command.Pause, token).ConfigureAwait(false));
        commands.Register<JavaInstallRecoverCommand>(JavaInstallRoutes.Recover, async (_, token) => await client.RecoverJavaAsync(client.JavaRuntimeRoot, token).ConfigureAwait(false));
        commands.Register<MinecraftSafeLaunchSessionCommand>(MinecraftSafeLaunchSessionContract.Set, async (command, token) =>
        { await initialization.WaitAsync(token).ConfigureAwait(false); return await client.SetSafeLaunchAsync(command.Required, token).ConfigureAwait(false); });
        queries.Register<MinecraftSafeLaunchSessionQuery, MinecraftSafeLaunchSessionSnapshot>(MinecraftSafeLaunchSessionContract.Query,
            async (_, token) => XsrResult.Success(await client.ReadSafeLaunchAsync(token).ConfigureAwait(false)));
        commands.Register<MinecraftProcessInteractionCommand>(MinecraftProcessInteractionContract.Interact, client.InteractProcessAsync);
        queries.Register<MinecraftProcessMetricsQuery, MinecraftProcessMetrics>(MinecraftProcessInteractionContract.Metrics, client.ReadProcessMetricsAsync);
        queries.Register<MinecraftProcessOutputQuery, MinecraftProcessOutputSnapshot>(MinecraftProcessOutputContract.Query,
            async (query, token) => await client.ReadOutputAsync(query.SessionId, token).ConfigureAwait(false));
        NativeMinecraftReadRouters.Register(queries, client); JavaManualDownloadRuntime.Register(queries, commands, client);
        return new(client, initialization, commands.Build(observer ?? new Observer(), timeProvider), queries.Build(observer ?? new Observer(), timeProvider));
    }
    private sealed class Observer : IXsrDispatchObserver { public void OnCompleted(XsrDispatchObservation observation) { } }
}
#endif
