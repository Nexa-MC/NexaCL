using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private NativeMinecraftLibraryOwner? _library;
    private async Task<byte[]> ExecuteLibraryAsync(int operation, ServiceWireReader reader, CancellationToken token)
    {
        using var writer = new ServiceWireWriter();
        if (operation == MinecraftServiceProtocol.LibraryConfigure)
        {
            string fallback = reader.String() ?? ""; bool reverse = reader.Boolean(); reader.End();
            if (!Path.IsPathFullyQualified(fallback)) throw new InvalidDataException("Invalid library directory.");
            await _initialization.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing), this);
                if (_library is not null) throw new InvalidOperationException("Minecraft library is already configured.");
                var persistence = new LibraryPersistence(this);
                var saved = await persistence.ReadAsync(token).ConfigureAwait(false);
                _library = new(_store, reverse ? new LibrarySource(this) : _instances!, persistence, fallback, saved);
            }
            finally { _initialization.Release(); }
            return [];
        }
        var library = _library ?? throw new InvalidOperationException("Minecraft library is not configured.");
        XsrResult result;
        switch (operation)
        {
            case MinecraftServiceProtocol.LibraryRefresh:
                reader.End(); result = await library.RefreshAsync(token).ConfigureAwait(false); break;
            case MinecraftServiceProtocol.LibraryDirectory:
                var directory = reader.MinecraftLibraryDirectoryCommand() ?? throw new InvalidDataException("Missing library directory."); reader.End(); result = await library.ChangeDirectoryAsync(directory, token).ConfigureAwait(false); break;
            case MinecraftServiceProtocol.LibraryForget:
                var forget = reader.MinecraftLibraryForgetCommand() ?? throw new InvalidDataException("Missing library forget request."); reader.End(); result = await library.ForgetAsync(forget, token).ConfigureAwait(false); break;
            case MinecraftServiceProtocol.LibrarySelect:
                var select = reader.MinecraftLibrarySelectCommand() ?? throw new InvalidDataException("Missing library selection."); reader.End(); result = await library.SelectAsync(select, token).ConfigureAwait(false); break;
            case MinecraftServiceProtocol.LibraryRename:
                var rename = reader.MinecraftLibraryRenameCommand() ?? throw new InvalidDataException("Missing library name."); reader.End(); result = await library.RenameDirectoryAsync(rename, token).ConfigureAwait(false); break;
            case MinecraftServiceProtocol.LibraryClone:
                var clone = reader.MinecraftLibraryCloneCommand() ?? throw new InvalidDataException("Missing clone request."); reader.End(); result = await library.CloneAsync(clone, token).ConfigureAwait(false); break;
            case MinecraftServiceProtocol.LibraryDelete:
                var delete = reader.MinecraftLibraryDeleteCommand() ?? throw new InvalidDataException("Missing library deletion."); reader.End(); result = await library.DeleteAsync(delete, token).ConfigureAwait(false); break;
            case MinecraftServiceProtocol.LibraryRememberRenamed:
                var remembered = reader.MinecraftLibraryRememberRenamedCommand() ?? throw new InvalidDataException("Missing renamed instance."); reader.End(); result = await library.RememberRenamedAsync(remembered.Root, remembered.Previous, remembered.Current, token).ConfigureAwait(false); break;
            default: throw new InvalidDataException("Unknown Minecraft library operation.");
        }
        writer.XsrError(result.Error); return writer.Finish();
    }
    private bool PublishLibraryState(Nexa.Xsr.State.XsrStateChange change)
    {
        if (change.SemanticId != MinecraftLibraryContract.StateKey) return false;
        var captured = _store.Read<MinecraftLibrarySnapshot>(_store.Resolve(change.SemanticId));
        _publications.Post(47, captured.Revision, () =>
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftServiceProtocol.LibraryState);
            writer.Int64(captured.Revision); writer.Int32((int)captured.Availability); writer.Boolean(captured.HasValue);
            if (captured.HasValue) writer.MinecraftLibrarySnapshot(captured.Value); return writer.Finish();
        });
        return true;
    }
    private sealed class LibraryPersistence(MinecraftServiceSession owner) : IMinecraftLibraryPersistence
    {
        public async ValueTask<MinecraftLibraryStoredConfiguration> ReadAsync(CancellationToken token = default)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.LibraryRead);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); var value = reader.MinecraftLibraryStoredConfiguration() ?? throw new InvalidDataException("Missing library configuration."); reader.End(); return value;
        }
        public async ValueTask<XsrResult<MinecraftLibraryStoredConfiguration>> SaveAsync(MinecraftLibrarySave command, CancellationToken token = default)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.LibrarySave); writer.MinecraftLibrarySave(command);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
            var result = error is null ? XsrResult.Success(reader.MinecraftLibraryStoredConfiguration() ?? throw new InvalidDataException("Missing saved library configuration.")) : XsrResult.Failure<MinecraftLibraryStoredConfiguration>(error); reader.End(); return result;
        }
    }
    private sealed class LibrarySource(MinecraftServiceSession owner) : IMinecraftInstanceSnapshotSource
    {
        public async ValueTask<IReadOnlyList<MinecraftInstanceDescriptor>> DiscoverAsync(string path, CancellationToken token = default)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.LibraryDiscover); writer.String(path);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); int count = reader.Count(); if (count < 0) throw new InvalidDataException("Missing library discovery.");
            var result = new MinecraftInstanceDescriptor[count]; for (int i = 0; i < count; i++) result[i] = reader.MinecraftInstanceDescriptor() ?? throw new InvalidDataException("Missing instance."); reader.End(); return Array.AsReadOnly(result);
        }
        public ValueTask<IReadOnlyList<MinecraftInstanceDescriptor>> RefreshAsync(string path, CancellationToken cancellationToken = default) => DiscoverAsync(path, cancellationToken);
        public async ValueTask<MinecraftInstanceDiscoverySnapshot?> LoadSnapshotAsync(string path, CancellationToken token = default)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.LibraryCached); writer.String(path);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); var result = reader.MinecraftInstanceDiscoverySnapshot(); reader.End(); return result;
        }
    }
}
