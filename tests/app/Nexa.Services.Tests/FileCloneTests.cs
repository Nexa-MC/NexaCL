using Nexa.Platform;
using Nexa.Services.Setup;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static void SteamShortcutsPreserveEntriesAndRejectCorruption()
    {
        byte[] foreign = [0, .. System.Text.Encoding.UTF8.GetBytes("shortcuts\0"), 0, (byte)'0', 0, 2,
            .. System.Text.Encoding.UTF8.GetBytes("appid\0"), 42, 0, 0, 0, 1,
            .. System.Text.Encoding.UTF8.GetBytes("appname\0Other Game\0"), 8, 8, 8];
        var preserved = PlatformSteamShortcut.Update(foreign, "/opt/NexaCL", false);
        AssertTrue(preserved.AsSpan(0, foreign.Length - 2).SequenceEqual(foreign.AsSpan(0, foreign.Length - 2)));
        var first = PlatformSteamShortcut.Update([], "/opt/Nexa CL/Nexa.Desktop", false);
        AssertTrue(first.AsSpan().SequenceEqual(PlatformSteamShortcut.Update(first, "/opt/Nexa CL/Nexa.Desktop", false)));
        var both = PlatformSteamShortcut.Update(first, "/opt/Nexa CL/Nexa.Desktop", true);
        string text = System.Text.Encoding.UTF8.GetString(both);
        AssertTrue(text.Contains("NexaCL (Gamescope)", StringComparison.Ordinal));
        AssertTrue(text.Contains("/opt/Nexa CL/Nexa.Desktop", StringComparison.Ordinal));
        AssertTrue(both.AsSpan().SequenceEqual(PlatformSteamShortcut.Update(both, "/opt/Nexa CL/Nexa.Desktop", true)));
        bool rejected = false;
        try { PlatformSteamShortcut.Update(first[..^1], "/opt/NexaCL", false); } catch (InvalidDataException) { rejected = true; }
        AssertTrue(rejected);
    }

    private static async Task NativeDirectorySnapshotSmoke(string root)
    {
        string source = Path.Combine(root, "source"), storage = Path.Combine(root, "snapshots");
        if (!Directory.Exists(source)) throw new IOException("Native fixture requires an existing source subvolume.");
        await File.WriteAllTextAsync(Path.Combine(source, "level.dat"), "original");
        var snapshot = await PlatformDirectorySnapshot.TryCaptureAsync(source, storage);
        AssertTrue(snapshot is not null, "Native snapshot was unavailable on the configured native fixture.");
        await using (snapshot!)
        {
            await File.WriteAllTextAsync(Path.Combine(source, "level.dat"), "changed");
            AssertEqual("original", await File.ReadAllTextAsync(Path.Combine(snapshot!.SourceDirectory, "level.dat")));
        }
        AssertFalse(Directory.EnumerateDirectories(storage).Any());
        AssertFalse(Directory.EnumerateFiles(storage).Any());
        var interrupted = await PlatformDirectorySnapshot.TryCaptureAsync(source, storage);
        AssertTrue(interrupted is not null);
        string foreign = Path.Combine(storage, "unrelated"); Directory.CreateDirectory(foreign);
        await using (var recovered = await PlatformDirectorySnapshot.TryCaptureAsync(source, storage))
        {
            AssertTrue(recovered is not null);
            AssertFalse(Directory.Exists(interrupted!.SourceDirectory));
            AssertTrue(Directory.Exists(foreign));
        }
        AssertFalse(Directory.EnumerateFiles(storage).Any());
    }

    private sealed class SnapshotTestLease(Action dispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { dispose(); return ValueTask.CompletedTask; }
    }
    private static async ValueTask NativeSnapshotCaptureKeepsImmutableViewAndReleases()
    {
        AssertEqual("pool/game", PlatformDirectorySnapshot.ExactDataset("pool/game\t/selected\n", "/selected"));
        AssertTrue(PlatformDirectorySnapshot.ExactDataset("pool/home\t/home\n", "/home/selected") is null);
        AssertTrue(PlatformDirectorySnapshot.ExactDataset("pool/game@other\t/selected\n", "/selected") is null);
        string root = OperatingSystem.IsMacOS() ? Path.Combine("/private/tmp", "nexacl-snapshot-" + Guid.NewGuid().ToString("N")) : CreateTempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source"), view = Path.Combine(root, "view");
            Directory.CreateDirectory(source); Directory.CreateDirectory(view);
            await File.WriteAllTextAsync(Path.Combine(source, "level.dat"), "live");
            await File.WriteAllTextAsync(Path.Combine(view, "level.dat"), "snapshot");
            int releases = 0;
            var service = new ContentBackupService(Path.Combine(root, "store"))
            {
                CaptureSnapshot = (_, _, _) => Task.FromResult<ContentCaptureSnapshot?>(new(view, new SnapshotTestLease(() => releases++)))
            };
            var backup = await service.CaptureAsync(source, "native-view");
            AssertEqual(1, releases);
            await service.RestoreAsync(backup.Identity, Path.Combine(root, "restore"));
            AssertEqual("snapshot", await File.ReadAllTextAsync(Path.Combine(root, "restore", "level.dat")));
            var failed = new ContentBackupService(Path.Combine(root, "failed-store"))
            {
                CaptureSnapshot = (_, _, _) => Task.FromResult<ContentCaptureSnapshot?>(new(view, new SnapshotTestLease(() => releases++))),
                CloneFile = (_, _) => throw new IOException("capture failed")
            };
            bool rejected = false;
            try { await failed.CaptureAsync(source, "failure"); } catch (IOException) { rejected = true; }
            AssertTrue(rejected); AssertEqual(2, releases); AssertEqual(0, (await failed.ListAsync()).Count);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask NativeClonesPreserveIndependentFilesAndBackupValidation()
    {
        // macOS /var and /tmp are aliases; backup roots deliberately reject linked ancestors.
        string root = OperatingSystem.IsMacOS() ? Path.Combine("/private/tmp", "nexacl-clone-test-" + Guid.NewGuid().ToString("N")) : CreateTempDirectory();
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source"), copy = Path.Combine(root, "copy");
            Directory.CreateDirectory(source);
            string file = Path.Combine(source, "level.dat");
            await File.WriteAllTextAsync(file, "original");
            bool native = PlatformFileClone.TryClone(file, copy);
            Console.WriteLine("Native CoW clone: " + (native ? "supported" : "unavailable; streaming fallback"));
            if (native)
            {
                AssertEqual("original", await File.ReadAllTextAsync(copy));
                await File.WriteAllTextAsync(copy, "modified");
                AssertEqual("original", await File.ReadAllTextAsync(file));
            }
            else AssertFalse(File.Exists(copy));
            await File.WriteAllTextAsync(copy, "existing");
            bool rejected = false;
            try { PlatformFileClone.TryClone(file, copy); } catch (IOException) { rejected = true; }
            AssertTrue(rejected); AssertEqual("existing", await File.ReadAllTextAsync(copy));
            int attempts = 0;
            var backups = new ContentBackupService(Path.Combine(root, "backup"))
            {
                CloneFile = (from, to) => { attempts++; File.Copy(from, to, false); return true; }
            };
            var manifest = await backups.CaptureAsync(source, "clone-fixture");
            string restored = Path.Combine(root, "restored");
            await backups.RestoreAsync(manifest.Identity, restored);
            AssertEqual(2, attempts); AssertEqual("original", await File.ReadAllTextAsync(Path.Combine(restored, "level.dat")));
            await File.WriteAllTextAsync(Path.Combine(restored, "level.dat"), "changed");
            AssertEqual(1, (await backups.VerifyOfflineAsync(manifest.Identity)).VerifiedFiles);
            var corrupt = new ContentBackupService(Path.Combine(root, "backup"))
            {
                CloneFile = (_, to) => { File.WriteAllText(to, "corrupt!"); return true; }
            };
            string failed = Path.Combine(root, "failed"); rejected = false;
            try { await corrupt.RestoreAsync(manifest.Identity, failed); } catch (IOException) { rejected = true; }
            AssertTrue(rejected); AssertFalse(Directory.Exists(failed));
            AssertFalse(Directory.EnumerateDirectories(root, "failed.nexa-restore-*").Any());
            var fallback = new ContentBackupService(Path.Combine(root, "backup")) { CloneFile = (_, _) => false };
            await fallback.RestoreAsync(manifest.Identity, failed);
            AssertEqual("original", await File.ReadAllTextAsync(Path.Combine(failed, "level.dat")));
        }
        finally { Directory.Delete(root, true); }
    }
}
