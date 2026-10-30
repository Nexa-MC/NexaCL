using System.Runtime.InteropServices;

namespace Nexa.Bootstrap;

internal static class Program
{
    [STAThread]
    private static unsafe int Main(string[] args)
    {
        string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        string module = Path.Combine(AppContext.BaseDirectory, "nexa-host" + extension);
        nint[] arguments = new nint[args.Length];
        nint baseDirectory = 0;
        try
        {
            // Absolute path: never resolve executable code against the working directory or PATH.
            nint library = NativeLibrary.Load(module);
            // NativeAOT shared libraries must remain loaded for the process lifetime.
            nint entry = NativeLibrary.GetExport(library, "nexa_host_main_v1");
            for (int i = 0; i < args.Length; i++) arguments[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            baseDirectory = Marshal.StringToCoTaskMemUTF8(AppContext.BaseDirectory);
            fixed (nint* argv = arguments)
            {
                var run = (delegate* unmanaged[Cdecl]<int, nint*, nint, int>)entry;
                return run(args.Length, argv, baseDirectory);
            }
        }
        catch (Exception error)
        {
            string message = $"Unable to start NexaCL. Restore the complete launcher installation.\nModule: {module}\n{error.Message}";
            Console.Error.WriteLine(message);
            if (OperatingSystem.IsWindows())
            {
                nint stderr = GetStdHandle(-12);
                if (stderr == 0 || stderr == -1) _ = MessageBox(0, message, "NexaCL", 0x10);
            }
            return 1;
        }
        finally
        {
            foreach (nint argument in arguments) Marshal.FreeCoTaskMem(argument);
            Marshal.FreeCoTaskMem(baseDirectory);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int handle);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint window, string text, string caption, uint type);
}
