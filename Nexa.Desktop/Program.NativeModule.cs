#if NEXA_NATIVE_MODULE
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexa.Desktop;

internal static partial class Program
{
    [UnmanagedCallersOnly(EntryPoint = "nexa_host_main_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int RunNativeHost(int argc, nint* argv, nint baseDirectory)
    {
        try
        {
            if (argc < 0 || argc > 65536 || (argc > 0 && argv == null) || baseDirectory == 0) return 2;
            string directory = Marshal.PtrToStringUTF8(baseDirectory) ?? throw new ArgumentException(null, nameof(baseDirectory));
            if (!Path.IsPathFullyQualified(directory)) return 2;
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", directory);
            if (OperatingSystem.IsWindows()) Thread.CurrentThread.SetApartmentState(ApartmentState.STA);
            string[] args = new string[argc];
            for (int i = 0; i < argc; i++)
                args[i] = Marshal.PtrToStringUTF8(argv[i]) ?? throw new ArgumentException(null, nameof(argv));
            return Main(args);
        }
        catch (Exception error)
        {
            // No managed exception may escape into the bootstrap's independent runtime.
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
#endif
