using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nexa.Native.MarkupWire;

namespace Nexa.Native.Markup;

internal static unsafe class Exports
{
    private static readonly NativeBufferResults Results = new(MarkupProtocol.OutputLimit);
    [UnmanagedCallersOnly(EntryPoint = "nexa_markup_execute_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static long Execute(byte* input, int length, int* outputLength)
    {
        if (outputLength == null) return 0;
        *outputLength = 0;
        try
        {
            if (length < 0 || length > MarkupProtocol.InputLimit || (length > 0 && input == null)) return 0;
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            try
            {
                string source = MarkupProtocol.Utf8.GetString(new ReadOnlySpan<byte>(input, length));
                NxmlDocument document = NxmlParser.Parse(source);
                writer.Write(1); writer.Write(0); // Wire version, success/error discriminator.
                MarkupProtocol.WriteNode(writer, document.Root);
            }
            catch (NxmlParseException failure)
            {
                output.SetLength(0); output.Position = 0;
                writer.Write(1); writer.Write(1); MarkupProtocol.Text(writer, failure.Message);
            }
            return Results.Store(output.GetBuffer().AsMemory(0, checked((int)output.Length)), outputLength);
        }
        catch { return 0; }
    }
    [UnmanagedCallersOnly(EntryPoint = "nexa_markup_copy_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Copy(long id, byte* output, int capacity) => Results.Copy(id, output, capacity);
    [UnmanagedCallersOnly(EntryPoint = "nexa_markup_release_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(long id) => Results.Release(id);
}
