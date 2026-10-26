using Nexa.Platform;
using Nexa.Services.Setup;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask NativeClonesPreserveIndependentFilesAndBackupValidation()
    {
        string root = CreateTempDirectory();
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
