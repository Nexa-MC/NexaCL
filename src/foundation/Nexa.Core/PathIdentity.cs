namespace Nexa.Core;

/// <summary>Portable path identity shared by storage, installation and management.</summary>
public static class PathIdentity
{
    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>Recognizes only fixed macOS root aliases and verifies their target ancestry.</summary>
    public static bool IsMacOSSystemDirectoryAlias(string path)
    {
        if (!OperatingSystem.IsMacOS() || path is not ("/var" or "/tmp" or "/etc")) return false;
        if (new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName is not { } target
            || !string.Equals(target, "/private" + path, StringComparison.Ordinal)) return false;
        for (string? current = target; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }

    /// <summary>Resolves existing local file links and linked ancestors with a bounded traversal.</summary>
    public static string ResolveExistingFile(string path)
    {
        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full)!;
        var pending = new Queue<string>(full[current.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries));
        int links = 0;
        while (pending.TryDequeue(out string? part))
        {
            current = Path.Combine(current, part);
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) == 0 || IsMacOSSystemDirectoryAlias(current)) continue;
            if (++links > 64) throw new IOException("The selected file contains a cyclic or excessively deep link chain.");
            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(current) : new FileInfo(current);
            string target = info.ResolveLinkTarget(returnFinalTarget: false)?.FullName
                ?? throw new IOException("The selected file contains an unsupported link.");
            string root = Path.GetPathRoot(target)!;
            pending = new Queue<string>(target[root.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Concat(pending));
            current = root;
        }
        if (!File.Exists(current) || Directory.Exists(current)) throw new FileNotFoundException("The selected local file does not exist.", path);
        return current;
    }

    public static string Contained(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Normalize(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("安装器路径超出工作目录。");
        for (string? parent = Path.GetDirectoryName(full); parent is not null && parent.Length >= prefix.Length; parent = Path.GetDirectoryName(parent))
            if (Directory.Exists(parent) && File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("安装目录不能包含链接。");
        return full;
    }
}
