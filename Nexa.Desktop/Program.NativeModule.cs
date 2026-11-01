#if NEXA_NATIVE_MODULE
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexa.Desktop;

internal static class NativeUiExports
{
    [UnmanagedCallersOnly(EntryPoint = "nexa_ui_identity_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int NativeUiIdentity(byte* output, int capacity)
    {
        ReadOnlySpan<byte> identity = "nexa-ui/2"u8;
        if (output != null && capacity >= identity.Length) identity.CopyTo(new Span<byte>(output, capacity));
        return identity.Length;
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_ui_main_v2", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int RunNativeUi(int argc, nint* argv, nint baseDirectory, nint productVersion)
    {
        try
        {
            if (argc < 0 || argc > 65536 || (argc > 0 && argv == null) || baseDirectory == 0 || productVersion == 0) return 2;
            string directory = Marshal.PtrToStringUTF8(baseDirectory) ?? throw new ArgumentException(null, nameof(baseDirectory));
            if (!Path.IsPathFullyQualified(directory)) return 2;
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", directory);
            string version = Marshal.PtrToStringUTF8(productVersion) ?? throw new ArgumentException(null, nameof(productVersion));
            if (version.Length > 64 || !Nexa.Services.Updates.UpdateVersion.TryParse(version, out var parsed) || parsed.ToString() != version) return 2;
            NativeUiBuildIdentity.ProductVersion = version;
            if (OperatingSystem.IsWindows()) Thread.CurrentThread.SetApartmentState(ApartmentState.STA);
            string[] args = new string[argc];
            for (int i = 0; i < argc; i++)
                args[i] = Marshal.PtrToStringUTF8(argv[i]) ?? throw new ArgumentException(null, nameof(argv));
            return Program.RunNativeApplication(args);
        }
        catch (Exception error)
        {
            // No managed exception may escape into the bootstrap's independent runtime.
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}

internal static class NativeUiBuildIdentity
{
    internal static string? ProductVersion;
}

internal static partial class Program
{
    internal static int RunNativeApplication(string[] args) => Main(args);
}
#endif
