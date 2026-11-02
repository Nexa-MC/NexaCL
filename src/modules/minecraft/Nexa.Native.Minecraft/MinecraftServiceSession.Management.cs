using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private async Task<byte[]> ExecuteManagementAsync(int operation, ServiceWireReader reader, CancellationToken token)
    {
        using var writer = new ServiceWireWriter();
        switch (operation)
        {
            case MinecraftServiceProtocol.ManageInstanceExportOptionsSave:
                {
                    var value = reader.InstanceExportOptionsSave() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceModpackExportService.SaveOptionsAsync(value, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceModpackExportCommand:
                {
                    var value = reader.InstanceModpackExportCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await new InstanceModpackExportService(_tasks, _store).ExportAsync(value, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceServerListSaveCommand:
                {
                    var value = reader.InstanceServerListSaveCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceServerListService.SaveAsync(value, _store, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceModEnabledCommand:
                {
                    var value = reader.InstanceModEnabledCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceContentService.SetModEnabledAsync(value, _store, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceContentRemoveCommand:
                {
                    var value = reader.InstanceContentRemoveCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceContentTrash.RemoveAsync(value, _store, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceModRemovalCommand:
                {
                    var value = reader.InstanceModRemovalCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceModRemovalService.RemoveAsync(value, _store, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceContentRestoreCommand:
                {
                    var value = reader.InstanceContentRestoreCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceContentTrash.RestoreAsync(value, _store, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceContentUpdateRollbackCommand:
                {
                    var value = reader.InstanceContentUpdateRollbackCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceContentUpdateTransaction.RollbackAsync(value, _store, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldCopyCommand:
                {
                    var value = reader.InstanceWorldCopyCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceWorldService.CopyAsync(value, _store, WorldSessionLock, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldBackupCommand:
                {
                    var value = reader.InstanceWorldBackupCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceWorldService.BackupAsync(value, _store, CaptureWorldAsync, WorldSessionLock, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldLockCommand:
                {
                    var value = reader.InstanceWorldLockCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceWorldService.SetLockAsync(value, _store, WorldSessionLock, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldDataPackCommand:
                {
                    var value = reader.InstanceWorldDataPackCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceWorldService.SetDataPackEnabledAsync(value, _store, WorldSessionLock, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldDataPackImportCommand:
                {
                    var value = reader.InstanceWorldDataPackImportCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceWorldService.ImportDataPackAsync(value, _store, WorldSessionLock, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldDataPackRemoveCommand:
                {
                    var value = reader.InstanceWorldDataPackRemoveCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceWorldService.RemoveDataPackAsync(value, _store, WorldSessionLock, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldDataPackRestoreCommand:
                {
                    var value = reader.InstanceWorldDataPackRestoreCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceWorldService.RestoreDataPackAsync(value, _store, WorldSessionLock, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceFileSaveCommand:
                {
                    var value = reader.InstanceFileSaveCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceFileWorkspaceService.SaveAsync(value, _store, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceIdentitySaveCommand:
                {
                    var value = reader.InstanceIdentitySaveCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceIdentityService.SaveAsync(value, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceScreenshotCropCommand:
                {
                    var value = reader.InstanceScreenshotCropCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await InstanceScreenshotService.CropWithEncoderAsync(value, _store, CropScreenshotAsync, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldSnapshotCaptureCommand:
                {
                    var value = reader.InstanceWorldSnapshotCaptureCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await WorldSnapshots.CaptureAsync(value, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldSnapshotRestoreCommand:
                {
                    var value = reader.InstanceWorldSnapshotRestoreCommand() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.XsrError((await WorldSnapshots.RestoreAsync(value, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.ManageInstanceContentIntegrityQuery:
                {
                    var value = reader.InstanceContentIntegrityQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceContentIntegrity(await InstanceContentIntegrityService.ReadAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldMetadataQuery:
                {
                    var value = reader.InstanceWorldMetadataQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceWorldMetadata(await InstanceWorldService.ReadAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldHealthQuery:
                {
                    var value = reader.InstanceWorldHealthQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceWorldHealth(await InstanceWorldService.ReadHealthAsync(value, _store, WorldSessionLock, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceScreenshotQuery:
                {
                    var value = reader.InstanceScreenshotQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceScreenshot(await InstanceScreenshotService.ReadAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceFileListQuery:
                {
                    var value = reader.InstanceFileListQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceFileListing(await InstanceFileWorkspaceService.ListAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceFileReadQuery:
                {
                    var value = reader.InstanceFileReadQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceFileDocument(await InstanceFileWorkspaceService.ReadAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceFileSavePreviewQuery:
                {
                    var value = reader.InstanceFileSavePreviewQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceFileSavePreview(await InstanceFileWorkspaceService.PreviewAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceIdentityQuery:
                {
                    var value = reader.InstanceIdentityQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceIdentitySnapshot(await InstanceIdentityService.ReadAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceModpackExportQuery:
                {
                    var value = reader.InstanceModpackExportQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceModpackExportPreview(await InstanceModpackExportService.PreviewAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceExportOptionsQuery:
                {
                    var value = reader.InstanceExportOptionsQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceExportOptions(await InstanceModpackExportService.LoadOptionsAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceServerListQuery:
                {
                    var value = reader.InstanceServerListQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceServerList(await InstanceServerListService.ReadAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceServerStatusQuery:
                {
                    var value = reader.InstanceServerStatusQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceServerStatus(await InstanceServerStatusService.ReadAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceModRemovalQuery:
                {
                    var value = reader.InstanceModRemovalQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceModRemovalPreview(await InstanceModRemovalService.PreviewAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceManagementQuery:
                {
                    var value = reader.InstanceManagementQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceManagementSnapshot(await ReadManagementAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceOfflineReadinessQuery:
                {
                    var value = reader.InstanceOfflineReadinessQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceOfflineReadinessReport(await ReadOfflineReadinessAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldSnapshotListQuery:
                {
                    var value = reader.InstanceWorldSnapshotListQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceWorldSnapshotListing(await ListWorldSnapshotsAsync(value, token).ConfigureAwait(false)); break;
                }
            case MinecraftServiceProtocol.ManageInstanceWorldSnapshotVerifyQuery:
                {
                    var value = reader.InstanceWorldSnapshotVerifyQuery() ?? throw new InvalidDataException("Missing management request."); reader.End();
                    writer.InstanceWorldSnapshotVerification(await WorldSnapshots.VerifyAsync(value, token).ConfigureAwait(false)); break;
                }
            default: throw new InvalidDataException("Unknown Minecraft management operation.");
        }
        return writer.Finish();
    }
}
