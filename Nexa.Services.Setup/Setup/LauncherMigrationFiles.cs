using System.Buffers;
using System.Security.Cryptography;

namespace Nexa.Services.Setup;

internal sealed record LauncherMigrationFile(string Source, string RelativePath, long Bytes, string Sha256);
internal sealed record LauncherMigrationSnapshot(IReadOnlyList<LauncherMigrationFile> Files, IReadOnlyList<string> Directories);

/// <summary>Bounded, link-free file operations shared by import staging and owned-receipt validation.</summary>
internal static class LauncherMigrationFiles
{
    private const int MaximumFiles = 200_000;
    private const int MaximumDirectories = 50_000;
    private const long MaximumFileBytes = 64L * 1024 * 1024 * 1024;
    private const long MaximumTotalBytes = 200L * 1024 * 1024 * 1024;
    private const int BufferBytes = 128 * 1024;

    internal static async Task<LauncherMigrationSnapshot> CaptureAsync(string root, CancellationToken token)
    {
        root = Absolute(root);
        CheckLinks(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("迁移来源目录不存在。");
        List<LauncherMigrationFile> files = [];
        List<string> directories = [];
        Stack<string> pending = new();
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        pending.Push(root);
        long total = 0;
        while (pending.TryPop(out string? directory))
        {
            token.ThrowIfCancellationRequested();
            CheckLinks(directory);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                CheckLinks(entry);
                string relative = Relative(Path.GetRelativePath(root, entry));
                if (!identities.Add(relative)) throw new IOException("迁移目录包含跨平台冲突的名称。");
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (directories.Count >= MaximumDirectories) throw new IOException("迁移目录数量超过预算。");
                    directories.Add(relative);
                    pending.Push(entry);
                }
                else
                {
                    if (files.Count >= MaximumFiles) throw new IOException("迁移文件数量超过预算。");
                    long length = new FileInfo(entry).Length;
                    if (length < 0 || length > MaximumFileBytes || length > MaximumTotalBytes - total)
                        throw new IOException("迁移文件大小超过预算。");
                    LauncherMigrationFile file = await InspectFileAsync(entry, relative, token).ConfigureAwait(false);
                    if (file.Bytes > MaximumTotalBytes - total) throw new IOException("迁移内容总大小超过预算。");
                    total += file.Bytes;
                    files.Add(file);
                }
            }
            CheckLinks(directory);
        }
        files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        directories.Sort(StringComparer.Ordinal);
        return new(files.AsReadOnly(), directories.AsReadOnly());
    }

    internal static async Task<LauncherMigrationFile> InspectFileAsync(string source, string relative, CancellationToken token)
    {
        source = Absolute(source);
        relative = Relative(relative);
        CheckFile(source);
        await using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = stream.Length;
        if (length < 0 || length > MaximumFileBytes) throw new IOException("迁移文件大小超过预算。");
        string sha256 = await HashAsync(stream, length, null, token).ConfigureAwait(false);
        CheckFile(source);
        if (stream.Length != length || new FileInfo(source).Length != length)
            throw new IOException("迁移来源在读取期间发生变化。");
        return new(source, relative, length, sha256);
    }

    internal static async Task CopyAsync(LauncherMigrationFile file, string destinationRoot, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Bytes < 0 || file.Bytes > MaximumFileBytes || file.Sha256.Length != 64 || file.Sha256.Any(static c => !char.IsAsciiHexDigit(c)))
            throw new IOException("迁移文件清单无效。");
        string source = Absolute(file.Source);
        destinationRoot = Absolute(destinationRoot);
        string target = Contained(destinationRoot, file.RelativePath);
        EnsureUnchanged(file, await InspectFileAsync(source, file.RelativePath, token).ConfigureAwait(false));
        token.ThrowIfCancellationRequested();
        CheckLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        CheckLinks(target);
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            CheckFile(source);
            CheckLinks(target);
            if (input.Length != file.Bytes) throw new IOException("迁移来源在预览后发生变化。");
            string sha256 = await HashAsync(input, file.Bytes, output, token).ConfigureAwait(false);
            if (!sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) || input.Length != file.Bytes)
                throw new IOException("迁移来源在复制期间发生变化。");
            await output.FlushAsync(token).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
        CheckLinks(target);
        EnsureUnchanged(file, await InspectFileAsync(source, file.RelativePath, token).ConfigureAwait(false));
    }

    internal static void CheckLinks(string path)
    {
        for (string? current = Absolute(path); current is not null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("迁移路径不能包含符号链接或重解析点。");
        }
    }

    /// <summary>Tests containment including equality; argument order is child, parent.</summary>
    internal static bool Within(string path, string parent)
    {
        path = Absolute(path);
        parent = Absolute(parent);
        if (Nexa.Core.PathIdentity.Comparer.Equals(path, parent)) return true;
        string prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    internal static string Absolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Any(char.IsControl)
            || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new IOException("迁移路径必须为普通绝对路径。");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>Canonicalizes only the two fixed macOS system aliases for private temporary work.</summary>
    internal static string TemporaryRoot()
    {
        string root = Absolute(Path.GetTempPath());
        if (OperatingSystem.IsMacOS())
        {
            foreach (string alias in new[] { "/var", "/tmp" })
            {
                if (!Within(root, alias)) continue;
                if ((File.GetAttributes(alias) & FileAttributes.ReparsePoint) == 0) break;
                string expected = "/private" + alias;
                string? link = new DirectoryInfo(alias).LinkTarget;
                // Inspect just this system link. Never resolve an arbitrary target or another link chain.
                if (string.IsNullOrEmpty(link) || link.Split('/').Any(static component => component is "." or "..")
                    || !Absolute(Path.GetFullPath(link, "/")).Equals(expected, StringComparison.Ordinal))
                    throw new IOException("macOS 临时目录的系统链接目标不符合预期。");
                CheckLinks(expected);
                root = Absolute(expected + root[alias.Length..]);
                break;
            }
        }
        CheckLinks(root);
        return root;
    }

    internal static string Contained(string root, string relative)
    {
        root = Absolute(root);
        string target = Absolute(Path.Combine(root, Relative(relative).Replace('/', Path.DirectorySeparatorChar)));
        if (!Within(target, root) || Nexa.Core.PathIdentity.Comparer.Equals(target, root))
            throw new IOException("迁移文件路径超出工作目录。");
        CheckLinks(target);
        return target;
    }

    internal static void DeleteOwned(string directory)
    {
        directory = Absolute(directory);
        if (Nexa.Core.PathIdentity.Comparer.Equals(directory, Path.GetPathRoot(directory)))
            throw new IOException("不能删除文件系统根目录。");
        CheckLinks(directory);
        if (!Directory.Exists(directory))
        {
            if (File.Exists(directory)) throw new IOException("迁移暂存路径不是目录。");
            return;
        }
        Stack<string> pending = new();
        pending.Push(directory);
        int files = 0, directories = 0;
        while (pending.TryPop(out string? current))
        {
            CheckLinks(current);
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                CheckLinks(entry);
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                {
                    if (++directories > MaximumDirectories) throw new IOException("迁移目录数量超过预算。");
                    pending.Push(entry);
                }
                else if (++files > MaximumFiles) throw new IOException("迁移文件数量超过预算。");
            }
        }
        CheckLinks(directory);
        Directory.Delete(directory, recursive: true);
    }

    private static string Relative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || (!OperatingSystem.IsWindows() && relative.Contains('\\')))
            throw new IOException("迁移文件的相对路径无效。");
        string normalized = relative.Replace('\\', '/');
        foreach (string component in normalized.Split('/'))
        {
            if (component.Length == 0 || component is "." or ".." || component[^1] is '.' or ' '
                || component.Any(static c => char.IsControl(c) || c is '<' or '>' or ':' or '"' or '|' or '?' or '*'))
                throw new IOException("迁移文件包含不安全或不可移植的路径名称。");
            string stem = component.Split('.')[0].TrimEnd(' ');
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
                || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                    && (stem[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3')))
                throw new IOException("迁移文件使用操作系统保留名称。");
        }
        return normalized;
    }

    private static void CheckFile(string path)
    {
        CheckLinks(path);
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw new IOException("迁移来源必须为普通文件。");
    }

    private static void EnsureUnchanged(LauncherMigrationFile expected, LauncherMigrationFile actual)
    {
        if (expected.Bytes != actual.Bytes || !expected.Sha256.Equals(actual.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("迁移来源在预览后发生变化。");
    }

    private static async Task<string> HashAsync(FileStream input, long expectedBytes, FileStream? output, CancellationToken token)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            long total = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int read = await input.ReadAsync(buffer.AsMemory(0, BufferBytes), token).ConfigureAwait(false);
                if (read == 0) break;
                if (read > expectedBytes - total) throw new IOException("迁移来源在读取期间发生变化。");
                total += read;
                hash.AppendData(buffer.AsSpan(0, read));
                if (output is not null) await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }
            if (total != expectedBytes) throw new IOException("迁移来源在读取期间发生变化。");
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }
}
