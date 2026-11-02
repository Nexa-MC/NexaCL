using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient : IMinecraftInstallationPort
{
    public event Action<string>? Installed;
    public event Action<string, string, string>? Renamed;
    private long _installationEventSequence;
    private readonly object _installationEventGate = new();
    private byte[] ReceiveInstallationEvent(ReadOnlyMemory<byte> bytes)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out _); long sequence = reader.Int64();
        string root = reader.String() ?? throw new InvalidDataException("Missing installation event root.");
        string? previous = reader.String(), current = reader.String(); reader.End();
        lock (_installationEventGate)
        {
            if (sequence != _installationEventSequence + 1 || (previous is null) != (current is null))
                throw new InvalidDataException("Invalid installation event sequence.");
            _installationEventSequence = sequence;
        }
        if (previous is null)
        {
            if (Installed is { } installed)
                foreach (Action<string> observer in installed.GetInvocationList())
                    try { observer(root); } catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { Report(error); }
        }
        else if (Renamed is { } renamed)
            foreach (Action<string, string, string> observer in renamed.GetInvocationList())
                try { observer(root, previous, current!); } catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { Report(error); }
        return [];
    }
    public Task<XsrResult<MinecraftInstallResult>> InstallAsync(MinecraftInstallCommand command, CancellationToken cancellationToken = default)
        => InstallMinecraftAsync(command, cancellationToken);
    public async Task<MinecraftModpackUpdatePreview> PreviewModpackUpdateAsync(MinecraftModpackUpdateQuery query, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ModpackUpdatePreview, writer => writer.MinecraftModpackUpdateQuery(query), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var result = reader.MinecraftModpackUpdatePreview() ?? throw new InvalidDataException("Missing modpack update preview."); reader.End(); return result;
    }
    public async Task<XsrResult<MinecraftInstallResult>> UpdateModpackAsync(MinecraftModpackUpdateCommand command, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ModpackUpdate, writer => writer.MinecraftModpackUpdateCommand(command), token).ConfigureAwait(false); return ReadInstallation(bytes);
    }
    public async Task<MinecraftModpackPreview> InspectModpackAsync(string path, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ModpackInspect, writer => writer.String(path), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var result = reader.MinecraftModpackPreview() ?? throw new InvalidDataException("Missing modpack preview."); reader.End(); return result;
    }
    public async Task<MinecraftModpackIdentity?> ReadModpackIdentityAsync(MinecraftModpackIdentityQuery query, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ModpackIdentity, writer => writer.MinecraftModpackIdentityQuery(query), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var result = reader.MinecraftModpackIdentity(); reader.End(); return result;
    }
    public async Task PrepareMigrationAsync(string root, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.InstallMigration, writer => writer.String(root), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    public Task<XsrResult> ImportLocalJarAsync(MinecraftLocalJarCommand command, CancellationToken token = default)
        => LaunchResultAsync(MinecraftServiceProtocol.LocalJarImport, writer => writer.MinecraftLocalJarCommand(command), token);
    public Task<XsrResult> ImportFolderAsync(MinecraftFolderImportCommand command, CancellationToken token = default)
        => LaunchResultAsync(MinecraftServiceProtocol.FolderImport, writer => writer.MinecraftFolderImportCommand(command), token);
    public async Task<MinecraftFolderInspection> InspectFolderAsync(string path, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.FolderInspect, writer => writer.String(path), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var result = reader.MinecraftFolderInspection() ?? throw new InvalidDataException("Missing folder inspection."); reader.End(); return result;
    }
}
