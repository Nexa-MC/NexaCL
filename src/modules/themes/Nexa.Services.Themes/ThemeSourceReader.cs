using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Nexa.Services.Themes;

internal static class ThemeSourceReader
{
    internal const long EntryLimit = 128L * 1024 * 1024;
    internal const long ExpansionLimit = 512L * 1024 * 1024;
    internal const int EntryCountLimit = 4096;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static async Task CopyAsync(string source, ThemeSourceKind kind, string destination, CancellationToken token)
    {
        source = ThemePaths.Canonical(source); destination = ThemePaths.Canonical(destination);
        ThemePaths.NoLinks(source); Directory.CreateDirectory(destination); long total = 0; int count = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathIdentities = new Dictionary<string, (string Name, bool Directory)>(StringComparer.OrdinalIgnoreCase);
        if (kind == ThemeSourceKind.Package)
        {
            if (new FileInfo(source).Length > EntryLimit) throw new InvalidDataException("Theme package exceeds compressed size budget.");
            using var archive = ZipFile.OpenRead(source);
            if (archive.Entries.Count > EntryCountLimit) throw new InvalidDataException("Too many theme archive entries.");
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested(); string name = entry.FullName; bool directory = name.EndsWith('/');
                if (directory) name = name[..^1]; ThemePaths.Relative(name);
                AdmitPath(name, directory, pathIdentities);
                if (!names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Duplicate path or link in package.");
                if (directory) { Directory.CreateDirectory(ThemePaths.Within(destination, name)); continue; }
                if (entry.Length > EntryLimit || entry.CompressedLength > EntryLimit) throw new InvalidDataException("Theme entry exceeds size budget.");
                total = checked(total + entry.Length); if (total > ExpansionLimit) throw new InvalidDataException("Theme exceeds expansion budget.");
                string target = ThemePaths.Within(destination, name); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open(); await CopyExactAsync(input, target, entry.Length, token).ConfigureAwait(false);
            }
        }
        else
        {
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Theme source directory does not exist.");
            // Walk each directory only after inspecting it; recursive framework enumeration can follow links before validation.
            var directories = new Stack<string>(); directories.Push(source);
            while (directories.TryPop(out string? directory))
            {
                ThemePaths.NoLinks(directory);
                foreach (string path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested(); if (++count > EntryCountLimit) throw new InvalidDataException("Too many theme directory entries.");
                    ThemePaths.NoLinks(path); string relative = Path.GetRelativePath(source, path).Replace(Path.DirectorySeparatorChar, '/'); ThemePaths.Relative(relative);
                    AdmitPath(relative, Directory.Exists(path), pathIdentities);
                    if (!names.Add(relative)) throw new InvalidDataException("Ambiguous theme directory path.");
                    if (Directory.Exists(path)) { directories.Push(path); continue; }
                    long length = new FileInfo(path).Length; total = checked(total + length);
                    if (length > EntryLimit || total > ExpansionLimit) throw new InvalidDataException("Theme directory exceeds its byte budget.");
                    string target = ThemePaths.Within(destination, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); await CopyExactAsync(input, target, length, token).ConfigureAwait(false);
                }
            }
        }
        // Files and directories cannot occupy one path, or shadow a file with a descendant on another OS.
        foreach (string name in names)
        {
            int slash = name.IndexOf('/');
            while (slash >= 0) { string prefix = name[..slash]; if (File.Exists(ThemePaths.Within(destination, prefix))) throw new InvalidDataException("Theme path conflicts with a file."); slash = name.IndexOf('/', slash + 1); }
        }
        foreach (string file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension is ".nxml" or ".exe" or ".dll" or ".so" or ".dylib" or ".js" or ".ps1" or ".bat" or ".cmd" or ".sh" or ".py" or ".lua" or ".vbs" or ".wasm" or ".jar" or ".class") throw new InvalidDataException("Theme packages cannot contain templates or executable content.");
        }
    }

    private static void AdmitPath(string name, bool directory, Dictionary<string, (string Name, bool Directory)> identities)
    {
        int end = 0;
        while (end < name.Length)
        {
            int next = name.IndexOf('/', end); bool parent = next >= 0; string prefix = parent ? name[..next] : name;
            string normalized = prefix.Normalize(NormalizationForm.FormC); bool isDirectory = parent || directory;
            if (identities.TryGetValue(normalized, out var prior))
            {
                if (!string.Equals(prior.Name, prefix, StringComparison.Ordinal) || prior.Directory != isDirectory) throw new InvalidDataException("Cross-platform ambiguous theme path or file/directory collision.");
            }
            else identities.Add(normalized, (prefix, isDirectory));
            if (!parent) break; end = next + 1;
        }
    }

    internal static (ThemeManifest Manifest, IReadOnlyList<ThemeStyleSource> Styles, string Revision) Validate(string root, HashSet<string> hostCapabilities)
    {
        string mimetype = Utf8.GetString(ReadBounded(ThemePaths.Within(root, "mimetype"), 128));
        if (mimetype is not ("application/x-nexa-theme" or "application/x-nexa-theme\n")) throw new InvalidDataException("Theme mimetype is invalid.");
        var manifest = ThemeManifestReader.Read(ReadBounded(ThemePaths.Within(root, "theme.json"), ThemeManifestReader.ManifestLimit));
        foreach (string capability in manifest.RequiredCapabilities) if (!hostCapabilities.Contains(capability)) throw new InvalidDataException("Unsupported required theme capability: " + capability);
        var styles = new List<ThemeStyleSource>(); long size = 0;
        foreach (string path in manifest.Styles)
        {
            byte[] bytes = ReadBounded(ThemePaths.Within(root, path), 1024 * 1024); size += bytes.Length;
            if (size > 1024 * 1024) throw new InvalidDataException("Theme NSS exceeds total 1 MiB budget.");
            styles.Add(new(path, Utf8.GetString(bytes)));
        }
        foreach (var resource in manifest.Appearance.Resources.Values)
        {
            var (function, argument) = ThemePaths.Expression(resource.Expression);
            if (function == "asset") ThemePackageService.ContentType(argument, resource.Kind);
            if (resource.Required && function == "asset" && !File.Exists(ThemePaths.Within(root, argument))) throw new InvalidDataException("Required theme asset is missing.");
            if (resource.Fallback is { } fallback && fallback != "none")
            {
                var (fallbackFunction, fallbackPath) = ThemePaths.Expression(fallback);
                if (fallbackFunction != "asset" || !File.Exists(ThemePaths.Within(root, fallbackPath))) throw new InvalidDataException("Resource fallbacks must be existing package assets or none.");
                ThemePackageService.ContentType(fallbackPath, resource.Kind);
            }
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))); hash.AppendData([0]);
            using var stream = File.OpenRead(path); byte[] buffer = new byte[81920]; int received;
            while ((received = stream.Read(buffer)) != 0) hash.AppendData(buffer.AsSpan(0, received));
        }
        return (manifest, styles.AsReadOnly(), Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    internal static byte[] ReadBounded(string path, int limit)
    {
        path = ThemePaths.Canonical(path); ThemePaths.NoLinks(path); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > limit) throw new InvalidDataException("Theme file exceeds its budget.");
        byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() >= 0) throw new InvalidDataException("Theme file changed during reading."); return bytes;
    }

    internal static async Task CopyExactAsync(Stream input, string destination, long length, CancellationToken token)
    {
        if (length is < 0 or > EntryLimit) throw new InvalidDataException("Resource exceeds byte budget.");
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        byte[] buffer = new byte[81920]; long actual = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - actual + 1)), token).ConfigureAwait(false);
            if (read == 0) break; actual += read; if (actual > length) throw new InvalidDataException("Resource changed or exceeds its declared length.");
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        if (actual != length) throw new InvalidDataException("Resource changed or has an invalid actual length.");
        await output.FlushAsync(token).ConfigureAwait(false);
    }
}
