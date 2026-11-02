using Nexa.Services.Files;
using Nexa.Services.Settings;
using Nexa.Services.Setup;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static void StorageLocationsUseConfiguredRootsWithoutCreatingDirectories()
    {
        string temp = Path.Combine(Path.GetTempPath(), "nexa-storage-paths-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(temp, "data"), game = Path.Combine(temp, "selected-game"),
            managed = Path.Combine(temp, "original-runtime"), cache = Path.Combine(temp, "configured-cache"),
            instance = Path.Combine(game, "versions", "1.20.1");
        string[] roots = [managed, managed + Path.DirectorySeparatorChar];
        string[] libraries = [game, Path.Combine(temp, "another-game")];
        var service = new StorageLocationsService(new(data), cache, roots, () => new(7, libraries, game, instance));
        roots[0] = Path.Combine(temp, "mutated-runtime");
        var result = service.Read(); AssertTrue(result.IsSuccess);
        StorageLocationsSnapshot locations = result.Value!;
        AssertEqual(7L, locations.LibraryRevision); AssertEqual(data, locations.DataDirectory);
        AssertEqual(Path.Combine(data, "settings"), locations.SettingsDirectory); AssertEqual(Path.Combine(data, "logs"), locations.LogDirectory);
        AssertEqual(cache, locations.CacheDirectory); AssertEqual(managed, locations.JavaRuntimeDirectories.Single());
        AssertEqual(game, locations.ActiveMinecraftDirectory); AssertEqual(Path.Combine(game, "versions"), locations.InstancesDirectory);
        AssertEqual(instance, locations.SelectedInstanceDirectory); AssertEqual(2, locations.MinecraftLibraryDirectories.Count);
        libraries[0] = Path.Combine(temp, "mutated-game");
        AssertEqual(game, locations.MinecraftLibraryDirectories[0]);
        AssertFalse(Directory.Exists(temp));
    }

    private static void StorageLocationsFollowSelectedRootAndPreserveMissingConfiguration()
    {
        string temp = Path.Combine(Path.GetTempPath(), "nexa-storage-selected-" + Guid.NewGuid().ToString("N"));
        StorageMinecraftSelection selection = new(3, [], null, null);
        var service = new StorageLocationsService(new(Path.Combine(temp, "data")), null, [], () => selection);
        var empty = service.Read().Value!;
        AssertTrue(empty.CacheDirectory is null); AssertEqual(0, empty.JavaRuntimeDirectories.Count);
        AssertTrue(empty.ActiveMinecraftDirectory is null); AssertTrue(empty.InstancesDirectory is null); AssertTrue(empty.SelectedInstanceDirectory is null);
        string first = Path.Combine(temp, "first"), second = Path.Combine(temp, "second");
        selection = new(4, [first, second], first, Path.Combine(first, "versions", "same-id"));
        var one = service.Read().Value!;
        selection = new(5, [first, second], second, Path.Combine(second, "versions", "same-id"));
        var two = service.Read().Value!;
        AssertEqual(4L, one.LibraryRevision); AssertEqual(5L, two.LibraryRevision);
        AssertEqual(Path.Combine(second, "versions", "same-id"), two.SelectedInstanceDirectory);
        AssertEqual(0, two.JavaRuntimeDirectories.Count); AssertTrue(two.CacheDirectory is null);
        AssertFalse(Directory.Exists(temp));
    }

    private static void StorageLocationsRejectForeignSelectionAndHonorCancellation()
    {
        string temp = Path.Combine(Path.GetTempPath(), "nexa-storage-invalid-" + Guid.NewGuid().ToString("N"));
        string game = Path.Combine(temp, "game");
        var service = new StorageLocationsService(new(temp), null, [], () => new(2, [game], game,
            Path.Combine(temp, "other", "versions", "same-id")));
        AssertFalse(service.Read().IsSuccess);
        int invoked = 0;
        var cancellable = new StorageLocationsService(new(temp), null, [], () => { invoked++; return new(2, [game], game, null); });
        using var stop = new CancellationTokenSource(); stop.Cancel();
        AssertFalse(cancellable.Read(stop.Token).IsSuccess); AssertEqual(0, invoked);
        AssertFalse(Directory.Exists(temp));
    }

    private static void StorageLocationsCatalogDeclaresOnlyDeliveredReadOnlyFacts()
    {
        var catalog = SettingsCatalog.Read(new());
        foreach (string id in new[] { "global.storage.f0e1f8e49c73", "global.storage.515eb03d238d", "global.storage.245e94a31c8f", "global.storage.c0888e611264" })
        {
            var entry = catalog.Entries.Single(item => item.Id == id);
            AssertEqual(SettingsCatalogEntryKind.State, entry.Kind);
            AssertEqual(SettingsCapabilityAvailability.Available, entry.Availability);
            AssertTrue(entry.SettingKey is null);
        }
        foreach (string id in new[] { "global.storage.52f5b9975060", "global.storage.aa43fd2c91cc" })
            AssertEqual(SettingsCapabilityAvailability.NotImplemented, catalog.Entries.Single(item => item.Id == id).Availability);
    }
}
