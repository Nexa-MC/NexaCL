using System.Buffers.Binary;

namespace Nexa.Platform;

/// <summary>Bounded PE inspection. OS architecture does not determine an emulated host's ABI.</summary>
public sealed class PlatformJvmArchitectureValidator : IPlatformJvmArchitectureValidator
{
    public IReadOnlyList<string> Validate(string hostExecutable, string javaExecutable, string? nativesDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return OperatingSystem.IsWindows() ? ValidateWindows(hostExecutable, javaExecutable, nativesDirectory, cancellationToken) : [];
    }

    internal static IReadOnlyList<string> ValidateWindows(string hostExecutable, string javaExecutable, string? nativesDirectory,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ushort expected = ReadMachine(hostExecutable);
        Require(javaExecutable);
        string java = new FileInfo(javaExecutable).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(javaExecutable);
        string bin = Path.GetDirectoryName(java)!;
        string home = Path.GetDirectoryName(bin)!;
        string[] candidates = [Path.Combine(bin, "server", "jvm.dll"), Path.Combine(home, "jre", "bin", "server", "jvm.dll")];
        string jvm = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("The selected Java runtime has no server jvm.dll. Select a complete JDK.");
        Require(jvm);
        List<string> warnings = [];
        if (!string.IsNullOrEmpty(nativesDirectory) && Directory.Exists(nativesDirectory))
        {
            int count = 0;
            foreach (string path in Directory.EnumerateFiles(nativesDirectory))
            {
                token.ThrowIfCancellationRequested();
                if (++count > 4096) throw new IOException("The native directory exceeds the architecture inspection limit.");
                if (!Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                // Legacy archives can contain both x86 and x64 DLLs. Only the game knows
                // which names it loads; mismatched siblings must not block a valid launch.
                try { Require(path); }
                catch (BadImageFormatException error) { if (warnings.Count < 16) warnings.Add(error.Message); }
            }
        }
        return warnings.AsReadOnly();

        void Require(string path)
        {
            token.ThrowIfCancellationRequested();
            ushort actual = ReadMachine(path);
            if (actual != expected)
                throw new BadImageFormatException($"Native architecture mismatch: {Path.GetFileName(path)} is {Name(actual)}; JVM Host requires {Name(expected)}. Select a matching Java runtime and native libraries.");
        }
    }

    internal static ushort ReadMachine(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("Native architecture inspection requires an absolute binary path.");
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> dos = stackalloc byte[64];
        if (file.Length < dos.Length) throw Invalid(path);
        file.ReadExactly(dos);
        if (dos[0] != 'M' || dos[1] != 'Z') throw Invalid(path);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(dos[60..]);
        // Only inspect headers, never allocate according to untrusted offsets or file size.
        if (offset < 64 || offset > 1024 * 1024 || offset > file.Length - 26) throw Invalid(path);
        file.Position = offset;
        Span<byte> pe = stackalloc byte[26];
        file.ReadExactly(pe);
        if (BinaryPrimitives.ReadUInt32LittleEndian(pe) != 0x00004550) throw Invalid(path);
        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(pe[4..]);
        if (machine is not (0x014c or 0x8664 or 0xaa64))
            throw new BadImageFormatException($"Unsupported native architecture 0x{machine:X4}: {Path.GetFileName(path)}.");
        ushort optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(pe[20..]);
        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(pe[24..]);
        if (optionalSize < 2 || offset + 24L + optionalSize > file.Length
            || magic != (machine == 0x014c ? 0x010b : 0x020b)) throw Invalid(path);
        return machine;
    }

    private static BadImageFormatException Invalid(string path) => new($"Invalid or incomplete PE binary: {Path.GetFileName(path)}.");
    private static string Name(ushort machine) => machine switch { 0x014c => "x86", 0x8664 => "x64", 0xaa64 => "ARM64", _ => "unknown" };
}
