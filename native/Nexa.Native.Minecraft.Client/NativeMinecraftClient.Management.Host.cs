using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private Func<ReadOnlyMemory<byte>, int, int, int, int, byte[]>? _screenshotCrop;
    private Func<string, CancellationToken, Task>? _captureWorld;
    private IInstanceWorldSnapshotStore? _worldSnapshots;
    public void AttachManagementHost(Func<ReadOnlyMemory<byte>, int, int, int, int, byte[]> screenshotCrop,
        Func<string, CancellationToken, Task> captureWorld, IInstanceWorldSnapshotStore snapshots)
    {
        ArgumentNullException.ThrowIfNull(screenshotCrop); ArgumentNullException.ThrowIfNull(captureWorld); ArgumentNullException.ThrowIfNull(snapshots);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_screenshotCrop is not null) throw new InvalidOperationException("Minecraft management host is already attached.");
            _screenshotCrop = screenshotCrop; _captureWorld = captureWorld; _worldSnapshots = snapshots;
        }
    }
    private async Task<byte[]> ExecuteManagementHostAsync(int operation, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out _);
        using var writer = new ServiceWireWriter();
        switch (operation)
        {
            case MinecraftHostOperations.CaptureWorld:
                {
                    string path = reader.String() ?? throw new InvalidDataException("Missing world directory."); reader.End();
                    await (_captureWorld ?? throw new InvalidOperationException("World backup capability is not attached."))(path, token).ConfigureAwait(false); break;
                }
            case MinecraftHostOperations.CropScreenshot:
                {
                    byte[] source = reader.Buffer(); int x = reader.Int32(), y = reader.Int32(), width = reader.Int32(), height = reader.Int32(); reader.End();
                    var crop = _screenshotCrop ?? throw new InvalidOperationException("Screenshot capability is not attached.");
                    byte[] result = await Task.Run(() => crop(source, x, y, width, height), token).ConfigureAwait(false); writer.Buffer(result); break;
                }
            case MinecraftHostOperations.WorldSnapshotList:
                {
                    string scope = reader.String() ?? ""; reader.End();
                    var snapshots = _worldSnapshots ?? throw new InvalidOperationException("World snapshot capability is not attached.");
                    writer.InstanceWorldSnapshotListing(new(await snapshots.ListAsync(scope, token).ConfigureAwait(false))); break;
                }
            case MinecraftHostOperations.WorldSnapshotCapture:
                {
                    string scope = reader.String() ?? "", path = reader.String() ?? "", name = reader.String() ?? ""; reader.End();
                    var snapshots = _worldSnapshots ?? throw new InvalidOperationException("World snapshot capability is not attached.");
                    writer.InstanceWorldSnapshot(await snapshots.CaptureAsync(scope, path, name, token).ConfigureAwait(false)); break;
                }
            case MinecraftHostOperations.WorldSnapshotVerify:
                {
                    string scope = reader.String() ?? "", identity = reader.String() ?? ""; reader.End();
                    var snapshots = _worldSnapshots ?? throw new InvalidOperationException("World snapshot capability is not attached.");
                    writer.InstanceWorldSnapshotVerification(await snapshots.VerifyAsync(scope, identity, token).ConfigureAwait(false)); break;
                }
            case MinecraftHostOperations.WorldSnapshotRestore:
                {
                    string scope = reader.String() ?? "", identity = reader.String() ?? "", path = reader.String() ?? ""; reader.End();
                    var snapshots = _worldSnapshots ?? throw new InvalidOperationException("World snapshot capability is not attached.");
                    await snapshots.RestoreAsync(scope, identity, path, token).ConfigureAwait(false); break;
                }
            default: throw new InvalidDataException("Unknown management host capability.");
        }
        return writer.Finish();
    }
}
