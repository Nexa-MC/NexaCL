using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Nexa.Services.Setup;

/// <summary>Reads only documented Modrinth instance metadata from a private, read-only database snapshot.</summary>
internal static class LauncherMigrationModrinthDatabase
{
    private const long MaximumDatabaseBytes = 256L * 1024 * 1024;
    private const int MaximumInstances = 256;
    private static readonly string[] OmittedSettings = ["账户、登录凭据、启动钩子和启动器专属设置不会导入；Java 与 JVM 参数请在迁移后确认。"];

    internal static async Task<IReadOnlyList<LauncherMigrationSourceInstance>> ReadAsync(string root, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        root = LauncherMigrationFiles.Absolute(root);
        string database = Path.Combine(root, "app.db"), wal = database + "-wal";
        string[] manifests = File.Exists(wal) ? [database, wal] : [database];
        string? temporary = null;
        try
        {
            LauncherMigrationFiles.CheckLinks(root);
            temporary = Path.Combine(LauncherMigrationFiles.TemporaryRoot(), "nexa-modrinth-discovery-" + Guid.NewGuid().ToString("N"));
            LauncherMigrationFiles.CheckLinks(temporary);
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(temporary);
            else Directory.CreateDirectory(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            List<string> hashes = [];
            foreach (string source in manifests)
                hashes.Add(await CopySnapshotAsync(source, Path.Combine(temporary, Path.GetFileName(source)), token).ConfigureAwait(false));
            for (int i = 0; i < manifests.Length; i++)
                if (!hashes[i].Equals(await SourceHashAsync(manifests[i], token).ConfigureAwait(false), StringComparison.Ordinal))
                    throw new IOException("Modrinth 数据库在读取期间变化，请退出原启动器后重试。");
            if (File.Exists(wal) != (manifests.Length == 2))
                throw new IOException("Modrinth 数据库在读取期间变化，请退出原启动器后重试。");
            using var connection = new Database(Path.Combine(temporary, "app.db"), token);
            connection.Query("BEGIN", 0, 0);
            HashSet<string> tables = connection.Query("SELECT name FROM sqlite_master WHERE type='table' AND name IN ('profiles','instances','instance_content_sets','settings')", 1, 4)
                .Select(static row => row[0]!).ToHashSet(StringComparer.Ordinal);
            string configRoot = root;
            if (tables.Contains("settings"))
            {
                List<string?[]> settings = connection.Query("SELECT custom_dir FROM settings WHERE id=0 LIMIT 1", 1, 1);
                if (settings.Count != 0 && !string.IsNullOrWhiteSpace(settings[0][0]))
                    configRoot = LauncherMigrationFiles.Absolute(settings[0][0]!);
            }
            LauncherMigrationFiles.CheckLinks(configRoot);
            string profiles = Path.Combine(configRoot, "profiles");
            LauncherMigrationFiles.CheckLinks(profiles);
            string sql;
            if (tables.Contains("instances") && tables.Contains("instance_content_sets"))
                sql = "SELECT i.path,i.name,cs.game_version,cs.loader,cs.loader_version,i.install_stage FROM instances i LEFT JOIN instance_content_sets cs ON cs.id=i.applied_content_set_id AND cs.instance_id=i.id ORDER BY i.path LIMIT 257";
            else if (tables.Contains("profiles"))
                sql = "SELECT path,name,game_version,mod_loader,mod_loader_version,install_stage FROM profiles ORDER BY path LIMIT 257";
            else throw new IOException("Modrinth 数据库结构不受支持，请选择受支持的实例或导出整合包。");
            List<string?[]> rows = connection.Query(sql, 6, MaximumInstances);
            List<LauncherMigrationSourceInstance> instances = [];
            HashSet<string> identities = new(Nexa.Core.PathIdentity.Comparer);
            foreach (string?[] row in rows)
            {
                token.ThrowIfCancellationRequested();
                string path = row[0] ?? "";
                string directory = LauncherMigrationFiles.Contained(profiles, path);
                if (!identities.Add(directory)) throw new IOException("Modrinth 数据库包含重复实例路径。");
                string name = row[1] ?? Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl)) name = Path.GetFileName(directory);
                string? reason = null;
                string? loader = (row[3] ?? "").ToLowerInvariant() switch
                {
                    "vanilla" => null,
                    "forge" => "Forge",
                    "fabric" => "Fabric",
                    "quilt" => "Quilt",
                    "neoforge" => "NeoForge",
                    _ => null
                };
                string? build = loader is null ? null : row[4];
                if (loader is "Forge" or "NeoForge" && build is not null && !string.IsNullOrWhiteSpace(row[2])
                    && build.StartsWith(row[2] + "-", StringComparison.Ordinal)) build = build[(row[2]!.Length + 1)..];
                if (string.IsNullOrWhiteSpace(row[2])) reason = "Modrinth 实例缺少 Minecraft 版本或当前内容集。";
                else if ((row[3] ?? "").ToLowerInvariant() is not ("vanilla" or "forge" or "fabric" or "quilt" or "neoforge")) reason = "Modrinth 实例使用未支持的加载器。";
                else if (loader is not null && (string.IsNullOrWhiteSpace(build)
                    || build.ToLowerInvariant() is "latest" or "stable" or "recommended" or "nightly")) reason = "Modrinth 实例缺少精确加载器版本。";
                else if (!string.Equals(row[5], "installed", StringComparison.OrdinalIgnoreCase)) reason = "Modrinth 实例尚未安装完成，请先在原启动器中完成安装。";
                else if (!Directory.Exists(directory)) reason = "Modrinth 实例游戏目录不存在，请检查自定义数据目录。";
                instances.Add(new(directory, name, directory, null, null, row[2] ?? "", loader,
                    build, reason, manifests, OmittedSettings));
            }
            connection.Query("COMMIT", 0, 0);
            return instances.AsReadOnly();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException or BadImageFormatException or EntryPointNotFoundException)
        {
            string reason = error is DatabaseException db ? db.Message : error is IOException && error.Message.StartsWith("Modrinth", StringComparison.Ordinal)
                ? error.Message : "无法只读解析 Modrinth 数据库；请退出原启动器并检查数据库、实例目录和访问权限。";
            return [new(root, "Modrinth App", root, null, null, "", null, null, reason, manifests, OmittedSettings)];
        }
        finally
        {
            if (temporary is not null)
            {
                LauncherMigrationFiles.CheckLinks(temporary);
                if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static async Task<string> CopySnapshotAsync(string source, string target, CancellationToken token)
    {
        LauncherMigrationFiles.CheckLinks(source);
        await using var input = OpenSource(source);
        long length = input.Length;
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            int count = await input.ReadAsync(buffer, token).ConfigureAwait(false);
            if (count == 0) break;
            total += count;
            if (total > length) throw new IOException("Modrinth 数据库在读取期间变化，请退出原启动器后重试。");
            hash.AppendData(buffer.AsSpan(0, count));
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
        LauncherMigrationFiles.CheckLinks(source);
        if (total != length || input.Length != length) throw new IOException("Modrinth 数据库在读取期间变化，请退出原启动器后重试。");
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<string> SourceHashAsync(string source, CancellationToken token)
    {
        LauncherMigrationFiles.CheckLinks(source);
        await using var stream = OpenSource(source);
        long length = stream.Length, total = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        while (true)
        {
            int count = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (count == 0) break;
            total += count;
            if (total > length) throw new IOException("Modrinth 数据库在读取期间变化，请退出原启动器后重试。");
            hash.AppendData(buffer.AsSpan(0, count));
        }
        LauncherMigrationFiles.CheckLinks(source);
        if (total != length || stream.Length != length) throw new IOException("Modrinth 数据库在读取期间变化，请退出原启动器后重试。");
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static FileStream OpenSource(string source)
    {
        var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is < 0 or > MaximumDatabaseBytes || (stream.Length == 0 && !source.EndsWith("-wal", StringComparison.Ordinal)))
        {
            stream.Dispose();
            throw new IOException("Modrinth 数据库为空或超过 256 MiB 读取预算。");
        }
        return stream;
    }

    private sealed class DatabaseException(string message) : IOException(message) { }

    private sealed unsafe class Database : IDisposable
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private readonly nint _library;
        private nint _database;
        private readonly delegate* unmanaged[Cdecl]<byte*, nint*, int, byte*, int> _open;
        private readonly delegate* unmanaged[Cdecl]<nint, int> _close;
        private readonly delegate* unmanaged[Cdecl]<nint, int, int> _busyTimeout;
        private readonly delegate* unmanaged[Cdecl]<nint, int, int, int> _limit;
        private readonly delegate* unmanaged[Cdecl]<nint, int, delegate* unmanaged[Cdecl]<void*, int>, void*, void> _progress;
        private readonly delegate* unmanaged[Cdecl]<nint, byte*, int, nint*, byte**, int> _prepare;
        private readonly delegate* unmanaged[Cdecl]<nint, int> _step;
        private readonly delegate* unmanaged[Cdecl]<nint, int> _finalize;
        private readonly delegate* unmanaged[Cdecl]<nint, int, int> _columnType;
        private readonly delegate* unmanaged[Cdecl]<nint, int, int> _columnBytes;
        private readonly delegate* unmanaged[Cdecl]<nint, int, byte*> _columnText;
        private readonly CancellationToken _token;
        private GCHandle _progressState;

        internal Database(string path, CancellationToken token)
        {
            _token = token;
            string library = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "winsqlite3.dll")
                : OperatingSystem.IsMacOS() ? "/usr/lib/libsqlite3.dylib" : "libsqlite3.so.0";
            if (!NativeLibrary.TryLoad(library, out _library))
                throw new DatabaseException("当前系统缺少 SQLite 库，无法读取 Modrinth App 数据库；其他启动器迁移仍可使用。");
            try
            {
                _open = (delegate* unmanaged[Cdecl]<byte*, nint*, int, byte*, int>)Export("sqlite3_open_v2");
                _close = (delegate* unmanaged[Cdecl]<nint, int>)Export("sqlite3_close_v2");
                _busyTimeout = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("sqlite3_busy_timeout");
                _limit = (delegate* unmanaged[Cdecl]<nint, int, int, int>)Export("sqlite3_limit");
                _progress = (delegate* unmanaged[Cdecl]<nint, int, delegate* unmanaged[Cdecl]<void*, int>, void*, void>)Export("sqlite3_progress_handler");
                _prepare = (delegate* unmanaged[Cdecl]<nint, byte*, int, nint*, byte**, int>)Export("sqlite3_prepare_v2");
                _step = (delegate* unmanaged[Cdecl]<nint, int>)Export("sqlite3_step");
                _finalize = (delegate* unmanaged[Cdecl]<nint, int>)Export("sqlite3_finalize");
                _columnType = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("sqlite3_column_type");
                _columnBytes = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("sqlite3_column_bytes");
                _columnText = (delegate* unmanaged[Cdecl]<nint, int, byte*>)Export("sqlite3_column_text");
                byte[] utf8 = StrictUtf8.GetBytes(path + '\0');
                nint database = 0;
                int result;
                fixed (byte* address = utf8) result = _open(address, &database, 0x00000001 | 0x00008000, null);
                _database = database;
                Ensure(result);
                Ensure(_busyTimeout(_database, 250));
                _limit(_database, 0, 64 * 1024); // SQLITE_LIMIT_LENGTH
                _limit(_database, 1, 16 * 1024); // SQLITE_LIMIT_SQL_LENGTH
                _limit(_database, 2, 256); // SQLITE_LIMIT_COLUMN
                _limit(_database, 3, 32); // SQLITE_LIMIT_EXPR_DEPTH
                _limit(_database, 5, 100_000); // SQLITE_LIMIT_VDBE_OP
                _limit(_database, 7, 0); // SQLITE_LIMIT_ATTACHED
                _progressState = GCHandle.Alloc(new QueryBudget(token));
                _progress(_database, 1000, &CheckBudget, (void*)GCHandle.ToIntPtr(_progressState));
            }
            catch { Dispose(); throw; }
        }

        internal List<string?[]> Query(string sql, int columns, int maximumRows)
        {
            _token.ThrowIfCancellationRequested();
            byte[] utf8 = StrictUtf8.GetBytes(sql + '\0');
            nint statement = 0;
            try
            {
                int result;
                fixed (byte* address = utf8) result = _prepare(_database, address, utf8.Length, &statement, null);
                Ensure(result);
                List<string?[]> rows = [];
                while ((result = _step(statement)) == 100)
                {
                    _token.ThrowIfCancellationRequested();
                    if (rows.Count >= maximumRows) throw new DatabaseException("Modrinth 实例数量超过 256 个读取预算。");
                    string?[] row = new string?[columns];
                    for (int column = 0; column < columns; column++)
                    {
                        if (_columnType(statement, column) == 5) continue;
                        byte* text = _columnText(statement, column);
                        int bytes = _columnBytes(statement, column);
                        if (bytes > 4096 || bytes < 0 || text == null)
                            throw new DatabaseException("Modrinth 数据库字段无效或超过读取预算。");
                        row[column] = StrictUtf8.GetString(new ReadOnlySpan<byte>(text, bytes));
                    }
                    rows.Add(row);
                }
                if (result != 101) Ensure(result);
                return rows;
            }
            finally { if (statement != 0) _finalize(statement); }
        }

        private nint Export(string name) => NativeLibrary.GetExport(_library, name);

        private void Ensure(int result)
        {
            if (result == 0) return;
            _token.ThrowIfCancellationRequested();
            throw new DatabaseException("无法读取 Modrinth 数据库；数据库损坏、结构不兼容或读取超时，请退出原启动器后重试。");
        }

        public void Dispose()
        {
            if (_database != 0)
            {
                _progress(_database, 0, null, null);
                _close(_database);
                _database = 0;
            }
            if (_progressState.IsAllocated) _progressState.Free();
            NativeLibrary.Free(_library);
        }

        private sealed class QueryBudget(CancellationToken token)
        {
            internal readonly CancellationToken Token = token;
            internal readonly long Started = Stopwatch.GetTimestamp();
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int CheckBudget(void* state)
        {
            var budget = (QueryBudget)GCHandle.FromIntPtr((nint)state).Target!;
            return budget.Token.IsCancellationRequested || Stopwatch.GetElapsedTime(budget.Started) > TimeSpan.FromSeconds(5) ? 1 : 0;
        }
    }
}
