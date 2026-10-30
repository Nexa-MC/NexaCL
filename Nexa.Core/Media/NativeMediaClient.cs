#if NEXA_EXTERNAL_MODULES
using System.Runtime.InteropServices;
using System.Text;

namespace Nexa.Core.Media;

// Kept internal: this is a release-coherent native boundary, not a plugin API.
internal static unsafe class NativeMediaClient
{
    private static readonly nint Library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,
        "nexa-media" + (OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so")));
    private static readonly delegate* unmanaged[Cdecl]<byte*, int, int, int, int*, int*, byte*, int, int> InspectNative =
        (delegate* unmanaged[Cdecl]<byte*, int, int, int, int*, int*, byte*, int, int>)NativeLibrary.GetExport(Library, "nexa_media_inspect_v1");

    internal static bool Inspect(byte[] bytes, int mode, bool slim, out int width, out int height, out string error)
    {
        Span<byte> diagnostic = stackalloc byte[2048];
        diagnostic.Clear();
        int w = 0, h = 0, status;
        fixed (byte* input = bytes)
        fixed (byte* message = diagnostic)
            status = InspectNative(input, bytes.Length, mode, slim ? 1 : 0, &w, &h, message, diagnostic.Length);
        int end = diagnostic.IndexOf((byte)0);
        error = Encoding.UTF8.GetString(end < 0 ? diagnostic : diagnostic[..end]);
        width = w; height = h;
        if (status < 0) throw new InvalidOperationException("Native media engine failed: " + error);
        return status == 0;
    }
}
#endif
