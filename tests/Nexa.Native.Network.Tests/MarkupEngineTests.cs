using System.Runtime.InteropServices;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static unsafe void NativeMarkupWorks()
    {
        var document = NxmlParser.Parse("""<Node Content="你好 🌍" Label="{state account.name}"><Node Content="Hi {data name}" /></Node>""");
        AssertEqual("Node", document.Root.Name);
        AssertEqual("你好 🌍", document.Root.FindProperty("Content")!.Value.Text);
        AssertEqual(NxmlValueKind.StateBinding, document.Root.FindProperty("Label")!.Value.Kind);
        AssertEqual(NxmlValueKind.DataText, document.Root.Children[0].FindProperty("Content")!.Value.Kind);
        foreach (string source in new[]
        {
            "", "<Node>", "<Node Content=\"a\" Content=\"b\" />", "<Node xmlns=\"wrong\" />", "<Node>text</Node>",
            "<!DOCTYPE Node [<!ENTITY x SYSTEM 'file:///does-not-exist'>]><Node />",
            "<Node Content=\"{unknown value}\" />", "<Node Width=\"Hi {data name}\" />",
            "<Node Content=\"\ud800\" />", new string('a', 1024 * 1024 + 1),
            string.Concat(Enumerable.Repeat("<Node>", 129)) + string.Concat(Enumerable.Repeat("</Node>", 129)),
        })
        {
            try { NxmlParser.Parse(source); throw new InvalidOperationException("Invalid NXML was accepted."); }
            catch (NxmlParseException) { }
        }
        Parallel.For(0, 32, _ => AssertEqual("Node", NxmlParser.Parse("<Node />").Root.Name));
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        nint library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nexa-markup" + suffix));
        var execute = (delegate* unmanaged[Cdecl]<byte*, int, int*, long>)NativeLibrary.GetExport(library, "nexa_markup_execute_v1");
        var copy = (delegate* unmanaged[Cdecl]<long, byte*, int, int>)NativeLibrary.GetExport(library, "nexa_markup_copy_v1");
        var release = (delegate* unmanaged[Cdecl]<long, void>)NativeLibrary.GetExport(library, "nexa_markup_release_v1");
        int size = 7;
        AssertEqual(0L, execute(null, 1, &size)); AssertEqual(0, size);
        long id = execute(null, 0, &size);
        if (id <= 0 || size < 8) throw new InvalidOperationException("Missing NXML error response.");
        try
        {
            byte[] output = new byte[size];
            fixed (byte* buffer = output)
            {
                AssertEqual(-1, copy(id, buffer, size - 1)); AssertEqual(0, copy(id, buffer, size));
                AssertEqual(1, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(output));
                AssertEqual(1, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(4)));
            }
        }
        finally { release(id); release(id); }
        Console.WriteLine("Native markup caller: Unicode DOM, grammar, DTD rejection, limits, concurrent calls and result retirement passed.");
    }
}
