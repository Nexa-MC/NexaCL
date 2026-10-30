#if NEXA_EXTERNAL_MODULES
using System.Text;
using Nexa.Native.Interop;
using Nexa.Native.MarkupWire;

namespace Nexa.Xsr.Renderer.Nxml;

internal static class NativeMarkupClient
{
    private static readonly NativeResultClient Client = new("nexa-markup", "nexa_markup", MarkupProtocol.InputLimit, MarkupProtocol.OutputLimit);
    internal static NxmlDocument Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) throw new NxmlParseException("The NXML document is empty.");
        if (text.Length > 1024 * 1024) throw new NxmlParseException("The NXML document exceeds the character budget.");
        byte[] input;
        try { input = MarkupProtocol.Utf8.GetBytes(text); }
        catch (EncoderFallbackException) { throw new NxmlParseException("The NXML document is not well-formed XML or uses a prohibited DTD."); }
        using var reader = new BinaryReader(new MemoryStream(Client.Execute(input)));
        if (reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported native NXML response version.");
        int status = reader.ReadInt32();
        if (status == 1)
        {
            string message = MarkupProtocol.Text(reader); End(reader); throw new NxmlParseException(message);
        }
        if (status != 0) throw new InvalidDataException("Invalid native NXML response status.");
        int remaining = 16384;
        var root = MarkupProtocol.ReadNode(reader, ref remaining);
        End(reader); return new(root);
    }
    private static void End(BinaryReader reader)
    {
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing native NXML response data.");
    }
}
#endif
