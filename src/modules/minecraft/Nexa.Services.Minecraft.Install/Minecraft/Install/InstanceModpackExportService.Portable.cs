using System.IO.Compression;
using System.Text;
using Nexa.Services.Minecraft.Management;
namespace Nexa.Services.Minecraft.Install;

public sealed partial class InstanceModpackExportService
{
    private static readonly string[] PortableModules = ["host", "ui", "pac", "crypto", "network", "updates", "media", "minecraft", "markup", "artwork", "accounts"];
    private static async Task WritePortablePayloadAsync(ZipArchive archive, InstanceModpackExportCommand command, bool directoryZip, CancellationToken token)
    {
        if (command.LauncherDirectory.Length == 0 && command.JavaDirectory.Length == 0) return;
        if (!directoryZip) throw new InvalidDataException("携带启动器或 Java 请选择游戏目录 ZIP 格式。");
        long total = 0; int count = 0; string? executable = null;
        if (command.LauncherDirectory.Length > 0)
        {
            string root = Path.GetFullPath(command.LauncherDirectory); RecoveryBlobStore.CheckLinks(root);
            executable = File.Exists(Path.Combine(root, "Nexa.Desktop.exe")) ? "Nexa.Desktop.exe" : "Nexa.Desktop";
            string suffix = executable.EndsWith(".exe", StringComparison.Ordinal) ? ".dll" : File.Exists(Path.Combine(root, "nexa-host.dylib")) ? ".dylib" : ".so";
            string[] required = [executable, "Nexa.Bootstrap.nfp", .. PortableModules.Select(n => "nexa-" + n + suffix)];
            foreach (string name in required)
                if (!File.Exists(Path.Combine(root, name)) || new FileInfo(Path.Combine(root, name)).Length == 0) throw new InvalidDataException("启动器目录不是完整的 NativeAOT 发布包，缺少：" + name);
            var runtimeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "libSkiaSharp.so", "libSkiaSharp.dylib", "libSkiaSharp.dll", "libHarfBuzzSharp.so", "libHarfBuzzSharp.dylib", "libHarfBuzzSharp.dll", "libAvaloniaNative.dylib", "libAvaloniaNative.so", "av_libglesv2.dll", "av_libegl.dll", "libEGL.dll", "libGLESv2.dll", "Avalonia.Native.dll", "SkiaSharp.dll", "HarfBuzzSharp.dll" };
            foreach (string name in required.Concat(Directory.EnumerateFiles(root).Select(Path.GetFileName).OfType<string>().Where(n => runtimeNames.Contains(n) || n.EndsWith(".nfp", StringComparison.Ordinal)
                || n.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) || n.StartsWith("COPYING", StringComparison.OrdinalIgnoreCase) || n.StartsWith("NOTICE", StringComparison.OrdinalIgnoreCase)
                || n is "Nexa.Desktop.deps.json" or "Nexa.Desktop.runtimeconfig.json" or "Nexa.Jvm.Host.deps.json" or "Nexa.Jvm.Host.runtimeconfig.json" or "Nexa.Update.Helper.deps.json" or "Nexa.Update.Helper.runtimeconfig.json" or "Nexa.Cloud.Market.Sidecar.nsc" or "Nexa.Cloud.Market.Sidecar.nsc.asc" or "Nexa.Cloud.Market.Sidecar.json" or "Nexa.Jvm.Host" or "Nexa.Jvm.Host.exe" or "Nexa.Update.Helper" or "Nexa.Update.Helper.exe" or "LICENSE" or "THIRD-PARTY-NOTICES.md")).Distinct(StringComparer.Ordinal))
                await AddAsync(Path.Combine(root, name), "launcher/" + name).ConfigureAwait(false);
            foreach (string directoryName in new[] { "assets", "resources", "licenses" })
            {
                string staticRoot = Path.Combine(root, directoryName);
                if (!Directory.Exists(staticRoot)) continue;
                Stack<string> pending = new(); pending.Push(staticRoot); int directories = 0;
                while (pending.TryPop(out string? directory))
                {
                    token.ThrowIfCancellationRequested(); RecoveryBlobStore.CheckLinks(directory);
                    if (++directories > 10000 || Path.GetRelativePath(root, directory).Split(Path.DirectorySeparatorChar).Length > 32) throw new InvalidDataException("启动器静态资源目录超过限制。");
                    foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        RecoveryBlobStore.CheckLinks(path);
                        string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                        if (ExcludedExportFile(relative)) continue;
                        if (Directory.Exists(path)) pending.Push(path);
                        else await AddAsync(path, "launcher/" + relative).ConfigureAwait(false);
                    }
                }
            }
            string runtime = Path.Combine(root, "runtime");
            if (Directory.Exists(runtime)) foreach (string file in Directory.EnumerateFiles(runtime))
                if (runtimeNames.Contains(Path.GetFileName(file))) await AddAsync(file, "launcher/runtime/" + Path.GetFileName(file)).ConfigureAwait(false);
        }
        if (command.JavaDirectory.Length > 0)
        {
            string root = Path.GetFullPath(command.JavaDirectory); RecoveryBlobStore.CheckLinks(root);
            if (!File.Exists(Path.Combine(root, "release")) || !(File.Exists(Path.Combine(root, "bin", "java")) || File.Exists(Path.Combine(root, "bin", "java.exe"))))
                throw new InvalidDataException("Java 目录应包含 release 和 bin/java（或 java.exe）。");
            if (executable is not null && !File.Exists(Path.Combine(root, "bin", executable.EndsWith(".exe", StringComparison.Ordinal) ? "java.exe" : "java")))
                throw new InvalidDataException("Java 与所选启动器的平台不同。");
            foreach (string top in Directory.EnumerateFileSystemEntries(root))
            {
                string name = Path.GetFileName(top);
                if (name is not ("bin" or "lib" or "conf" or "legal" or "include" or "jmods" or "release" or "LICENSE" or "COPYRIGHT" or "README" or "NOTICE")) continue;
                Stack<(string Physical, string Relative)> pending = new(); pending.Push((top, name)); int directories = 0;
                while (pending.TryPop(out var item))
                {
                    token.ThrowIfCancellationRequested();
                    string path = item.Physical;
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    {
                        FileSystemInfo source = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                        path = source.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Java 包含无法解析的链接。");
                        string contained = Path.GetRelativePath(root, path).Replace('\\', '/');
                        MinecraftModpackArchive.SafeRelative(contained);
                    }
                    RecoveryBlobStore.CheckLinks(path);
                    if (Directory.Exists(path))
                    {
                        if (++directories > 10000 || item.Relative.Split('/').Length > 32) throw new InvalidDataException("Java 目录超过限制或包含循环链接。");
                        foreach (string child in Directory.EnumerateFileSystemEntries(path)) pending.Push((child, item.Relative + "/" + Path.GetFileName(child)));
                    }
                    else await AddAsync(path, "java/" + item.Relative).ConfigureAwait(false);
                }
            }
        }
        if (executable is not null)
        {
            string instance = Path.GetFileName(command.InstanceDirectory);
            if (instance.Any(c => char.IsControl(c) || c is '%' or '!' or '"')) throw new InvalidDataException("实例名称不能用于便携启动脚本。");
            string windowsJava = command.JavaDirectory.Length > 0 ? "set \"JAVA_HOME=%~dp0java\"\r\n" : "";
            string unixJava = command.JavaDirectory.Length > 0 ? "export JAVA_HOME=\"$PWD/java\"\n" : "";
            if (executable.EndsWith(".exe", StringComparison.Ordinal))
                await TextAsync("Start.cmd", "@echo off\r\nsetlocal DisableDelayedExpansion\r\ncd /d \"%~dp0\"\r\n" + windowsJava + "\"%~dp0launcher\\Nexa.Desktop.exe\" --run-root \"%~dp0.minecraft\" --run-instance \"" + instance + "\"\r\n").ConfigureAwait(false);
            else
                await TextAsync("Start.sh", "#!/bin/sh\ncd -- \"$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd)\" || exit 1\n" + unixJava + "exec ./launcher/Nexa.Desktop --run-root \"$PWD/.minecraft\" --run-instance '" + instance.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'\n").ConfigureAwait(false);
        }
        await TextAsync("PORTABLE.txt", "Minecraft: .minecraft\nLauncher: launcher\nJava: java (select bin/java in launcher Java settings)\nSign in or create an offline account on first run. No account data is included.\n").ConfigureAwait(false);

        async Task TextAsync(string name, string value)
        {
            var entry = archive.CreateEntry(name);
            if (name.EndsWith(".sh", StringComparison.Ordinal)) entry.ExternalAttributes = 0x81ED << 16;
            await using var output = entry.Open(); await output.WriteAsync(Encoding.UTF8.GetBytes(value), token).ConfigureAwait(false);
        }
        async Task AddAsync(string path, string relative)
        {
            token.ThrowIfCancellationRequested(); RecoveryBlobStore.CheckLinks(path);
            await using var input = File.OpenRead(path);
            if (input.Length > FileLimit || (total += input.Length) > TotalLimit || ++count > 50000) throw new InvalidDataException("便携运行时超过导出限制。");
            var entry = archive.CreateEntry(MinecraftModpackArchive.SafeRelative(relative), CompressionLevel.Fastest);
            if (!OperatingSystem.IsWindows()) entry.ExternalAttributes = ((int)File.GetUnixFileMode(path) | 0x8000) << 16;
            await using var target = entry.Open();
            await Nexa.Services.Files.ArchiveReadBudget.CopyAsync(input, target, input.Length, FileLimit, new(FileLimit), token).ConfigureAwait(false);
        }
    }
}
