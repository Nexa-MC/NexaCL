using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Nexa.Core.Xml;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static unsafe void NativeXmlWorks()
    {
        const string source = "<?xml version='1.0' encoding='utf-16'?><?probe test?><root xmlns:p='urn:test' p:a='值'>a<child>b<![CDATA[c]]></child>d<!-- ignored --></root>";
        var limits = new XmlSnapshotLimits(4096);
        var text = XmlSnapshotReader.ParseText(source, limits);
        byte[] encoded = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(source)];
        var bytes = XmlSnapshotReader.ParseBytes(encoded, limits);
        foreach (var document in new[] { text, bytes })
        {
            var root = document.Root!;
            AssertEqual("abcd", root.Value);
            AssertEqual(1, document.DescendantNodes().OfType<XmlSnapshotProcessingInstruction>().Count());
            AssertEqual("值", root.Attributes().Single(a => a.Name.NamespaceName == "urn:test").Value);
            AssertEqual(true, root.Attributes().Any(a => a.IsNamespaceDeclaration));
            AssertEqual<XmlSnapshotProperty?>(null, root.Attribute("a"));
            var child = root.Elements().Single();
            AssertEqual(true, ReferenceEquals(root, child.Parent));
            AssertEqual(true, ReferenceEquals(root, child.Ancestors().Single()));
            AssertEqual("bc", document.Descendants("child").Single().Value);
        }
        foreach (var xml in new[] { "<a>", "<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///missing'>]><a>&x;</a>", "<a x='1' x='2'/>", "<a>\ud800</a>" })
        {
            try { XmlSnapshotReader.ParseText(xml, limits); throw new InvalidOperationException("Invalid XML accepted."); }
            catch (XmlException) { }
        }
        try { XmlSnapshotReader.ParseText("<a><b/></a>", new(4096, 1)); throw new InvalidOperationException("Element budget ignored."); }
        catch (InvalidDataException) { }
        try { XmlSnapshotReader.ParseText("<a><b/></a>", new(4096, 10, 1)); throw new InvalidOperationException("Depth budget ignored."); }
        catch (InvalidDataException) { }
        try { XmlSnapshotReader.ParseText("<a>long text</a>", new(8)); throw new InvalidOperationException("Character budget ignored."); }
        catch (XmlException) { }
        Parallel.For(0, 32, _ => AssertEqual("root", XmlSnapshotReader.ParseText("<root/>", limits).Root!.Name.LocalName));

        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        nint library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nexa-markup" + suffix));
        var execute = (delegate* unmanaged[Cdecl]<byte*, int, int*, long>)NativeLibrary.GetExport(library, "nexa_markup_xml_execute_v1");
        var copy = (delegate* unmanaged[Cdecl]<long, byte*, int, int>)NativeLibrary.GetExport(library, "nexa_markup_xml_copy_v1");
        var release = (delegate* unmanaged[Cdecl]<long, void>)NativeLibrary.GetExport(library, "nexa_markup_xml_release_v1");
        int size = 9;
        AssertEqual(0L, execute(null, 17, &size)); AssertEqual(0, size);
        byte[] invalid = new byte[17]; invalid[0] = 2;
        long id;
        fixed (byte* request = invalid) id = execute(request, invalid.Length, &size);
        if (id <= 0 || size < 8) throw new InvalidOperationException("Missing invalid-mode response.");
        byte[] response = new byte[size];
        fixed (byte* output = response)
        {
            AssertEqual(-1, copy(id, output, size - 1));
            AssertEqual(0, copy(id, output, size));
            release(id); release(id);
            AssertEqual(-1, copy(id, output, size));
        }
        Console.WriteLine("Native XML caller: encoding, namespaces, mixed text, parent identity, DTD, budgets and retirement passed.");
    }
}
