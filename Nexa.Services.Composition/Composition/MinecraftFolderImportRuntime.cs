using System.Text.Json;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

public sealed record MinecraftFolderImportRuntime(XsrCommandRouter Commands, XsrQueryRouter Queries);

public static class MinecraftFolderImportRuntimeComposer
{
    public static MinecraftFolderImportRuntime Compose(FoundationHost host, MinecraftLibraryService library, MinecraftInstallService installer, IXsrDispatchObserver observer)
    {
        var service = new MinecraftFolderImportService(host.Tasks);
        var jars = new MinecraftLocalJarService(host.Tasks, host.StateStore, installer);
        XsrCommandRouterBuilder commands = new();
        commands.Register<MinecraftFolderImportCommand>(MinecraftFolderImportContract.Import, async (command, token) =>
        {
            var result = await service.ImportAsync(command, token).ConfigureAwait(false);
            if (result.IsSuccess) await library.RefreshAsync(token).ConfigureAwait(false);
            return result;
        });
        XsrQueryRouterBuilder queries = new();
        queries.Register<MinecraftModpackUpdateQuery, MinecraftModpackUpdatePreview>(MinecraftModpackUpdateContract.Preview, async (query, token) =>
        {
            try { return XsrResult.Success(await installer.PreviewModpackUpdateAsync(query, token).ConfigureAwait(false)); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or FormatException or OverflowException)
            { return XsrResult.Failure<MinecraftModpackUpdatePreview>(MinecraftErrors.InvalidRequest(error.Message)); }
        });
        commands.Register<MinecraftModpackUpdateCommand>(MinecraftModpackUpdateContract.Apply, async (command, token) =>
        {
            var result = await installer.UpdateModpackAsync(command, token).ConfigureAwait(false);
            if (result.IsSuccess) await library.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            return result.IsSuccess ? XsrResult.Success() : XsrResult.Failure(result.Error!);
        });
        commands.Register<MinecraftModpackCommand>(MinecraftModpackContract.Install, async (command, token) =>
        {
            var result = await installer.InstallModpackAsync(command, token).ConfigureAwait(false);
            if (result.IsSuccess) await library.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            return result.IsSuccess ? XsrResult.Success() : XsrResult.Failure(result.Error!);
        });
        commands.Register<MinecraftLocalJarCommand>(MinecraftLocalJarContract.Import, async (command, token) =>
        {
            var result = await jars.ImportAsync(command, token).ConfigureAwait(false);
            if (result.IsSuccess) await library.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            return result;
        });
        queries.Register<MinecraftFolderInspectQuery, MinecraftFolderInspection>(MinecraftFolderImportContract.Inspect, async (query, token) =>
        {
            try { return XsrResult.Success(await MinecraftFolderImportService.InspectAsync(query.Path, token).ConfigureAwait(false)); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or FormatException or OverflowException)
            { return XsrResult.Failure<MinecraftFolderInspection>(MinecraftErrors.InvalidRequest(error.Message)); }
        });
        return new(commands.Build(observer), queries.Build(observer));
    }
}
