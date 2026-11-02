using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nexa.Platform;

public static partial class PlatformFileClone
{
    /// <summary>Creates a distinct CoW file, or returns false with no destination on unsupported storage.</summary>
    public static bool TryClone(string source, string destination)
    {
        if (!Path.IsPathFullyQualified(source) || !Path.IsPathFullyQualified(destination))
            throw new ArgumentException("Clone paths must be absolute.");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Clone destination already exists.");
        if ((File.GetAttributes(source) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("Clone source must be a regular file.");
        if (OperatingSystem.IsMacOS())
        {
            if (clonefile(source, destination, 0) == 0)
            { File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite); return true; }
            // clonefile is exclusive-create. Never unlink a destination another caller created.
            if (File.Exists(destination)) throw new IOException("Clone failed with an existing destination.");
            return false;
        }
        if (!OperatingSystem.IsLinux()) return false;
        bool cloned = false;
        using SafeFileHandle input = File.OpenHandle(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using (SafeFileHandle output = File.OpenHandle(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            try
            {
                cloned = ioctl(output, 0x40049409, input) == 0; // linux/fs.h FICLONE
                if (cloned) RandomAccess.FlushToDisk(output);
            }
            catch { output.Dispose(); File.Delete(destination); throw; }
        }
        if (!cloned) File.Delete(destination);
        return cloned;
    }

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int clonefile(string source, string destination, uint flags);
    [LibraryImport("libc", SetLastError = true)]
    private static partial int ioctl(SafeFileHandle destination, nuint request, SafeFileHandle source);
}
