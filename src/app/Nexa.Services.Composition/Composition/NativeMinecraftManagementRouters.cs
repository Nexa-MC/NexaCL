#if NEXA_NATIVE_SERVICES
using Nexa.Native.Minecraft.Client;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
namespace Nexa.Services.Composition;
internal static class NativeMinecraftManagementRouters
{
    internal static void Register(XsrCommandRouterBuilder commands, XsrQueryRouterBuilder queries, NativeMinecraftClient client)
    {
        commands.Register<InstanceLaunchScriptCommand>(InstanceLaunchScriptContract.Export, client.ExportLaunchScriptAsync);
        commands.Register<InstanceExportOptionsSave>(InstanceModpackExportContract.SaveOptions, client.DispatchInstanceExportOptionsSaveAsync);
        commands.Register<InstanceModpackExportCommand>(InstanceModpackExportContract.Export, client.DispatchInstanceModpackExportCommandAsync);
        commands.Register<InstanceServerListSaveCommand>(InstanceServerListContract.Save, client.DispatchInstanceServerListSaveCommandAsync);
        commands.Register<InstanceModEnabledCommand>(InstanceManagementContract.SetModEnabled, client.DispatchInstanceModEnabledCommandAsync);
        commands.Register<InstanceContentRemoveCommand>(InstanceManagementContract.RemoveContent, client.DispatchInstanceContentRemoveCommandAsync);
        commands.Register<InstanceModRemovalCommand>(InstanceManagementContract.RemoveMod, client.DispatchInstanceModRemovalCommandAsync);
        commands.Register<InstanceContentRestoreCommand>(InstanceManagementContract.RestoreContent, client.DispatchInstanceContentRestoreCommandAsync);
        commands.Register<InstanceContentUpdateRollbackCommand>(InstanceManagementContract.RollbackContentUpdate, client.DispatchInstanceContentUpdateRollbackCommandAsync);
        commands.Register<InstanceWorldCopyCommand>(InstanceWorldContract.Copy, client.DispatchInstanceWorldCopyCommandAsync);
        commands.Register<InstanceWorldBackupCommand>(InstanceWorldContract.Backup, client.DispatchInstanceWorldBackupCommandAsync);
        commands.Register<InstanceWorldLockCommand>(InstanceWorldContract.SetLock, client.DispatchInstanceWorldLockCommandAsync);
        commands.Register<InstanceWorldDataPackCommand>(InstanceWorldContract.SetDataPackEnabled, client.DispatchInstanceWorldDataPackCommandAsync);
        commands.Register<InstanceWorldDataPackImportCommand>(InstanceWorldContract.ImportDataPack, client.DispatchInstanceWorldDataPackImportCommandAsync);
        commands.Register<InstanceWorldDataPackRemoveCommand>(InstanceWorldContract.RemoveDataPack, client.DispatchInstanceWorldDataPackRemoveCommandAsync);
        commands.Register<InstanceWorldDataPackRestoreCommand>(InstanceWorldContract.RestoreDataPack, client.DispatchInstanceWorldDataPackRestoreCommandAsync);
        commands.Register<InstanceFileSaveCommand>(InstanceFileWorkspaceContract.Save, client.DispatchInstanceFileSaveCommandAsync);
        commands.Register<InstanceIdentitySaveCommand>(InstanceIdentityContract.Save, client.DispatchInstanceIdentitySaveCommandAsync);
        commands.Register<InstanceScreenshotCropCommand>(InstanceScreenshotContract.Crop, client.DispatchInstanceScreenshotCropCommandAsync);
        commands.Register<InstanceWorldSnapshotCaptureCommand>(InstanceWorldSnapshotContract.Capture, client.DispatchInstanceWorldSnapshotCaptureCommandAsync);
        commands.Register<InstanceWorldSnapshotRestoreCommand>(InstanceWorldSnapshotContract.Restore, client.DispatchInstanceWorldSnapshotRestoreCommandAsync);
        queries.Register<InstanceContentIntegrityQuery, InstanceContentIntegrity>(InstanceContentIntegrityContract.Read, client.QueryInstanceContentIntegrityQueryAsync);
        queries.Register<InstanceWorldMetadataQuery, InstanceWorldMetadata>(InstanceWorldContract.Read, client.QueryInstanceWorldMetadataQueryAsync);
        queries.Register<InstanceWorldHealthQuery, InstanceWorldHealth>(InstanceWorldHealthContract.Read, client.QueryInstanceWorldHealthQueryAsync);
        queries.Register<InstanceScreenshotQuery, InstanceScreenshot>(InstanceScreenshotContract.Read, client.QueryInstanceScreenshotQueryAsync);
        queries.Register<InstanceFileListQuery, InstanceFileListing>(InstanceFileWorkspaceContract.List, client.QueryInstanceFileListQueryAsync);
        queries.Register<InstanceFileReadQuery, InstanceFileDocument>(InstanceFileWorkspaceContract.Read, client.QueryInstanceFileReadQueryAsync);
        queries.Register<InstanceFileSavePreviewQuery, InstanceFileSavePreview>(InstanceFileWorkspaceContract.Preview, client.QueryInstanceFileSavePreviewQueryAsync);
        queries.Register<InstanceIdentityQuery, InstanceIdentitySnapshot>(InstanceIdentityContract.Query, client.QueryInstanceIdentityQueryAsync);
        queries.Register<InstanceModpackExportQuery, InstanceModpackExportPreview>(InstanceModpackExportContract.Preview, client.QueryInstanceModpackExportQueryAsync);
        queries.Register<InstanceExportOptionsQuery, InstanceExportOptions>(InstanceModpackExportContract.LoadOptions, client.QueryInstanceExportOptionsQueryAsync);
        queries.Register<ContentInspectionQuery, ContentInspectionSnapshot>(ContentInspectionContract.Read, client.InspectContentAsync);
        queries.Register<LanGameDiscoveryQuery, LanGameDiscoverySnapshot>(LanGameDiscoveryContract.Discover, client.DiscoverLanGamesAsync);
        queries.Register<InstanceServerListQuery, InstanceServerList>(InstanceServerListContract.Read, client.QueryInstanceServerListQueryAsync);
        queries.Register<InstanceServerStatusQuery, InstanceServerStatus>(InstanceServerListContract.Status, client.QueryInstanceServerStatusQueryAsync);
        queries.Register<InstanceModRemovalQuery, InstanceModRemovalPreview>(InstanceManagementContract.ModRemovalPreview, client.QueryInstanceModRemovalQueryAsync);
        queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query, client.QueryInstanceManagementQueryAsync);
        queries.Register<InstanceOfflineReadinessQuery, InstanceOfflineReadinessReport>(InstanceOfflineReadinessContract.Query,
            (value, token) => RecoverQueryAsync(() => client.QueryInstanceOfflineReadinessQueryAsync(value, token),
                new(XsrErrorKind.Rejected, XsrSemanticId.Parse("minecraft.instance.offline-readiness.rejected"), "无法检查本地离线依赖，请检查版本目录和权限。"), token));
        queries.Register<InstanceWorldSnapshotListQuery, IReadOnlyList<InstanceWorldSnapshot>>(InstanceWorldSnapshotContract.List, async (value, token) =>
        { var result = await RecoverQueryAsync(() => client.QueryInstanceWorldSnapshotListQueryAsync(value, token), SnapshotReadFailure(), token).ConfigureAwait(false); return result.IsSuccess ? XsrResult.Success(result.Value!.Items) : XsrResult.Failure<IReadOnlyList<InstanceWorldSnapshot>>(result.Error!); });
        queries.Register<InstanceWorldSnapshotVerifyQuery, InstanceWorldSnapshotVerification>(InstanceWorldSnapshotContract.Verify,
            (value, token) => RecoverQueryAsync(() => client.QueryInstanceWorldSnapshotVerifyQueryAsync(value, token), SnapshotReadFailure(), token));
    }
    private static XsrError SnapshotReadFailure() => Nexa.Services.Minecraft.MinecraftErrors.InvalidRequest("无法读取此世界的快照，请检查世界目录和权限。");
    private static async ValueTask<XsrResult<T>> RecoverQueryAsync<T>(Func<ValueTask<XsrResult<T>>> query, XsrError failure, CancellationToken token)
    {
        try { return await query().ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return XsrResult.Failure<T>(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { return XsrResult.Failure<T>(failure); }
    }
}
#endif
