using Nexa.Desktop.Ui;
using Nexa.Services.Accounts;
using Nexa.Services.Composition;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;
using Nexa.Services.Tasks;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{

    private static int ReadCellInt(XsrStateStore store, XsrSemanticId key) =>
        (int?)store.ReadAppliedValue(store.Resolve(key)) ?? -1;

    private static MinecraftRuntime CreateRecordingRuntime(RecordingStartRoute recording)
    {
        NoopDispatchObserver observer = new();
        XsrCommandRouterBuilder commands = new();
        commands.Register<MinecraftStartCommand>(MinecraftRouteIds.Start, recording.Handle);
        commands.Register<MinecraftDecideJavaAcquisitionCommand>(MinecraftRouteIds.AcquireDecide, (command, _) =>
        {
            recording.LastDecision = command.Approve;
            return ValueTask.FromResult(XsrResult.Success());
        });
        commands.Register<MinecraftSelectJavaVersionCommand>(MinecraftRouteIds.JavaVersionSelect, (command, _) =>
        { recording.LastJavaMajor = command.Major; return ValueTask.FromResult(XsrResult.Success()); });
        commands.Register<MinecraftCancelProcessCommand>(MinecraftRouteIds.ProcessCancel, (command, _) =>
        { recording.LastCancelledSession = command.SessionId; return ValueTask.FromResult(XsrResult.Success()); });
        XsrQueryRouterBuilder queries = new();
        return new MinecraftRuntime(
            new MinecraftVersionDiscovery(),
            new MinecraftInstanceDiscovery(),
            new MinecraftProcessService(),
            commands.Build(observer),
            queries.Build(observer));
    }

    private static MinecraftInstanceDescriptor Instance(string id)
    {
        MinecraftVersionClassification classification = new(
            id,
            "release",
            MinecraftVersionCategory.Release,
            null);
        MinecraftVersionDescriptor version = new(
            id,
            Path.Combine(Path.GetTempPath(), id),
            Path.Combine(Path.GetTempPath(), id, id + ".json"),
            null,
            null,
            "example.Main",
            null,
            classification);
        return new MinecraftInstanceDescriptor(id, version.DirectoryPath, id, version, new MinecraftInstanceMetadata());
    }

    private static XsrUiSceneNode FindByKey(XsrUiShell shell, XsrUiScene scene, string key) =>
        scene.Nodes.Single(node => string.Equals(shell.Tree.Name(node.Entity), key, StringComparison.Ordinal));

    private static bool HasKey(XsrUiShell shell, XsrUiScene scene, string key) =>
        scene.Nodes.Any(node => string.Equals(shell.Tree.Name(node.Entity), key, StringComparison.Ordinal));

    private static void Emit(DesktopUiIntentSink intents, string command) =>
        intents.Emit(XsrSemanticId.Parse(command), default, XsrCorrelationId.Create());

    private static void Emit(
        DesktopUiIntentSink intents,
        string command,
        XsrUiEntityId source) =>
        intents.Emit(XsrSemanticId.Parse(command), source, XsrCorrelationId.Create());

    private static XsrUiEntityId FindEntity(XsrUiShell shell, string key)
    {
        XsrUiEntityId found = default;
        shell.Tree.Walk(
            shell.Stage.Root,
            entity =>
            {
                if (string.Equals(shell.Tree.Name(entity), key, StringComparison.Ordinal))
                {
                    found = entity;
                    return false;
                }

                return true;
            });
        return found;
    }

    private static bool IsVisible(XsrUiShell shell, string key)
    {
        XsrUiEntityId entity = FindEntity(shell, key);
        return entity.IsAssigned
            && shell.Tree.GetComponent<XsrUiElement>(entity)?.IsVisible == true;
    }

    private static string ReadCell(XsrStateStore store, XsrSemanticId key) =>
        (string?)store.ReadAppliedValue(store.Resolve(key)) ?? string.Empty;

    private sealed class FailingInstallMetadata : Nexa.Services.Minecraft.Install.IMinecraftInstallMetadataSource
    {
        public Task<System.Text.Json.Nodes.JsonObject> FetchVanillaVersionJsonAsync(
            string gameVersion, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated metadata outage");

        public Task<System.Text.Json.Nodes.JsonObject> FetchLoaderProfileJsonAsync(
            Nexa.Services.Minecraft.Install.InstallLoader loader, string gameVersion, string build,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated metadata outage");

        public Task<System.Text.Json.Nodes.JsonObject> FetchAssetIndexJsonAsync(
            string indexUrl, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated metadata outage");
    }
}
