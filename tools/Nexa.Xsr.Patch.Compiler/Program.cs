using System.Text;

namespace Nexa.Xsr.Patch.Compiler;

internal static partial class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["--self-test"]) return SelfTest();
            if (args is ["--self-test-automatic", "--core", var fixtureCore]) return AutomaticSelfTest(fixtureCore);
            if (args is ["--verify-automatic", var directory]) return AssemblyInstrumenter.VerifyDirectory(directory);
            if (args is ["--benchmark-automatic", "--core", var benchmarkCore]) return AutomaticBenchmark(benchmarkCore);
            if (args is ["--instrument", var assembly, "--core", var core, "--catalog", var catalog])
                return AssemblyInstrumenter.Run(assembly, core, catalog);
            if (args is ["--instrument", var target, "--core", var abi, "--catalog", var inventory, "--references", var references])
                return AssemblyInstrumenter.Run(target, abi, inventory, references);
            if (args is not ["--input", var input, "--output", var output])
                throw new InvalidDataException("NXRP001: expected --input FILE --output FILE or --self-test.");
            var source = new FileInfo(input);
            if (!source.Exists || source.Length > 2 * 1024 * 1024)
                throw new InvalidDataException("NXRP001: source is missing or exceeds the 2 MiB budget.");
            string destination = Path.GetFullPath(output);
            string debugPath = Path.GetRelativePath(Path.GetDirectoryName(destination)!, source.FullName);
            if (Path.IsPathRooted(debugPath)) throw new InvalidDataException("NXRP001: generated and input source must share a filesystem root.");
            string rewritten = Rewrite(File.ReadAllText(input, new UTF8Encoding(false, true)), debugPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, rewritten, new UTF8Encoding(false));
                File.Move(temporary, destination, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return 0;
        }
        catch (Exception error) when (error is IOException or ArgumentException)
        { Console.Error.WriteLine(error); return 1; }
    }
}
