using System.Runtime.InteropServices;
using System.Text;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static unsafe void NativeInstrumentationWorks()
    {
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        foreach (string engine in new[] { "pac", "crypto", "network", "updates", "media", "minecraft", "markup" })
        {
            // NativeAOT runtimes stay loaded for the process lifetime.
            nint library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nexa-" + engine + suffix));
            var target = (delegate* unmanaged[Cdecl]<int, byte*, int, int>)NativeLibrary.GetExport(library, "nexa_instrumentation_target_v1");
            var configure = (delegate* unmanaged[Cdecl]<int, int, int>)NativeLibrary.GetExport(library, "nexa_instrumentation_probe_v1");
            var hits = (delegate* unmanaged[Cdecl]<int, long>)NativeLibrary.GetExport(library, "nexa_instrumentation_hits_v1");
            int count = target(-1, null, 0), selected = -1, infrastructure = -1;
            if (count <= 0) throw new InvalidOperationException("Missing native function catalog.");
            for (int index = 0; index < count; index++)
            {
                int size = target(index, null, 0);
                if (size <= 0 || size > 65536) throw new InvalidOperationException("Invalid target size.");
                byte[] bytes = new byte[size];
                fixed (byte* buffer = bytes)
                {
                    buffer[0] = 0x7f;
                    AssertEqual(size, target(index, buffer, size - 1));
                    AssertEqual((byte)0x7f, buffer[0]);
                    AssertEqual(size, target(index, buffer, size));
                }
                string entry = Encoding.UTF8.GetString(bytes);
                if (entry.Contains("\t3\t", StringComparison.Ordinal)) infrastructure = index;
                string method = engine == "pac" ? "::Evaluate``0(" : engine == "media" ? "::Inspect``0(" : engine is "minecraft" or "markup" ? "::Execute``0(" : "::Create``0(";
                if (entry.Contains(".Exports" + method, StringComparison.Ordinal)) selected = index;
            }
            if (selected < 0 || infrastructure < 0) throw new InvalidOperationException("Incomplete native inventory.");
            AssertEqual(-1, configure(infrastructure, 1));
            AssertEqual(-1, configure(count, 1));
            AssertEqual(-1, target(count, null, 0));
            AssertEqual(0, configure(selected, 1));
            AssertEqual(0, configure(selected, 1));
            AssertEqual(0L, hits(selected));
            if (engine is "minecraft" or "markup")
            {
                var invoke = (delegate* unmanaged[Cdecl]<byte*, int, int*, long>)NativeLibrary.GetExport(library, "nexa_" + engine + "_execute_v1");
                AssertEqual(0L, invoke(null, 0, null));
            }
            else if (engine == "media")
            {
                var invoke = (delegate* unmanaged[Cdecl]<byte*, int, int, int, int*, int*, byte*, int, int>)NativeLibrary.GetExport(library, "nexa_media_inspect_v1");
                AssertEqual(-1, invoke(null, 0, 0, 0, null, null, null, 0));
            }
            else if (engine == "network")
            {
                var invoke = (delegate* unmanaged[Cdecl]<int, int, int, int, nint*, int>)NativeLibrary.GetExport(library, "nexa_network_create_v1");
                AssertEqual(-1, invoke(0, 0, 0, 0, null));
            }
            else if (engine == "updates")
            {
                var invoke = (delegate* unmanaged[Cdecl]<int, nint, long, long>)NativeLibrary.GetExport(library, "nexa_updates_create_v1");
                AssertEqual(0L, invoke(0, 0, 0));
            }
            else if (engine == "crypto")
            {
                var invoke = (delegate* unmanaged[Cdecl]<byte*, int, nint*, byte*, int, int>)NativeLibrary.GetExport(library, "nexa_crypto_create_v1");
                AssertEqual(-1, invoke(null, 0, null, null, 0));
            }
            else
            {
                var invoke = (delegate* unmanaged[Cdecl]<byte*, int, nint, nint, byte*, int, int>)NativeLibrary.GetExport(library, "nexa_pac_evaluate_v1");
                AssertEqual(-1, invoke(null, 0, 0, 0, null, 0));
            }
            AssertEqual(1L, hits(selected));
            AssertEqual(0, configure(selected, 0));
            AssertEqual(-1L, hits(selected));
            AssertEqual(0, configure(selected, 1));
            AssertEqual(0L, hits(selected));
            AssertEqual(0, configure(selected, 0));
            Console.WriteLine(engine + ": native instrumentation activation, entry counting and retirement passed.");
        }
    }
}
