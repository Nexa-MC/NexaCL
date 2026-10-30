using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nexa.Native.Markup.Xml;

namespace Nexa.Native.Markup;

internal static unsafe class XmlExports
{
    private static readonly NativeBufferResults Results = new(XmlSnapshotProtocol.OutputLimit);
    [UnmanagedCallersOnly(EntryPoint = "nexa_markup_xml_execute_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static long Execute(byte* input, int length, int* outputLength)
    {
        if (outputLength == null) return 0;
        *outputLength = 0;
        try
        {
            if (input == null || length < 17 || length > XmlSnapshotProtocol.InputLimit) return 0;
            using var output = new XmlSnapshotProtocol.Output();
            using var writer = new BinaryWriter(output);
            try
            {
                using var reader = new BinaryReader(new UnmanagedMemoryStream(input, length));
                byte text = reader.ReadByte();
                if (text > 1) throw new InvalidDataException("Invalid XML input mode.");
                var limits = new XmlSnapshotLimits(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                limits.Validate();
                int count = reader.ReadInt32();
                if (count < 0 || count != length - reader.BaseStream.Position) throw new InvalidDataException("Invalid XML input length.");
                var bytes = new ReadOnlySpan<byte>(input + checked((int)reader.BaseStream.Position), count);
                var document = text == 1 ? XmlSnapshotReader.ParseText(XmlSnapshotProtocol.Utf8.GetString(bytes), limits)
                    : XmlSnapshotReader.ParseBytes(bytes, limits);
                writer.Write(1); writer.Write(0); XmlSnapshotProtocol.WriteNodes(writer, document.AllNodes);
            }
            catch (Exception failure)
            {
                output.SetLength(0); output.Position = 0;
                writer.Write(1); writer.Write(failure is System.Xml.XmlException ? 1 : 2); XmlSnapshotProtocol.Text(writer, failure.Message);
            }
            return Results.Store(output.GetBuffer().AsMemory(0, checked((int)output.Length)), outputLength);
        }
        catch { return 0; }
    }
    [UnmanagedCallersOnly(EntryPoint = "nexa_markup_xml_copy_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Copy(long id, byte* output, int capacity) => Results.Copy(id, output, capacity);
    [UnmanagedCallersOnly(EntryPoint = "nexa_markup_xml_release_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(long id) => Results.Release(id);
}
