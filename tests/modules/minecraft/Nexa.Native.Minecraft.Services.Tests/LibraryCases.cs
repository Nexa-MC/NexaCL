using Nexa.Native.Minecraft.Client;
using Nexa.Services.Minecraft;
using Nexa.Xsr;
using Nexa.Xsr.State;

internal static class LibraryCases
{
    internal sealed class Persistence : IMinecraftLibraryPersistence
    {
        private readonly object _gate = new();
        private int _revision;
        internal int Writes;
        internal MinecraftLibraryConfiguration? Configuration;
        public ValueTask<MinecraftLibraryStoredConfiguration> ReadAsync(CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); lock (_gate) return ValueTask.FromResult(new MinecraftLibraryStoredConfiguration(_revision.ToString(System.Globalization.CultureInfo.InvariantCulture), Configuration)); }
        public async ValueTask<XsrResult<MinecraftLibraryStoredConfiguration>> SaveAsync(MinecraftLibrarySave command, CancellationToken token = default)
        {
            await Task.Yield(); token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (command.ExpectedRevision != _revision.ToString(System.Globalization.CultureInfo.InvariantCulture)) return XsrResult.Failure<MinecraftLibraryStoredConfiguration>(MinecraftErrors.InvalidRequest("stale library configuration"));
                Configuration = command.Configuration; Writes++; _revision++;
                return XsrResult.Success(new MinecraftLibraryStoredConfiguration(_revision.ToString(System.Globalization.CultureInfo.InvariantCulture), Configuration));
            }
        }
    }
    internal sealed class Source(NativeMinecraftClient client) : IMinecraftInstanceSnapshotSource
    {
        internal string? Hold;
        internal int Active;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<IReadOnlyList<MinecraftInstanceDescriptor>> DiscoverAsync(string minecraftRootDirectory, CancellationToken cancellationToken = default)
        {
            if (minecraftRootDirectory == Hold)
            {
                Interlocked.Increment(ref Active); Entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                finally { Interlocked.Decrement(ref Active); }
            }
            return await client.InstancesAsync(minecraftRootDirectory, refresh: true, token: cancellationToken).ConfigureAwait(false);
        }
        public ValueTask<IReadOnlyList<MinecraftInstanceDescriptor>> RefreshAsync(string minecraftRootDirectory, CancellationToken cancellationToken = default) => DiscoverAsync(minecraftRootDirectory, cancellationToken);
        public ValueTask<MinecraftInstanceDiscoverySnapshot?> LoadSnapshotAsync(string minecraftRootDirectory, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult<MinecraftInstanceDiscoverySnapshot?>(null); }
    }
    internal static async Task RunAsync(NativeMinecraftClient client, XsrStateStore replicas, string root, string instance, Persistence persistence, Source source, CancellationToken token)
    {
        if (!(await client.RefreshAsync(token)).IsSuccess) throw new InvalidDataException("Native library refresh failed.");
        MinecraftLibrarySnapshot Read() => replicas.Read<MinecraftLibrarySnapshot>(replicas.Resolve(MinecraftLibraryContract.StateKey), token).Value!;
        var snapshot = Read();
        if (snapshot.RootDirectory != root || snapshot.SelectedInstanceId != "renamed-game" || snapshot.IsLoading || snapshot.IsProvisional
            || persistence.Configuration?.Directories.Single().SelectedInstanceId != "renamed-game")
            throw new InvalidDataException("Native library lost durable renamed selection or acknowledged before state replication.");
        if ((await client.SelectLibraryInstanceAsync(new(root, "missing"), token)).IsSuccess)
            throw new InvalidDataException("Native library accepted an undiscovered selection.");
        if (!(await client.RenameLibraryDirectoryAsync(new(root, "Native games"), token)).IsSuccess || Read().Directories.Single().Name != "Native games")
            throw new InvalidDataException("Native library lost its durable display name.");
        string second = Path.Combine(Path.GetDirectoryName(root)!, "held-library"); Directory.CreateDirectory(second);
        source.Hold = second; source.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<XsrResult> old = client.ChangeLibraryDirectoryAsync(new(second, true), token);
        await source.Entered.Task.WaitAsync(token);
        if (!Read().IsLoading || (await client.DeleteLibraryInstanceAsync(new(second, "anything"), token)).IsSuccess)
            throw new InvalidDataException("Native library allowed deletion before authoritative discovery.");
        if (!(await client.ChangeLibraryDirectoryAsync(new(root), token)).IsSuccess || (await old).IsSuccess
            || Read().RootDirectory != root || Read().SelectedInstanceId != "renamed-game" || source.Active != 0)
            throw new InvalidDataException("A retired native library scan overwrote the newer directory.");
        if (!(await client.ForgetLibraryDirectoryAsync(new(second + Path.DirectorySeparatorChar + "."), token)).IsSuccess
            || (await client.ForgetLibraryDirectoryAsync(new(root), token)).IsSuccess)
            throw new InvalidDataException("Native library lost normalized forget or last-directory protection.");
        string removable = Path.Combine(root, "versions", "native-remove"); Directory.CreateDirectory(removable);
        await File.WriteAllTextAsync(Path.Combine(removable, "native-remove.json"), "{\"id\":\"native-remove\",\"mainClass\":\"fixture.Main\",\"type\":\"release\"}", token);
        await File.WriteAllTextAsync(Path.Combine(removable, "native-remove.jar"), "recycle sentinel", token);
        if (!(await client.RefreshAsync(token)).IsSuccess || !(await client.DeleteLibraryInstanceAsync(new(root, "native-remove"), token)).IsSuccess
            || Directory.Exists(removable) || !Directory.EnumerateFiles(Path.Combine(root, ".recycle"), "native-remove.jar", SearchOption.AllDirectories).Any()
            || Read().SelectedInstanceId != "renamed-game" || !Directory.Exists(instance))
            throw new InvalidDataException("Native library deletion lost recycling or changed another selection.");
        Console.WriteLine("Native library preserved durable selection, async generation cancellation, normalized directory edits and instance recycling.");
    }
}
