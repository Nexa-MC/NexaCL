#if NEXA_EXTERNAL_MODULES
using System.Runtime.InteropServices;
using System.Text;

namespace Nexa.Native.Interop;

internal static class NativeEngineClient
{
    internal static nint Load(string module)
    {
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        // Keep native engines loaded for process lifetime; never resolve from CWD or PATH.
        return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, module + suffix));
    }

    internal static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    internal static string Message(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end >= 0 ? bytes[..end] : bytes);
    }
}
#endif
