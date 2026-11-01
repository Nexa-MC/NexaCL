using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexa.Native.Host;

internal static class HostExports
{
    private static int _entered;

    [UnmanagedCallersOnly(EntryPoint = "nexa_host_main_v2", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Main(int argc, nint* argv, nint baseDirectory, nint productVersion)
    {
        try
        {
            if (argc is < 0 or > 65536 || (argc > 0 && argv == null) || baseDirectory == 0 || productVersion == 0) return 2;
            string directory = Marshal.PtrToStringUTF8(baseDirectory) ?? throw new ArgumentException(null, nameof(baseDirectory));
            if (!Path.IsPathFullyQualified(directory)) return 2;
            if (Interlocked.Exchange(ref _entered, 1) != 0) return 2;
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", directory);
            string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
            string path = Path.Combine(directory, "nexa-ui" + suffix);
            // Images stay loaded. Resolve and validate the complete entry surface before
            // letting the UI initialize its dispatcher or admit application work.
            nint image = NativeLibrary.Load(path);
            var entry = (delegate* unmanaged[Cdecl]<int, nint*, nint, nint, int>)NativeLibrary.GetExport(image, "nexa_ui_main_v2");
            var identity = (delegate* unmanaged[Cdecl]<byte*, int, int>)NativeLibrary.GetExport(image, "nexa_ui_identity_v1");
            ReadOnlySpan<byte> expected = "nexa-ui/2"u8;
            if (identity(null, 0) != expected.Length) throw new InvalidDataException("Incompatible nexa-ui module identity.");
            byte[] actual = new byte[expected.Length];
            fixed (byte* output = actual)
                if (identity(output, actual.Length) != actual.Length || !actual.AsSpan().SequenceEqual(expected))
                    throw new InvalidDataException("Incompatible nexa-ui module identity.");
            // No arguments are decoded/re-encoded here, and this call remains on the
            // original process main thread required by Cocoa.
            int result = entry(argc, argv, baseDirectory, productVersion);
            GC.KeepAlive(directory);
            return result;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Unable to enter nexa-ui. Restore the complete launcher installation.");
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
