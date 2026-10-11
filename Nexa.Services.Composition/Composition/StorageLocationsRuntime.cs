using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Services.Setup;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

internal static class StorageLocationsRuntime
{
    internal static void Register(XsrQueryRouterBuilder queries, FoundationHost host, StoragePreferencesService preferences)
    {
        var service = new StorageLocationsService(new(preferences.DataDirectory), host.CacheDirectory,
            host.JavaManagedRuntimeRoots, () =>
            {
                // One state read keeps paths and selected-instance identity in the same revision.
                var snapshot = host.StateStore.TryResolve(MinecraftLibraryContract.StateKey, out var state)
                    ? host.StateStore.Read<MinecraftLibrarySnapshot>(state).Value : null;
                return snapshot is null ? new(0, [], null, null)
                    : new(snapshot.Revision, snapshot.Directories.Select(directory => directory.Path).ToArray(),
                        snapshot.RootDirectory, snapshot.SelectedInstance?.DirectoryPath);
            });
        queries.Register<StorageLocationsQuery, StorageLocationsSnapshot>(StorageLocationsContract.Query,
            (_, token) => new(Task.Run(() => service.Read(token), token)));
    }
}
