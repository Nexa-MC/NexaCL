using Nexa.Services.Minecraft;
using Nexa.Services.Setup;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private async Task<LauncherMigrationResult> RunLauncherMigrationAsync(string kind, string[] selected,
        LauncherMigrationPreview? preview, LauncherMigrationReceipt? receipt, LauncherMigrationSourceKind sourceKind,
        string? source, string? destination, CancellationToken token)
    {
        if (kind is "source" or "destination" or "recover")
        {
            string? path = await _chooseLauncherMigrationDirectory!(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (path is null) return new(kind);
            if (kind == "recover") return new(kind, Receipt: await ReadLauncherMigrationReceiptAsync(path, token).ConfigureAwait(false));
            if (kind == "destination") path = Path.Combine(path, "Nexa-Migrated-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
            return new(kind, path);
        }
        if (kind == "preview")
        {
            var query = new LauncherMigrationQuery(sourceKind, source!, destination!);
            return new(kind, Preview: await LauncherMigrationQueryAsync<LauncherMigrationQuery, LauncherMigrationPreview>(LauncherMigrationContract.Preview, query, token).ConfigureAwait(false));
        }
        if (kind == "apply")
        {
            await LauncherMigrationCommandAsync(LauncherMigrationContract.Apply, new LauncherMigrationApplyCommand(preview!, selected), token).ConfigureAwait(false);
            receipt = await ReadLauncherMigrationReceiptAsync(preview!.Query.DestinationDirectory, token).ConfigureAwait(false);
        }
        if (kind is "apply" or "register")
        {
            try
            {
                await RegisterLauncherMigrationRootsAsync(receipt!, token).ConfigureAwait(false);
                return new(kind, Receipt: receipt, Registered: true);
            }
            catch (IOException)
            {
                token.ThrowIfCancellationRequested();
                // Copying has already committed. Retain the receipt and retry only registration.
                return new(kind, Receipt: receipt);
            }
        }
        if (kind is "undo" or "cleanup")
        {
            if (kind == "undo")
            {
                if (!LauncherMigrationHasRemainingLibraryRoot(receipt!)) throw new IOException(MigrationRaw("lastRoot"));
                await LauncherMigrationCommandAsync(LauncherMigrationContract.Undo, new LauncherMigrationUndoCommand(receipt!), token).ConfigureAwait(false);
            }
            Volatile.Write(ref _launcherMigrationCleanupActive, 1);
            try
            {
                bool complete = await ForgetLauncherMigrationRootsAsync(receipt!).ConfigureAwait(false);
                _launcherMigrationCleanupCompletions.Enqueue((receipt!, complete));
                return new(kind);
            }
            finally { Volatile.Write(ref _launcherMigrationCleanupActive, 0); }
        }
        throw new InvalidOperationException("Unknown launcher migration action.");
    }

    private async Task RegisterLauncherMigrationRootsAsync(LauncherMigrationReceipt receipt, CancellationToken token)
    {
        foreach (var instance in receipt.Instances)
        {
            await LauncherMigrationLibraryCommandAsync(MinecraftLibraryRoutes.Directory, new MinecraftLibraryDirectoryCommand(instance.MinecraftRootDirectory, true), token).ConfigureAwait(false);
            await LauncherMigrationLibraryCommandAsync(MinecraftLibraryRoutes.Rename, new MinecraftLibraryRenameCommand(instance.MinecraftRootDirectory, LauncherMigrationLibraryName(instance.Name)), token).ConfigureAwait(false);
            await LauncherMigrationLibraryCommandAsync(MinecraftLibraryRoutes.Select, new MinecraftLibrarySelectCommand(instance.MinecraftRootDirectory, instance.InstanceId), token).ConfigureAwait(false);
        }
        if (receipt.Instances.Count > 1)
        {
            var first = receipt.Instances[0];
            await LauncherMigrationLibraryCommandAsync(MinecraftLibraryRoutes.Directory, new MinecraftLibraryDirectoryCommand(first.MinecraftRootDirectory), token).ConfigureAwait(false);
        }
    }

    private static string LauncherMigrationLibraryName(string name)
    {
        name = name.Trim();
        if (name.Length <= 80) return name;
        int end = System.Globalization.StringInfo.ParseCombiningCharacters(name).Last(index => index <= 80);
        return name[..end];
    }

    private bool LauncherMigrationHasRemainingLibraryRoot(LauncherMigrationReceipt receipt)
    {
        if (!_store.TryResolve(MinecraftLibraryContract.StateKey, out var state)) return false;
        var library = _store.Read<MinecraftLibrarySnapshot>(state).Value;
        return library is not null && library.Directories.Any(directory => !receipt.Instances.Any(instance =>
            Nexa.Core.PathIdentity.Comparer.Equals(instance.MinecraftRootDirectory, directory.Path)));
    }

    private async Task<bool> ForgetLauncherMigrationRootsAsync(LauncherMigrationReceipt receipt)
    {
        bool complete = true;
        foreach (var instance in receipt.Instances)
        {
            if (!_store.TryResolve(MinecraftLibraryContract.StateKey, out var state)) { complete = false; continue; }
            var library = _store.Read<MinecraftLibrarySnapshot>(state).Value;
            if (library is null) { complete = false; continue; }
            if (!library.Directories.Any(directory => Nexa.Core.PathIdentity.Comparer.Equals(instance.MinecraftRootDirectory, directory.Path))) continue;
            try
            {
                // Disk deletion has committed: navigation cancellation cannot leave its registration behind.
                await LauncherMigrationLibraryCommandAsync(MinecraftLibraryRoutes.Forget,
                    new MinecraftLibraryForgetCommand(instance.MinecraftRootDirectory), CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException) { complete = false; }
        }
        return complete;
    }

    private Task<LauncherMigrationReceipt> ReadLauncherMigrationReceiptAsync(string directory, CancellationToken token)
        => LauncherMigrationQueryAsync<LauncherMigrationUndoQuery, LauncherMigrationReceipt>(LauncherMigrationContract.ReadReceipt, new(directory), token);

    private async Task<TReply> LauncherMigrationQueryAsync<TQuery, TReply>(XsrSemanticId semantic, TQuery query, CancellationToken token) where TQuery : notnull
    {
        if (!_queries.TryResolve(semantic, out var route)) throw new IOException(MigrationRaw("unavailable"));
        var result = await _queries.QueryAsync<TQuery, TReply>(route, query, cancellationToken: token).ConfigureAwait(false);
        if (!result.IsSuccess) { token.ThrowIfCancellationRequested(); throw new IOException(result.Error?.Message ?? MigrationRaw("failed")); }
        return result.Value!;
    }

    private async Task LauncherMigrationCommandAsync<TCommand>(XsrSemanticId semantic, TCommand command, CancellationToken token) where TCommand : notnull
    {
        if (!_commands.TryResolve(semantic, out var route)) throw new IOException(MigrationRaw("unavailable"));
        var result = await _commands.Dispatch(route, command, cancellationToken: token).Completion.ConfigureAwait(false);
        if (!result.IsSuccess) { token.ThrowIfCancellationRequested(); throw new IOException(result.Error?.Message ?? MigrationRaw("failed")); }
    }

    private async Task LauncherMigrationLibraryCommandAsync<TCommand>(XsrSemanticId semantic, TCommand command, CancellationToken token) where TCommand : notnull
    {
        if (_launcherMigrationLibrary is null || !_launcherMigrationLibrary.TryResolve(semantic, out var route)) throw new IOException(MigrationRaw("libraryUnavailable"));
        var result = await _launcherMigrationLibrary.Dispatch(route, command, cancellationToken: token).Completion.ConfigureAwait(false);
        if (!result.IsSuccess) { token.ThrowIfCancellationRequested(); throw new IOException(result.Error?.Message ?? MigrationRaw("libraryFailed")); }
    }
}
