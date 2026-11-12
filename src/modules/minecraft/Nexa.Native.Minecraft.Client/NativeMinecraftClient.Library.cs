using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Minecraft.Client;

public sealed class NativeMinecraftInstanceSource(NativeMinecraftClient client) : IMinecraftInstanceSource
{
    public NativeMinecraftClient Client { get; } = client;
    public async ValueTask<IReadOnlyList<MinecraftInstanceDescriptor>> DiscoverAsync(string minecraftRootDirectory, CancellationToken cancellationToken = default)
        => await Client.InstancesAsync(minecraftRootDirectory, token: cancellationToken).ConfigureAwait(false);
}
public sealed partial class NativeMinecraftClient : IMinecraftLibraryPort
{
    private IMinecraftLibraryPersistence? _libraryPersistence;
    private IMinecraftInstanceSource? _librarySource;
    private bool _libraryAdmitted;
    public void AttachLibraryPersistence(IMinecraftLibraryPersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); if (_libraryAdmitted || _libraryPersistence is not null) throw new InvalidOperationException("Library persistence is already admitted."); _libraryPersistence = persistence; }
    }
    public void AttachLibrarySource(IMinecraftInstanceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); if (_libraryAdmitted || _librarySource is not null) throw new InvalidOperationException("Library source is already admitted."); _librarySource = source; }
    }
    public async Task ConfigureLibraryAsync(string directory, CancellationToken token = default)
    {
        bool reverse;
        lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); if (_libraryAdmitted || _libraryPersistence is null) throw new InvalidOperationException("Library persistence must be attached exactly once before admission."); _libraryAdmitted = true; reverse = _librarySource is not null; }
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.LibraryConfigure, writer => { writer.String(directory); writer.Boolean(reverse); }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    public Task<XsrResult> RefreshAsync(CancellationToken cancellationToken = default) => LaunchResultAsync(MinecraftServiceProtocol.LibraryRefresh, _ => { }, cancellationToken);
    public Task<XsrResult> ChangeLibraryDirectoryAsync(MinecraftLibraryDirectoryCommand value, CancellationToken token = default) => LaunchResultAsync(MinecraftServiceProtocol.LibraryDirectory, writer => writer.MinecraftLibraryDirectoryCommand(value), token);
    public Task<XsrResult> ForgetLibraryDirectoryAsync(MinecraftLibraryForgetCommand value, CancellationToken token = default) => LaunchResultAsync(MinecraftServiceProtocol.LibraryForget, writer => writer.MinecraftLibraryForgetCommand(value), token);
    public Task<XsrResult> SelectLibraryInstanceAsync(MinecraftLibrarySelectCommand value, CancellationToken token = default) => LaunchResultAsync(MinecraftServiceProtocol.LibrarySelect, writer => writer.MinecraftLibrarySelectCommand(value), token);
    public Task<XsrResult> RenameLibraryDirectoryAsync(MinecraftLibraryRenameCommand value, CancellationToken token = default) => LaunchResultAsync(MinecraftServiceProtocol.LibraryRename, writer => writer.MinecraftLibraryRenameCommand(value), token);
    public Task<XsrResult> CloneLibraryInstanceAsync(MinecraftLibraryCloneCommand value, CancellationToken token = default)
        => LaunchResultAsync(MinecraftServiceProtocol.LibraryClone, writer => writer.MinecraftLibraryCloneCommand(value), token);
    public Task<XsrResult> DeleteLibraryInstanceAsync(MinecraftLibraryDeleteCommand value, CancellationToken token = default) => LaunchResultAsync(MinecraftServiceProtocol.LibraryDelete, writer => writer.MinecraftLibraryDeleteCommand(value), token);
    public Task<XsrResult> RememberRenamedAsync(string root, string previous, string current, CancellationToken cancellationToken = default)
        => LaunchResultAsync(MinecraftServiceProtocol.LibraryRememberRenamed, writer => writer.MinecraftLibraryRememberRenamedCommand(new(root, previous, current)), cancellationToken);
    private async Task<byte[]> ExecuteLibraryHostAsync(int operation, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out _); using var writer = new ServiceWireWriter();
        var persistence = _libraryPersistence ?? throw new InvalidOperationException("Library persistence is not attached.");
        switch (operation)
        {
            case MinecraftHostOperations.LibraryRead:
                reader.End(); writer.MinecraftLibraryStoredConfiguration(await persistence.ReadAsync(token).ConfigureAwait(false)); break;
            case MinecraftHostOperations.LibrarySave:
                var command = reader.MinecraftLibrarySave() ?? throw new InvalidDataException("Missing library save."); reader.End();
                var saved = await persistence.SaveAsync(command, token).ConfigureAwait(false); writer.XsrError(saved.Error); if (saved.IsSuccess) writer.MinecraftLibraryStoredConfiguration(saved.Value); break;
            case MinecraftHostOperations.LibraryDiscover:
                string path = reader.String() ?? ""; reader.End(); var source = _librarySource ?? throw new InvalidOperationException("Library discovery source is not attached.");
                var instances = await source.DiscoverAsync(path, token).ConfigureAwait(false); writer.Count(instances.Count); foreach (var instance in instances) writer.MinecraftInstanceDescriptor(instance); break;
            case MinecraftHostOperations.LibraryCached:
                string directory = reader.String() ?? ""; reader.End(); writer.MinecraftInstanceDiscoverySnapshot(_librarySource is IMinecraftInstanceSnapshotSource cached ? await cached.LoadSnapshotAsync(directory, token).ConfigureAwait(false) : null); break;
            default: throw new InvalidDataException("Unknown library host operation.");
        }
        return writer.Finish();
    }
    private byte[] ApplyLibraryState(ServiceWireReader reader)
    {
        long revision = reader.Int64(); var availability = (XsrStateAvailability)reader.Int32(); bool hasValue = reader.Boolean();
        if (revision <= 0 || availability is not (XsrStateAvailability.Available or XsrStateAvailability.Unavailable or XsrStateAvailability.Stale)) throw new InvalidDataException("Invalid library state revision.");
        var value = hasValue ? reader.MinecraftLibrarySnapshot() ?? throw new InvalidDataException("Missing library state.") : default!; reader.End();
        if (_replicas is { } store) store.Replicate(store.Resolve(MinecraftLibraryContract.StateKey), revision, availability, hasValue, value); return [];
    }
}
