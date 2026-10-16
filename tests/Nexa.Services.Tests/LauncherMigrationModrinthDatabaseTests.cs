using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Setup;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask LauncherMigrationModrinthReadsCurrentWalWithoutSourceWrites()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            string settingsRoot = MigrationFormatDirectory(root, "settings"), configRoot = MigrationFormatDirectory(root, "custom-data");
            string profile = MigrationFormatDirectory(configRoot, "profiles", "中文实例"), database = Path.Combine(settingsRoot, "app.db");
            using var writer = new MigrationSqliteFixture(database);
            writer.Execute("PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; "
                + "CREATE TABLE settings(id INTEGER PRIMARY KEY,custom_dir TEXT); "
                + "CREATE TABLE instances(id TEXT PRIMARY KEY,path TEXT,name TEXT,applied_content_set_id TEXT,install_stage TEXT); "
                + "CREATE TABLE instance_content_sets(id TEXT PRIMARY KEY,instance_id TEXT,game_version TEXT,loader TEXT,loader_version TEXT); "
                + "CREATE TABLE minecraft_users(access_token TEXT); INSERT INTO minecraft_users VALUES('never-import-this-account-token'); "
                + "INSERT INTO settings VALUES(0,'" + configRoot.Replace("'", "''", StringComparison.Ordinal) + "'); "
                + "INSERT INTO instances VALUES('owned','中文实例','Modrinth 中文实例','applied','installed'); "
                + "INSERT INTO instance_content_sets VALUES('applied','owned','1.20.1','forge','1.20.1-47.2.0');");
            string[] sources = Directory.GetFiles(settingsRoot).Order(StringComparer.Ordinal).ToArray();
            AssertTrue(sources.Contains(database + "-wal"));
            string[] hashes = sources.Select(static path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
            DateTime[] times = sources.Select(File.GetLastWriteTimeUtc).ToArray();
            var instance = (await LauncherMigrationFormats.DiscoverAsync(LauncherMigrationSourceKind.Auto, settingsRoot, default)).Single();
            AssertEqual("Modrinth 中文实例", instance.Name); AssertEqual(profile, instance.GameDirectory);
            AssertEqual("1.20.1", instance.GameVersion); AssertEqual("Forge", instance.Loader); AssertEqual("47.2.0", instance.LoaderBuild);
            AssertTrue(instance.UnsupportedReason is null); AssertEqual(2, instance.ManifestPaths.Count);
            AssertTrue(instance.ManifestPaths.Contains(database)); AssertTrue(instance.ManifestPaths.Contains(database + "-wal"));
            AssertFalse(instance.ToString().Contains("never-import-this-account-token", StringComparison.Ordinal));
            AssertTrue(sources.SequenceEqual(Directory.GetFiles(settingsRoot).Order(StringComparer.Ordinal)));
            for (int i = 0; i < sources.Length; i++)
            {
                AssertEqual(hashes[i], Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sources[i]))));
                AssertEqual(times[i], File.GetLastWriteTimeUtc(sources[i]));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask LauncherMigrationModrinthReadsOldProfilesAndRefusesInvalidDatabase()
    {
        string root = CreateLauncherMigrationTempDirectory();
        try
        {
            MigrationFormatDirectory(root, "profiles", "legacy");
            string database = Path.Combine(root, "app.db");
            using (var writer = new MigrationSqliteFixture(database))
                writer.Execute("CREATE TABLE profiles(path TEXT,name TEXT,game_version TEXT,mod_loader TEXT,mod_loader_version TEXT,install_stage TEXT); "
                    + "INSERT INTO profiles VALUES('legacy','Legacy profile','1.21.1','fabric','0.16.10','installed');");
            var instance = (await LauncherMigrationModrinthDatabase.ReadAsync(root, default)).Single();
            AssertEqual("Legacy profile", instance.Name); AssertEqual("Fabric", instance.Loader); AssertEqual("0.16.10", instance.LoaderBuild);
            AssertTrue(instance.UnsupportedReason is null); AssertEqual(1, instance.ManifestPaths.Count);
            using (var writer = new MigrationSqliteFixture(database))
                writer.Execute("UPDATE profiles SET mod_loader_version='latest';");
            instance = (await LauncherMigrationModrinthDatabase.ReadAsync(root, default)).Single();
            AssertTrue(instance.UnsupportedReason is not null);
            File.WriteAllText(database, "invalid SQLite database with private text");
            instance = (await LauncherMigrationModrinthDatabase.ReadAsync(root, default)).Single();
            AssertTrue(instance.UnsupportedReason is not null);
            AssertFalse(instance.UnsupportedReason!.Contains("private text", StringComparison.Ordinal));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            bool rejected = false;
            try { await LauncherMigrationModrinthDatabase.ReadAsync(root, canceled.Token); }
            catch (OperationCanceledException) { rejected = true; }
            AssertTrue(rejected);
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Creates real SQLite fixtures using the same OS ABI; it is never used by the product reader.</summary>
    private sealed unsafe class MigrationSqliteFixture : IDisposable
    {
        private readonly nint _library;
        private readonly delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, nint*, int> _execute;
        private readonly delegate* unmanaged[Cdecl]<nint, int> _close;
        private nint _database;

        internal MigrationSqliteFixture(string path)
        {
            string library = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "winsqlite3.dll")
                : OperatingSystem.IsMacOS() ? "/usr/lib/libsqlite3.dylib" : "libsqlite3.so.0";
            _library = NativeLibrary.Load(library);
            try
            {
                var open = (delegate* unmanaged[Cdecl]<byte*, nint*, int, byte*, int>)NativeLibrary.GetExport(_library, "sqlite3_open_v2");
                _execute = (delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, nint*, int>)NativeLibrary.GetExport(_library, "sqlite3_exec");
                _close = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(_library, "sqlite3_close_v2");
                byte[] utf8 = Encoding.UTF8.GetBytes(path + '\0');
                nint database = 0;
                int result;
                fixed (byte* address = utf8) result = open(address, &database, 0x00000002 | 0x00000004, null);
                _database = database;
                if (result != 0) throw new IOException("Could not create SQLite migration fixture.");
            }
            catch { Dispose(); throw; }
        }

        internal void Execute(string sql)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(sql + '\0');
            int result;
            fixed (byte* address = utf8) result = _execute(_database, address, 0, 0, null);
            if (result != 0) throw new IOException("Could not execute SQLite migration fixture statement.");
        }

        public void Dispose()
        {
            if (_database != 0) { _close(_database); _database = 0; }
            NativeLibrary.Free(_library);
        }
    }
}
