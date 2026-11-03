using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Minecraft.Java;

internal static class InstanceJavaRuntimeDiscovery
{
    internal static async ValueTask<IReadOnlyList<JavaRuntimeCandidate>> FindAsync(
        string instanceDirectory, IJavaRuntimeLocator inspector, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(instanceDirectory)) throw new IOException("版本目录必须是绝对路径。");
        string root = Path.GetFullPath(instanceDirectory);
        RecoveryBlobStore.CheckLinks(root);
        if (!Directory.Exists(root)) return [];
        List<JavaRuntimeCandidate> candidates = [];
        Stack<(string Directory, int Depth)> pending = new();
        int roots = 0;
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            if (++roots > 4096) throw new IOException("版本目录条目超过扫描限制。");
            string name = Path.GetFileName(directory);
            if (name.Equals("java", StringComparison.OrdinalIgnoreCase) || name.Equals("jre", StringComparison.OrdinalIgnoreCase)
                || name.Equals("jdk", StringComparison.OrdinalIgnoreCase) || name.Equals("runtime", StringComparison.OrdinalIgnoreCase))
                pending.Push((directory, 0));
        }
        await InspectHomeAsync(root).ConfigureAwait(false);
        int visited = 0;
        while (pending.TryPop(out var item))
        {
            token.ThrowIfCancellationRequested();
            if (++visited > 512 || item.Depth > 8) throw new IOException("版本内 Java 目录超过扫描限制。");
            if ((File.GetAttributes(item.Directory) & FileAttributes.ReparsePoint) != 0) continue;
            RecoveryBlobStore.CheckLinks(item.Directory);
            if (await InspectHomeAsync(item.Directory).ConfigureAwait(false)) continue;
            foreach (string child in Directory.EnumerateDirectories(item.Directory).Take(513))
            {
                if (pending.Count >= 512) throw new IOException("版本内 Java 目录超过扫描限制。");
                pending.Push((child, item.Depth + 1));
            }
        }
        return candidates;

        async ValueTask<bool> InspectHomeAsync(string home)
        {
            string path = Path.Combine(home, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (!File.Exists(path)) return false;
            // Do not follow runtimes linked outside the version, even if the target is valid.
            try { RecoveryBlobStore.CheckLinks(path); }
            catch (IOException) { return false; }
            if (candidates.Count >= 32) throw new IOException("版本内 Java 数量超过扫描限制。");
            if (await inspector.InspectAsync(path, token).ConfigureAwait(false) is { } candidate) candidates.Add(candidate);
            return true;
        }
    }
}
