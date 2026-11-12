using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private XsrCommandRouter? _folderCommands;
    public void BindContentImport(XsrCommandRouter commands) => _folderCommands = commands;
    private IEnumerable<HostCapabilityRoute> ContentTransferRoutes => [
        HostRoutes.Typed<ContentTransfer, OperationRef>("Minecraft.Content", "Import", async (c, v) =>
        {
            if (v.Format.Value != "nexa:content/mods" || v.Selection.Length != 0) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var instance = FindInstance(c, v.Instance);
            string path = Application.Files.Resolve(c.Identity, v.File);
            var inspection = await Query<MinecraftFolderInspectQuery, MinecraftFolderInspection>(_folderQueries, MinecraftFolderImportContract.Inspect, new(path), c.Options.Cancellation).ConfigureAwait(false);
            if (inspection.Kind != MinecraftFolderKind.Jar || inspection.Jar is not { } artifact) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            return Services.StartHostOperation(c, "nexa.minecraft.content.write", Abi100Schemas.Empty, async token =>
            {
                await Confirm(c, "content/import", "Import " + Path.GetFileName(path) + " into " + instance.Summary.Name, ContentTransfer.Write(v), token).ConfigureAwait(false);
                Application.Files.Resolve(c.Identity, v.File);
                if (FindInstance(c, v.Instance).Summary.Revision != instance.Summary.Revision) throw new AbiHostException(AbiErrors.CONFLICT);
                await Command(_folderCommands, MinecraftLocalJarContract.Import, new MinecraftLocalJarCommand(artifact, instance.Snapshot.RootDirectory, instance.Instance.Id, LocalJarAction.Mod), token).ConfigureAwait(false);
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            });
        }),
        HostRoutes.Typed<ContentTransfer, OperationRef>("Minecraft.Content", "Export", async (c, v) =>
        {
            var format = v.Format.Value switch
            {
                "nexa:format/mrpack" => InstanceExportFormat.Mrpack, "nexa:format/zip" => InstanceExportFormat.Zip,
                "nexa:format/curseforge" => InstanceExportFormat.CurseForge, "nexa:format/mcbbs" => InstanceExportFormat.Mcbbs,
                "nexa:format/multimc" => InstanceExportFormat.MultiMC, _ => throw new AbiHostException(AbiErrors.SCHEMA_INVALID)
            };
            if (v.Selection.Length > 256 || v.Selection.Distinct().Count() != v.Selection.Length) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var instance = FindInstance(c, v.Instance);
            string destination = Application.Files.Resolve(c.Identity, v.File, true);
            if (Path.Exists(destination)) throw new AbiHostException(AbiErrors.CONFLICT);
            var preview = await Query<InstanceModpackExportQuery, InstanceModpackExportPreview>(foundationQueries, InstanceModpackExportContract.Preview, new(instance.Instance.DirectoryPath), c.Options.Cancellation).ConfigureAwait(false);
            var selected = new List<(string Path, bool Directory)>();
            foreach (var id in v.Selection)
            {
                var item = await FindContent(c, id, c.Options.Cancellation).ConfigureAwait(false);
                if (item.Identity.Instance != v.Instance) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                selected.Add((item.Identity.Page + "/" + item.Identity.Name, item.Entry.IsDirectory));
            }
            string[] excluded = v.Selection.Length == 0 ? [] : preview.Files.Where(file => !selected.Any(item => file.Path == item.Path || item.Directory && file.Path.StartsWith(item.Path + "/", StringComparison.Ordinal))).Select(file => file.Path).ToArray();
            if (selected.Any(item => !preview.Files.Any(file => file.Path == item.Path || item.Directory && file.Path.StartsWith(item.Path + "/", StringComparison.Ordinal)))) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            return Services.StartHostOperation(c, "nexa.minecraft.content.write", Abi100Schemas.FileRef, async token =>
            {
                await Confirm(c, "content/export", "Export " + instance.Summary.Name + " as " + format + " to " + destination, ContentTransfer.Write(v), token).ConfigureAwait(false);
                Application.Files.Resolve(c.Identity, v.File, true);
                if (Path.Exists(destination) || FindInstance(c, v.Instance).Summary.Revision != instance.Summary.Revision) throw new AbiHostException(AbiErrors.CONFLICT);
                string stage = Path.Combine(Path.GetDirectoryName(destination)!, ".nexa-plugin-export-" + Guid.NewGuid().ToString("N") + (format == InstanceExportFormat.Mrpack ? ".mrpack" : ".zip"));
                try
                {
                    await Command(foundationCommands, InstanceModpackExportContract.Export, new InstanceModpackExportCommand(instance.Instance.DirectoryPath, stage, instance.Summary.Name, "1.0", preview.Categories.Select(p => p.Id).ToArray())
                    { Format = format, ExcludedPaths = excluded, IncludedPaths = v.Selection.Length == 0 ? null : preview.Files.Where(file => !excluded.Contains(file.Path, StringComparer.Ordinal)).Select(file => file.Path).ToArray() }, token).ConfigureAwait(false);
                    await Application.Files.MutationGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        Application.Files.Resolve(c.Identity, v.File, true); token.ThrowIfCancellationRequested();
                        if (Path.Exists(destination)) throw new AbiHostException(AbiErrors.CONFLICT);
                        File.Move(stage, destination, false);
                    }
                    finally { Application.Files.MutationGate.Release(); }
                    return AbiReply<AbiValue>.Ok(FileRef.Write(v.File));
                }
                finally { if (File.Exists(stage)) File.Delete(stage); }
            });
        })
    ];
}
