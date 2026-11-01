using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Install;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private MinecraftLocalJarService? _localJars;
    private MinecraftFolderImportService? _folderImports;
    private async Task<byte[]> ExecuteImportAsync(int operation, ServiceWireReader reader, CancellationToken token)
    {
        var installation = _installation ?? throw new InvalidOperationException("Minecraft installation is not configured.");
        using var writer = new ServiceWireWriter();
        switch (operation)
        {
            case MinecraftServiceProtocol.ModpackUpdatePreview:
                var preview = reader.MinecraftModpackUpdateQuery() ?? throw new InvalidDataException("Missing modpack update query."); reader.End();
                writer.MinecraftModpackUpdatePreview(await installation.PreviewModpackUpdateAsync(preview, token).ConfigureAwait(false)); break;
            case MinecraftServiceProtocol.ModpackUpdate:
                var command = reader.MinecraftModpackUpdateCommand() ?? throw new InvalidDataException("Missing modpack update."); reader.End();
                var result = await installation.UpdateModpackAsync(command, token).ConfigureAwait(false);
                writer.XsrError(result.Error); if (result.IsSuccess) writer.MinecraftInstallResult(result.Value); break;
            case MinecraftServiceProtocol.ModpackInspect:
                string archive = reader.String() ?? ""; reader.End(); writer.MinecraftModpackPreview(await installation.InspectModpackAsync(archive, token).ConfigureAwait(false)); break;
            case MinecraftServiceProtocol.ModpackIdentity:
                var identity = reader.MinecraftModpackIdentityQuery() ?? throw new InvalidDataException("Missing modpack identity query."); reader.End();
                writer.MinecraftModpackIdentity(await installation.ReadModpackIdentityAsync(identity, token).ConfigureAwait(false)); break;
            case MinecraftServiceProtocol.InstallMigration:
                string root = reader.String() ?? ""; reader.End(); await installation.PrepareMigrationAsync(root, token).ConfigureAwait(false); break;
            case MinecraftServiceProtocol.LocalJarImport:
                var jar = reader.MinecraftLocalJarCommand() ?? throw new InvalidDataException("Missing local jar command."); reader.End();
                writer.XsrError((await _localJars!.ImportAsync(jar, token).ConfigureAwait(false)).Error); break;
            case MinecraftServiceProtocol.FolderInspect:
                string path = reader.String() ?? ""; reader.End(); writer.MinecraftFolderInspection(await MinecraftFolderImportService.InspectAsync(path, token).ConfigureAwait(false)); break;
            case MinecraftServiceProtocol.FolderImport:
                var folder = reader.MinecraftFolderImportCommand() ?? throw new InvalidDataException("Missing folder import."); reader.End();
                writer.XsrError((await _folderImports!.ImportAsync(folder, token).ConfigureAwait(false)).Error); break;
            default: throw new InvalidDataException("Unknown Minecraft import operation.");
        }
        return writer.Finish();
    }
}
