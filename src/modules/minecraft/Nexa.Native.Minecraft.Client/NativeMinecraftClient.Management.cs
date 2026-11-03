using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    public async ValueTask<XsrResult<ContentInspectionSnapshot>> InspectContentAsync(ContentInspectionQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ContentInspect, writer => writer.ContentInspectionQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError();
        if (error is not null) { reader.End(); return XsrResult.Failure<ContentInspectionSnapshot>(error); }
        var result = reader.ContentInspectionSnapshot() ?? throw new InvalidDataException("Missing inspection result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<LanGameDiscoverySnapshot>> DiscoverLanGamesAsync(LanGameDiscoveryQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.LanDiscover, writer => writer.LanGameDiscoveryQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError();
        if (error is not null) { reader.End(); return XsrResult.Failure<LanGameDiscoverySnapshot>(error); }
        var result = reader.LanGameDiscoverySnapshot() ?? throw new InvalidDataException("Missing LAN result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult> ExportLaunchScriptAsync(InstanceLaunchScriptCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ScriptExport, writer => writer.InstanceLaunchScriptCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceExportOptionsSaveAsync(InstanceExportOptionsSave value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceExportOptionsSave, writer => writer.InstanceExportOptionsSave(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceModpackExportCommandAsync(InstanceModpackExportCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceModpackExportCommand, writer => writer.InstanceModpackExportCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceServerListSaveCommandAsync(InstanceServerListSaveCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceServerListSaveCommand, writer => writer.InstanceServerListSaveCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceModEnabledCommandAsync(InstanceModEnabledCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceModEnabledCommand, writer => writer.InstanceModEnabledCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceContentRemoveCommandAsync(InstanceContentRemoveCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceContentRemoveCommand, writer => writer.InstanceContentRemoveCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceModRemovalCommandAsync(InstanceModRemovalCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceModRemovalCommand, writer => writer.InstanceModRemovalCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceContentRestoreCommandAsync(InstanceContentRestoreCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceContentRestoreCommand, writer => writer.InstanceContentRestoreCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceContentUpdateRollbackCommandAsync(InstanceContentUpdateRollbackCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceContentUpdateRollbackCommand, writer => writer.InstanceContentUpdateRollbackCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceWorldCopyCommandAsync(InstanceWorldCopyCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldCopyCommand, writer => writer.InstanceWorldCopyCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceWorldBackupCommandAsync(InstanceWorldBackupCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldBackupCommand, writer => writer.InstanceWorldBackupCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceWorldLockCommandAsync(InstanceWorldLockCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldLockCommand, writer => writer.InstanceWorldLockCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceWorldDataPackCommandAsync(InstanceWorldDataPackCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldDataPackCommand, writer => writer.InstanceWorldDataPackCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceWorldDataPackImportCommandAsync(InstanceWorldDataPackImportCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldDataPackImportCommand, writer => writer.InstanceWorldDataPackImportCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceWorldDataPackRemoveCommandAsync(InstanceWorldDataPackRemoveCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldDataPackRemoveCommand, writer => writer.InstanceWorldDataPackRemoveCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceWorldDataPackRestoreCommandAsync(InstanceWorldDataPackRestoreCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldDataPackRestoreCommand, writer => writer.InstanceWorldDataPackRestoreCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceFileSaveCommandAsync(InstanceFileSaveCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceFileSaveCommand, writer => writer.InstanceFileSaveCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceIdentitySaveCommandAsync(InstanceIdentitySaveCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceIdentitySaveCommand, writer => writer.InstanceIdentitySaveCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceScreenshotCropCommandAsync(InstanceScreenshotCropCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceScreenshotCropCommand, writer => writer.InstanceScreenshotCropCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceWorldSnapshotCaptureCommandAsync(InstanceWorldSnapshotCaptureCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldSnapshotCaptureCommand, writer => writer.InstanceWorldSnapshotCaptureCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult> DispatchInstanceWorldSnapshotRestoreCommandAsync(InstanceWorldSnapshotRestoreCommand value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldSnapshotRestoreCommand, writer => writer.InstanceWorldSnapshotRestoreCommand(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<XsrResult<InstanceContentIntegrity>> QueryInstanceContentIntegrityQueryAsync(InstanceContentIntegrityQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceContentIntegrityQuery, writer => writer.InstanceContentIntegrityQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceContentIntegrity() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceWorldMetadata>> QueryInstanceWorldMetadataQueryAsync(InstanceWorldMetadataQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldMetadataQuery, writer => writer.InstanceWorldMetadataQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceWorldMetadata() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceWorldHealth>> QueryInstanceWorldHealthQueryAsync(InstanceWorldHealthQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldHealthQuery, writer => writer.InstanceWorldHealthQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceWorldHealth() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceScreenshot>> QueryInstanceScreenshotQueryAsync(InstanceScreenshotQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceScreenshotQuery, writer => writer.InstanceScreenshotQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceScreenshot() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceFileListing>> QueryInstanceFileListQueryAsync(InstanceFileListQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceFileListQuery, writer => writer.InstanceFileListQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceFileListing() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceFileDocument>> QueryInstanceFileReadQueryAsync(InstanceFileReadQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceFileReadQuery, writer => writer.InstanceFileReadQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceFileDocument() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceFileSavePreview>> QueryInstanceFileSavePreviewQueryAsync(InstanceFileSavePreviewQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceFileSavePreviewQuery, writer => writer.InstanceFileSavePreviewQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceFileSavePreview() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceIdentitySnapshot>> QueryInstanceIdentityQueryAsync(InstanceIdentityQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceIdentityQuery, writer => writer.InstanceIdentityQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceIdentitySnapshot() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceModpackExportPreview>> QueryInstanceModpackExportQueryAsync(InstanceModpackExportQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceModpackExportQuery, writer => writer.InstanceModpackExportQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceModpackExportPreview() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceExportOptions>> QueryInstanceExportOptionsQueryAsync(InstanceExportOptionsQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceExportOptionsQuery, writer => writer.InstanceExportOptionsQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceExportOptions() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceServerList>> QueryInstanceServerListQueryAsync(InstanceServerListQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceServerListQuery, writer => writer.InstanceServerListQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceServerList() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceServerStatus>> QueryInstanceServerStatusQueryAsync(InstanceServerStatusQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceServerStatusQuery, writer => writer.InstanceServerStatusQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceServerStatus() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceModRemovalPreview>> QueryInstanceModRemovalQueryAsync(InstanceModRemovalQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceModRemovalQuery, writer => writer.InstanceModRemovalQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceModRemovalPreview() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceManagementSnapshot>> QueryInstanceManagementQueryAsync(InstanceManagementQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceManagementQuery, writer => writer.InstanceManagementQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceManagementSnapshot() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceOfflineReadinessReport>> QueryInstanceOfflineReadinessQueryAsync(InstanceOfflineReadinessQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceOfflineReadinessQuery, writer => writer.InstanceOfflineReadinessQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceOfflineReadinessReport() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceWorldSnapshotListing>> QueryInstanceWorldSnapshotListQueryAsync(InstanceWorldSnapshotListQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldSnapshotListQuery, writer => writer.InstanceWorldSnapshotListQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceWorldSnapshotListing() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
    public async ValueTask<XsrResult<InstanceWorldSnapshotVerification>> QueryInstanceWorldSnapshotVerifyQueryAsync(InstanceWorldSnapshotVerifyQuery value, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ManageInstanceWorldSnapshotVerifyQuery, writer => writer.InstanceWorldSnapshotVerifyQuery(value), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.InstanceWorldSnapshotVerification() ?? throw new InvalidDataException("Missing management result."); reader.End(); return XsrResult.Success(result);
    }
}
