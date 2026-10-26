using System.Buffers.Binary;
using Nexa.Platform;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.ModLoaders;
using Nexa.Services.Minecraft.Process;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static void NativeArchitectureValidatesRequiredBinariesAndMixedArchives()
    {
        string root = Path.Combine(Path.GetTempPath(), "nexa-pe-" + Guid.NewGuid().ToString("N"));
        try
        {
            string host = Path.Combine(root, "host.exe"), java = Path.Combine(root, "jdk", "bin", "java.exe");
            string jvm = Path.Combine(root, "jdk", "bin", "server", "jvm.dll"), natives = Path.Combine(root, "natives");
            Directory.CreateDirectory(natives);
            foreach (ushort machine in new ushort[] { 0x014c, 0x8664, 0xaa64 })
            {
                WritePe(host, machine); WritePe(java, machine); WritePe(jvm, machine);
                AssertEqual(0, PlatformJvmArchitectureValidator.ValidateWindows(host, java, natives).Count);
            }
            WritePe(host, 0x8664); WritePe(java, 0x8664); WritePe(jvm, 0x8664);
            WritePe(Path.Combine(natives, "lwjgl.dll"), 0x014c);
            WritePe(Path.Combine(natives, "lwjgl64.dll"), 0x8664);
            var warnings = PlatformJvmArchitectureValidator.ValidateWindows(host, java, natives);
            AssertEqual(1, warnings.Count); AssertTrue(warnings[0].Contains("lwjgl.dll", StringComparison.Ordinal));
            WritePe(jvm, 0xaa64);
            ExpectBad(() => PlatformJvmArchitectureValidator.ValidateWindows(host, java, natives));
            WritePe(jvm, 0x8664); WritePe(java, 0x014c);
            ExpectBad(() => PlatformJvmArchitectureValidator.ValidateWindows(host, java, natives));
            WritePe(java, 0x8664);
            byte[] invalid = File.ReadAllBytes(host);
            BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(60), int.MaxValue);
            File.WriteAllBytes(host, invalid); ExpectBad(() => PlatformJvmArchitectureValidator.ReadMachine(host));
            File.WriteAllBytes(host, [0x4d, 0x5a]); ExpectBad(() => PlatformJvmArchitectureValidator.ReadMachine(host));
            WritePe(host, 0xa641); ExpectBad(() => PlatformJvmArchitectureValidator.ReadMachine(host));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { PlatformJvmArchitectureValidator.ValidateWindows(host, java, natives, cancelled.Token); throw new InvalidOperationException("Cancellation ignored."); }
            catch (OperationCanceledException) { }
        }
        finally { Directory.Delete(root, true); }

        static void ExpectBad(Action action)
        {
            try { action(); throw new InvalidOperationException("Invalid architecture accepted."); }
            catch (BadImageFormatException) { }
        }
        static void WritePe(string path, ushort machine)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] bytes = new byte[256]; bytes[0] = 0x4d; bytes[1] = 0x5a;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(60), 128);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(128), 0x4550);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(132), machine);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(148), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(152), machine == 0x014c ? (ushort)0x10b : (ushort)0x20b);
            File.WriteAllBytes(path, bytes);
        }
    }

    private static async ValueTask NativeArchitectureFailurePreventsProcessCreation()
    {
        var port = new RecordingArgumentPort();
        await using var service = new MinecraftProcessService(port, jvmHostExecutable: Path.GetFullPath("host.exe"))
        { ArchitectureValidator = new RejectingArchitectureValidator() };
        MinecraftLaunchPlan plan = new("java", Path.GetTempPath(), ["fixture.Main"], [], [],
            new MinecraftModLoaderDescriptor(MinecraftModLoaderKind.Vanilla, null, "fixture.Main", []));
        try { await service.StartAsync(plan, "architecture-test"); throw new InvalidOperationException("Launch was accepted."); }
        catch (BadImageFormatException) { }
        AssertEqual(0, port.Calls);
        AssertEqual(0, service.ListSessions().Count);
    }

    private sealed class RejectingArchitectureValidator : IPlatformJvmArchitectureValidator
    {
        public IReadOnlyList<string> Validate(string hostExecutable, string javaExecutable, string? nativesDirectory,
            CancellationToken cancellationToken = default) => throw new BadImageFormatException("Fixture mismatch.");
    }
}
