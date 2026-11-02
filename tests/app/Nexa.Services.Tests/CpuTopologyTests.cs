using System.Buffers.Binary;
using System.Diagnostics;
using Nexa.Platform;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.ModLoaders;
using Nexa.Services.Minecraft.Process;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static void TopologySelectionRespectsObservedCapabilities()
    {
        PlatformCpuTopology.Cpu[] cpus = [new(2, 100, 0, 32, "a"), new(3, 100, 0, 32, "a"), new(7, 200, 1, 96, "b")];
        AssertTrue(PlatformCpuTopology.ChooseProcessors(cpus, "performance").SequenceEqual([7]));
        AssertTrue(PlatformCpuTopology.ChooseProcessors(cpus, "cache").SequenceEqual([7]));
        AssertTrue(PlatformCpuTopology.ChooseProcessors(cpus, "numa").SequenceEqual([2, 3]));
        foreach (string preference in new[] { "performance", "cache", "numa", "invalid" })
        {
            bool rejected = false;
            try { PlatformCpuTopology.ChooseProcessors([new(0, null, null, null, null)], preference); }
            catch (InvalidOperationException) { rejected = true; }
            AssertTrue(rejected);
        }
        byte[] sets = new byte[64], cache = new byte[56];
        for (int i = 0; i < 2; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(sets.AsSpan(i * 32), 32);
            sets[i * 32 + 14] = (byte)i; sets[i * 32 + 18] = (byte)i;
        }
        sets[51] = 2; // CPU 1 allocated to a different process.
        BinaryPrimitives.WriteUInt32LittleEndian(cache, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(cache.AsSpan(4), 56);
        cache[8] = 3; BinaryPrimitives.WriteUInt32LittleEndian(cache.AsSpan(12), 96 * 1048576);
        BinaryPrimitives.WriteUInt64LittleEndian(cache.AsSpan(40), 3);
        var observed = PlatformCpuTopology.ParseWindows(sets, cache);
        AssertEqual(1, observed.Length); AssertEqual(0, observed[0].Index);
        AssertEqual(96L * 1048576, observed[0].CacheBytes!.Value);
        AssertEqual(0, PlatformCpuTopology.ParseWindows(sets, cache, new HashSet<uint> { 123 }).Length);
        bool malformed = false;
        try { PlatformCpuTopology.ParseWindows(sets.AsSpan(0, 31), cache); }
        catch (InvalidDataException) { malformed = true; }
        AssertTrue(malformed);
        if (OperatingSystem.IsLinux())
        {
            int[] allowed = MinecraftProcessScheduling.ParseCpuSets(File.ReadLines("/proc/self/status")
                .Single(line => line.StartsWith("Cpus_allowed_list:", StringComparison.Ordinal)).Split(':', 2)[1]).ToArray();
            foreach (string preference in new[] { "cache", "numa" })
            {
                var selected = new PlatformCpuTopology().ChooseProcessors(preference);
                AssertTrue(selected.Count > 0); AssertTrue(selected.All(allowed.Contains));
            }
        }
    }

    private static async ValueTask TopologyAdmissionPrecedesBootstrap()
    {
        string executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Nexa.Services.Tests.exe" : "Nexa.Services.Tests");
        MinecraftLaunchPlan plan = new("java", Path.GetTempPath(), ["-cp", "fixture", "fixture.Main", "", "private-token"],
            ["fixture"], [], new MinecraftModLoaderDescriptor(MinecraftModLoaderKind.Vanilla, null, "fixture.Main", []))
        { MainClassIndex = 2, CpuPolicy = "cache" };
        var native = new TopologyFixture();
        await using var service = new MinecraftProcessService(jvmHostExecutable: executable) { CpuTopology = native, SchedulingRuntime = native };
        var session = await service.StartAsync(plan, "topology");
        AssertEqual(1, native.Calls);
        AssertEqual(0, await session.WaitForExitAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)));
        native.Reject = true;
        bool failed = false;
        try { await service.StartAsync(plan, "topology-refused"); }
        catch (InvalidOperationException) { failed = true; }
        AssertTrue(failed); AssertTrue(native.Child!.HasExited);
        foreach (var incompatible in new[] { plan with { CpuSets = [0] }, plan with { WrapperCommand = "wrapper" } })
        {
            failed = false;
            try { await service.StartAsync(incompatible, "conflict"); }
            catch (InvalidOperationException) { failed = true; }
            AssertTrue(failed);
        }
        AssertEqual(2, native.Calls);
    }

    private sealed class TopologyFixture : IPlatformCpuTopology, IPlatformJvmRuntime
    {
        public bool Reject; public int Calls; public Process? Child;
        public IReadOnlyList<int> ChooseProcessors(string preference) { AssertEqual("cache", preference); return [2, 3]; }
        public PlatformProcessControlResult SetCpuSets(Process process, IReadOnlyList<int> cpus)
        {
            Calls++; Child = process; AssertFalse(process.HasExited); AssertTrue(cpus.SequenceEqual([2, 3]));
            return new(!Reject, Reject ? "refused" : "ok", "fixture");
        }
        public bool CpuSetsSupported => true;
        public bool QualitySupported => false;
        public bool CommitSupported => false;
        public bool GpuSupported => false;
        public bool TreeSupported => false;
        public bool SystemEventsSupported => false;
        public PlatformProcessControlResult SetQuality(Process process, PlatformProcessQuality quality) => throw new NotSupportedException();
        public PlatformJvmSample ReadSample(Process process) => throw new NotSupportedException();
        public ValueTask<PlatformJvmMemoryOutput> ReadJvmMemoryAsync(string path, int pid) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<PlatformSystemEvent>?> ReadSystemEventsAsync(int pid, DateTimeOffset since, DateTimeOffset until, CancellationToken token = default) => throw new NotSupportedException();
    }
}
