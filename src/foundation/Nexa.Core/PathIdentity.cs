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
