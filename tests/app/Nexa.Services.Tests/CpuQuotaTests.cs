using Nexa.Platform;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.ModLoaders;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask CpuQuotaPrecedesBootstrapAndFailureReapsHost()
    {
        string executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Nexa.Services.Tests.exe" : "Nexa.Services.Tests");
        MinecraftLaunchPlan plan = new("java", Path.GetTempPath(), ["-cp", "fixture", "fixture.Main", "", "private-token"],
            ["fixture"], [], new MinecraftModLoaderDescriptor(MinecraftModLoaderKind.Vanilla, null, "fixture.Main", []))
        { MainClassIndex = 2, CpuQuotaPercent = 150 };
        var quota = new RecordingCpuQuota(true);
        await using (var service = new MinecraftProcessService(jvmHostExecutable: executable) { CpuQuota = quota })
        {
            var session = await service.StartAsync(plan, "quota-fixture");
            AssertEqual(1, quota.Calls);
            AssertEqual(0, await session.WaitForExitAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)));
            await service.StartAsync(plan with { CpuQuotaPercent = 0 }, "quota-default");
            AssertEqual(1, quota.Calls);
        }
        quota = new RecordingCpuQuota(false);
        await using (var service = new MinecraftProcessService(jvmHostExecutable: executable) { CpuQuota = quota })
        {
            bool failed = false;
            try { await service.StartAsync(plan, "quota-failed"); }
            catch (InvalidOperationException) { failed = true; }
            AssertTrue(failed); AssertTrue(quota.Child!.HasExited);
            AssertTrue(service.ListSessions().All(s => s.State is not (MinecraftProcessState.Created or MinecraftProcessState.Running)));
            try { await service.StartAsync(plan with { WrapperCommand = "wrapper" }, "quota-wrapper"); AssertTrue(false); }
            catch (InvalidOperationException) { }
            AssertEqual(1, quota.Calls);
        }
    }

    private static async ValueTask ResourceQuotaValidatesMemoryAndIoEvidence()
    {
        AssertTrue(PlatformCpuQuota.MatchesMemoryLimit("536870912\n", 536870912));
        foreach (string invalid in new[] { "max", "0", "-1", "536870913" })
            AssertFalse(PlatformCpuQuota.MatchesMemoryLimit(invalid, 536870912));
        AssertTrue(PlatformCpuQuota.MatchesIoLimit("8:0 rbps=1048576 wbps=2097152 riops=max wiops=max\n", 1048576, 2097152));
        AssertTrue(PlatformCpuQuota.MatchesIoLimit("8:0 rbps=1048576 wbps=max", 1048576, 0));
        foreach (string invalid in new[] { "", "8:0 rbps=max wbps=max", "8:0 rbps=1048577", "8:0 rbps=1 rbps=1", "invalid rbps=1", "8:0 rbps=1\n8:1 rbps=1" })
            AssertFalse(PlatformCpuQuota.MatchesIoLimit(invalid, 1048576, 0));
        var limits = new PlatformProcessQuota(150, 536870912, 1048576, 2097152, "/game path");
        string[] arguments = PlatformCpuQuota.StartArguments("nexacl-fixture.scope", 42, limits);
        AssertTrue(arguments.Contains("MemoryMax")); AssertTrue(arguments.Contains("536870912"));
        AssertTrue(arguments.Contains("IOReadBandwidthMax")); AssertTrue(arguments.Contains("IOWriteBandwidthMax"));
        AssertEqual(2, arguments.Count(a => a == "/game path"));
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        IPlatformCpuQuota oldAdapter = new RecordingCpuQuota(true);
        AssertFalse((await oldAdapter.ApplyAsync(process, limits)).Succeeded);
        var (_, policy) = PolicyFixture(new InMemorySettingsPort());
        string root = CreateTempDirectory();
        try
        {
            var request = new MinecraftLaunchRequest
            {
                VersionJson = new System.Text.Json.Nodes.JsonObject(),
                VersionId = "test",
                InstanceDirectory = root,
                MinecraftRootDirectory = root,
                PlayerName = "fixture",
                PlayerUuid = "fixture"
            };
            foreach (string key in new[] { "game.process-memory-limit-mib", "game.io-read-limit-mib", "game.io-write-limit-mib" })
            {
                AssertFalse(SettingsPolicySchema.ByKey[key].Exportable);
                AssertTrue(policy.Set(new(key, SettingsLayer.Global, new(SettingsOverrideMode.Custom, "512"))).IsSuccess);
            }
            var selected = MinecraftLaunchCoordinator.ApplySettings(request, policy.Read(new(root)).Value!);
            AssertEqual(512L, selected.ProcessMemoryLimitMiB); AssertEqual(512L, selected.IoReadLimitMiB); AssertEqual(512L, selected.IoWriteLimitMiB);
            AssertTrue(policy.Set(new("game.process-memory-limit-mib", SettingsLayer.Instance, new(SettingsOverrideMode.Custom, "0"), root)).IsSuccess);
            AssertEqual(0L, MinecraftLaunchCoordinator.ApplySettings(request, policy.Read(new(root)).Value!).ProcessMemoryLimitMiB);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class RecordingCpuQuota(bool succeed) : IPlatformCpuQuota
    {
        internal int Calls;
        internal System.Diagnostics.Process? Child;
        public async ValueTask<PlatformProcessControlResult> ApplyAsync(System.Diagnostics.Process process, int percent, CancellationToken token = default)
        {
            Calls++; Child = process;
            AssertEqual(150, percent);
            await Task.Delay(100, token);
            // The fixture exits after bootstrap. Still alive proves admission precedes its transfer.
            AssertFalse(process.HasExited);
            return new(succeed, succeed ? "ok" : "quota_refused", "fixture quota result");
        }
    }

    private static async ValueTask CpuQuotaValidatesKernelEvidenceAndDefaults()
    {
        AssertTrue(PlatformCpuQuota.MatchesQuota("150000 100000\n", 150));
        foreach (string invalid in new[] { "max 100000", "150001 100000", "-1 100000", "0 100000", "100 0", "garbage", "9223372036854775807 100000" })
            AssertFalse(PlatformCpuQuota.MatchesQuota(invalid, 150));
        string[] args = PlatformCpuQuota.StartArguments("nexacl-fixture.scope", 42, 150);
        AssertTrue(args.Contains("--user")); AssertTrue(args.Contains("1500000")); AssertTrue(args.Contains("42"));
        AssertFalse(args.Contains("--system"));
        var schema = SettingsPolicySchema.ByKey["game.cpu-quota-percent"];
        AssertEqual("0", schema.DefaultValue); AssertTrue(schema.InstanceOverride);
        foreach (string value in new[] { "-1", "102401", "1.5" })
            AssertTrue(schema.Validate(new(SettingsOverrideMode.Custom, value)) is not null);
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        AssertTrue((await new PlatformCpuQuota().ApplyAsync(current, 0)).Succeeded);
        AssertFalse((await new PlatformCpuQuota().ApplyAsync(current, -1)).Succeeded);
        if (OperatingSystem.IsLinux())
        {
            var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Nexa.Services.Tests"))
            { UseShellExecute = false, RedirectStandardInput = true };
            info.ArgumentList.Add("--jvm-host");
            using var child = System.Diagnostics.Process.Start(info)!;
            try
            {
                var native = await new PlatformCpuQuota().ApplyAsync(child, 150);
                AssertTrue(native.Succeeded || native.Code is "quota_refused" or "quota_unavailable" or "dependency_missing");
                Console.WriteLine("CPU quota native adapter: " + native.Code);
                if (native.Succeeded)
                {
                    var memory = await new PlatformCpuQuota().ApplyAsync(child, new PlatformProcessQuota(150, 536870912));
                    AssertTrue(memory.Succeeded || memory.Code is "quota_refused" or "quota_unavailable" or "dependency_missing");
                    Console.WriteLine("Memory quota native adapter: " + memory.Code);
                }
                AssertFalse(child.HasExited);
            }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
        }
    }
}
