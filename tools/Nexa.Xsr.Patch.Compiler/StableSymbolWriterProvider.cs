using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Nexa.Xsr.Patch.Compiler;

/// <summary>Keep atomic staging paths out of the PE debug identity and deterministic MVID.</summary>
internal sealed class StableSymbolWriterProvider : ISymbolWriterProvider
{
    public ISymbolWriter GetSymbolWriter(ModuleDefinition module, string fileName) =>
        new Writer(new PortablePdbWriterProvider().GetSymbolWriter(module, fileName), Path.ChangeExtension(module.Name, ".pdb"));

    public ISymbolWriter GetSymbolWriter(ModuleDefinition module, Stream symbolStream) =>
        new Writer(new PortablePdbWriterProvider().GetSymbolWriter(module, symbolStream), Path.ChangeExtension(module.Name, ".pdb"));

    private sealed class Writer(ISymbolWriter inner, string name) : ISymbolWriter
    {
        public ISymbolReaderProvider GetReaderProvider() => inner.GetReaderProvider();
        public void Write(MethodDebugInformation info) => inner.Write(info);
        public void Write() => inner.Write();
        public void Write(ICustomDebugInformationProvider info) => inner.Write(info);
        public void Dispose() => inner.Dispose();

        public ImageDebugHeader GetDebugHeader()
        {
            var entries = inner.GetDebugHeader().Entries.Select(entry =>
            {
                if (entry.Directory.Type != ImageDebugType.CodeView || entry.Data.Length < 24
                    || !entry.Data.AsSpan(0, 4).SequenceEqual("RSDS"u8)) return entry;
                byte[] path = Encoding.UTF8.GetBytes(name + "\0");
                byte[] data = new byte[24 + path.Length];
                entry.Data.AsSpan(0, 24).CopyTo(data);
                path.CopyTo(data, 24);
                var directory = entry.Directory;
                directory.SizeOfData = data.Length;
                return new ImageDebugHeaderEntry(directory, data);
            }).ToArray();
            return new ImageDebugHeader(entries);
        }
    }
}
