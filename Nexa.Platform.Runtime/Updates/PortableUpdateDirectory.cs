using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Nexa.Platform.Updates;

/// <summary>
/// User-owned portable storage, exclusively without elevation. This does not claim
/// the same-account isolation provided by ProtectedUpdateDirectory.
/// </summary>
public sealed partial class PortableUpdateDirectory : IUpdateDirectory
{
    private readonly bool _created;
    private bool _disposed;
    public string Path { get; }
    private PortableUpdateDirectory(string path, bool created) { Path = path; _created = created; }

    public static IUpdateDirectory Open(string path)
    {
        if (PortableUpdateInstallation.IsElevated)
            throw new UnauthorizedAccessException("便携版自动更新不能以管理员权限运行。请以普通用户启动，或使用系统安装包。");
        string full = System.IO.Path.GetFullPath(path);
        if (PortableUpdateInstallation.IsSystemPath(full))
            throw new UnauthorizedAccessException("系统安装必须使用受保护的更新 helper。");
        ValidateTree(full);
        return new PortableUpdateDirectory(full, false);
    }

    public IUpdateDirectory OpenDirectory(string name)
    {
        string child = Leaf(name);
        ValidateTree(child);
        return new PortableUpdateDirectory(child, false);
    }

    public IUpdateDirectory CreateDirectory(string name, bool publicRead)
    {
        string child = Leaf(name);
        // Both calls fail if the entry exists; never adopt or repair a pre-existing tree.
        bool created = OperatingSystem.IsWindows() ? CreateDirectoryWindows(child, 0)
            : Mkdir(child, publicRead ? 0x1edu : 0x1c0u) == 0;
        if (!created) throw new IOException("无法创建新的便携更新目录。", new Win32Exception(Marshal.GetLastPInvokeError()));
        ValidateTree(child);
        return new PortableUpdateDirectory(child, true);
    }

    public FileStream OpenRead(string name)
    {
        string file = Leaf(name);
        ValidateFile(file, allowMissing: false);
        return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
    }

    public FileStream OpenState(string name, bool publicRead = false, bool exclusive = true)
    {
        string file = Leaf(name);
        ValidateFile(file, allowMissing: true);
        var stream = new FileStream(file, Options(FileMode.OpenOrCreate, publicRead, executable: false,
            exclusive ? FileShare.Read : FileShare.ReadWrite));
        try
        {
            if (exclusive && !OperatingSystem.IsWindows()
                && Flock(checked((int)stream.SafeFileHandle.DangerousGetHandle()), 2 | 4) != 0)
                throw new IOException("另一个便携更新事务正在进行。");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    public FileStream CreateFile(string name, bool publicRead = false, bool executable = false)
    {
        if (!_created) throw new InvalidOperationException("更新载荷只能写入新建目录。");
        return new FileStream(Leaf(name), Options(FileMode.CreateNew, publicRead, executable, FileShare.Read));
    }

    private static FileStreamOptions Options(FileMode mode, bool publicRead, bool executable, FileShare sharing)
    {
        var options = new FileStreamOptions
        {
            Mode = mode,
            Access = FileAccess.ReadWrite,
            Share = sharing,
            BufferSize = 65536,
            Options = FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = (UnixFileMode)(publicRead ? executable ? 0x1ed : 0x1a4 : 0x180);
        return options;
    }

    private string Leaf(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(name) || name.Length > 255 || name is "." or ".."
            || name.EndsWith('.') || name.EndsWith(' ') || name.Any(c => c < 32 || "/\\:<>|?*".Contains(c)))
            throw new ArgumentException("更新名称必须是单个目录项。", nameof(name));
        ValidateTree(Path);
        return System.IO.Path.Combine(Path, name);
    }

    private static void ValidateTree(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            var attributes = directory.Attributes;
            if (directory.LinkTarget is not null || (attributes & FileAttributes.ReparsePoint) != 0
                || (attributes & FileAttributes.Directory) == 0)
                throw new IOException("便携更新目录不能包含链接或重解析点。");
        }
    }

    private static void ValidateFile(string path, bool allowMissing)
    {
        var file = new FileInfo(path);
        if (file.LinkTarget is not null) throw new IOException("便携更新文件不能是链接。");
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
                throw new IOException("便携更新文件必须是普通文件。");
        }
        catch (FileNotFoundException) when (allowMissing) { }
    }

    // Per-file WriteThrough and Flush(true) are used by the transaction. Portable storage
    // carries no promise about filesystem/physical power-loss durability.
    public void Flush() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose() => _disposed = true;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectoryWindows(string path, nint attributes);
    [LibraryImport("libc", EntryPoint = "mkdir", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Mkdir(string path, uint mode);
    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static partial int Flock(int file, int operation);
}
