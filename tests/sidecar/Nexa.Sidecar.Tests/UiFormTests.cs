using System.Text;
using Nexa.Sidecar.Protocol;

namespace Nexa.Sidecar.Tests;

internal static partial class Program
{
    private static void UiFormsRejectMalformedAndAmbiguousInputs()
    {
        AssertEqual(512u, (uint)SidecarFeatures.UiForms);
        AssertEqual("943CA674D19370C40F6F8C01DAC3E05E059FA92BD2394E91CFE55BC28DD7982C", SidecarUiForms.SchemaFingerprint);
        byte[] encoded = SidecarUiForms.Encode("save", new Dictionary<string, string> { ["z"] = "中文", ["a"] = new('x', 70000) });
        var form = SidecarUiForms.Decode(encoded);
        AssertEqual("save", form.Argument); AssertEqual("a", form.Fields[0].Key);
        AssertEqual(70000, form.Fields[0].Utf8Value.Length);
        AssertEqual("中文", Encoding.UTF8.GetString(form.Fields[1].Utf8Value));
        var packet = SidecarDataMessages.EncodeBinaryRequest(1, new(SidecarWireCodecs.GeneratedDto, encoded));
        AssertTrue(SidecarDataMessages.DecodeBinaryRequest(packet).Value.Span.SequenceEqual(encoded));
        AssertThrows<InvalidDataException>(() => SidecarUiForms.Encode(new("", [new("same", []), new("same", [])])));
        AssertThrows<InvalidDataException>(() => SidecarUiForms.Encode(new("", [new("z", []), new("a", [])])));
        AssertThrows<DecoderFallbackException>(() => SidecarUiForms.Encode(new("", [new("bad", [255])])));
        AssertThrows<InvalidDataException>(() => SidecarUiForms.Encode(new("", [new("", [])])));
        AssertThrows<InvalidDataException>(() => SidecarUiForms.Encode(new("", [new(new('k', 257), [])])));
        AssertThrows<InvalidDataException>(() => SidecarUiForms.Encode(new("", [new("large", new byte[SidecarUiForms.MaximumValueLength + 1])])));
        AssertThrows<InvalidDataException>(() => SidecarUiForms.Encode("", Enumerable.Range(0, 129).ToDictionary(i => "field" + i, _ => "")));
        AssertThrows<InvalidDataException>(() => SidecarUiForms.Encode("", Enumerable.Range(0, 8).ToDictionary(i => "field" + i, _ => new string('x', SidecarUiForms.MaximumValueLength))));
        AssertThrows<InvalidDataException>(() => SidecarUiForms.Decode(encoded.Concat(new byte[] { 0 }).ToArray()));
        encoded[8] ^= 1;
        AssertThrows<InvalidDataException>(() => SidecarUiForms.Decode(encoded));
        encoded[8] ^= 1;
        AssertThrows<InvalidDataException>(() => SidecarUiForms.Decode(encoded.AsMemory(0, encoded.Length - 1)));
        // A producer cannot bypass canonical key validation by building the wire itself.
        byte[] duplicate = SidecarUiForms.Encode(new("", [new("a", []), new("b", [])]));
        duplicate[^5] = (byte)'a';
        AssertThrows<InvalidDataException>(() => SidecarUiForms.Decode(duplicate));
    }
}
