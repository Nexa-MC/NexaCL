using System.Text.Json.Nodes;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static void ExplicitSchedulingValidatesSettingsAndScopedOverrides()
    {
        AssertTrue(MinecraftProcessScheduling.ParseCpuSets(" 4,0-2 ").SequenceEqual([0, 1, 2, 4]));
        AssertEqual(0, MinecraftProcessScheduling.ParseCpuSets("").Count);
        foreach (string invalid in new[] { "0,0", "2-1", "-1", "1024", "0,", "1-2-3", "1,1-3", "all", new string('1', 4097) })
            AssertTrue(SettingsPolicySchema.ByKey["game.cpu-sets"].Validate(new(SettingsOverrideMode.Custom, invalid)) is not null);
        AssertFalse(SettingsPolicySchema.ByKey["game.cpu-sets"].Exportable);
        var (_, policy) = PolicyFixture(new InMemorySettingsPort());
        string root = CreateTempDirectory();
        try
        {
            string instance = Path.Combine(root, "instance"); Directory.CreateDirectory(instance);
            AssertTrue(policy.Set(new("game.cpu-sets", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "0,2"))).IsSuccess);
            AssertTrue(policy.Set(new("game.process-quality", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "efficiency"))).IsSuccess);
            var request = new MinecraftLaunchRequest
            {
                VersionJson = new JsonObject(),
                VersionId = "test",
                InstanceDirectory = instance,
                MinecraftRootDirectory = root,
                PlayerName = "fixture",
                PlayerUuid = "fixture"
            };
            var selected = MinecraftLaunchCoordinator.ApplySettings(request, policy.Read(new(instance)).Value!);
            AssertTrue(selected.CpuSets.SequenceEqual([0, 2])); AssertEqual(MinecraftProcessQuality.Efficiency, selected.ProcessQuality);
            AssertTrue(policy.Set(new("game.cpu-sets", SettingsLayer.Instance, new(SettingsOverrideMode.Custom, ""), instance)).IsSuccess);
            AssertTrue(policy.Set(new("game.process-quality", SettingsLayer.Instance, new(SettingsOverrideMode.Custom, "default"), instance)).IsSuccess);
            selected = MinecraftLaunchCoordinator.ApplySettings(request, policy.Read(new(instance)).Value!);
            AssertEqual(0, selected.CpuSets.Count); AssertEqual(MinecraftProcessQuality.Default, selected.ProcessQuality);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask ExplicitSchedulingNativeLinuxUsesOnlyOwnedProcess()
    {
        if (!OperatingSystem.IsLinux()) return;
        string root = CreateTempDirectory();
        try
        {
            string allowed = File.ReadLines("/proc/self/status").Single(x => x.StartsWith("Cpus_allowed_list:", StringComparison.Ordinal)).Split(':', 2)[1];
            int cpu = MinecraftProcessScheduling.ParseCpuSets(allowed)[0];
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Path.Combine(AppContext.BaseDirectory, "Nexa.Services.Tests"), "--scheduling-child")
            { UseShellExecute = false, RedirectStandardOutput = true })!;
            try
            {
                AssertEqual("ready", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
                var runtime = new Nexa.Platform.PlatformJvmRuntime();
                AssertTrue(runtime.SetCpuSets(process, [cpu]).Succeeded);
                AssertTrue(runtime.SetQuality(process, Nexa.Platform.PlatformProcessQuality.Efficiency).Succeeded);
                string[] threads = Directory.GetDirectories($"/proc/{process.Id}/task");
                AssertTrue(threads.Length >= 5);
                foreach (string thread in threads)
                {
                    string actual = File.ReadLines(Path.Combine(thread, "status")).Single(x => x.StartsWith("Cpus_allowed_list:", StringComparison.Ordinal)).Split(':', 2)[1].Trim();
                    AssertEqual(cpu.ToString(System.Globalization.CultureInfo.InvariantCulture), actual);
                    string stat = File.ReadAllText(Path.Combine(thread, "stat"));
                    AssertEqual("19", stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[16]);
                }
            }
            finally { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask ExplicitSchedulingAppliesAndOwnsFailedProcesses()
    {
        string root = CreateTempDirectory();
        try
        {
            List<string> order = [];
            await using var processes = new MinecraftProcessService(new LongLivedProcessPort());
            var hooks = new HookFixturePort(order);
            var host = new HookFixtureHost(processes, order);
            var executor = new MinecraftLaunchExecutor(host, null, hooks);
            var plan = HookPlan(root);
            var session = await executor.ExecuteAsync(plan, "default-scheduling");
            AssertFalse(order.Any(x => x.StartsWith("cpu:", StringComparison.Ordinal) || x.StartsWith("quality:", StringComparison.Ordinal)));
            session.Cancel(); await session.WaitForExitAsync();
            order.Clear();
            var configured = plan with
            {
                CpuSets = [0, 2],
                ProcessQuality = MinecraftProcessQuality.Efficiency,
                ProcessPriority = System.Diagnostics.ProcessPriorityClass.Normal,
                PreLaunchCommand = "fixture",
                WaitForPreLaunchCommand = false
            };
            bool wrapperRejected = false;
            try { await executor.ExecuteAsync(configured with { WrapperCommand = "fixture-wrapper" }, "wrapped-scheduling"); }
            catch (InvalidOperationException error) when (error.Message.Contains("direct JVM Host", StringComparison.Ordinal)) { wrapperRejected = true; }
            AssertTrue(wrapperRejected); AssertFalse(order.Contains("jvm_start"));
            session = await executor.ExecuteAsync(configured, "explicit-scheduling");
            AssertTrue(order.Contains("cpu:0,2")); AssertTrue(order.Contains("quality:Efficiency"));
            AssertTrue(order.IndexOf("priority") < order.IndexOf("quality:Efficiency"));
            AssertTrue(order.IndexOf("quality:Efficiency") < order.IndexOf("hook_detach"));
            session.Cancel(); await session.WaitForExitAsync();
            host.RejectScheduling = true;
            bool rejected = false;
            try { await executor.ExecuteAsync(configured, "rejected-scheduling"); }
            catch (InvalidOperationException error) when (error.Message.Contains("CPU selection", StringComparison.Ordinal)) { rejected = true; }
            AssertTrue(rejected); AssertTrue(hooks.Last!.Disposed); AssertFalse(hooks.Last.Detached);
            AssertTrue(processes.ListSessions().All(x => x.State is not (MinecraftProcessState.Created or MinecraftProcessState.Running)));
            rejected = false;
            try { await executor.ExecuteAsync(configured with { CpuSets = [] }, "rejected-quality"); }
            catch (InvalidOperationException error) when (error.Message.Contains("Process quality", StringComparison.Ordinal)) { rejected = true; }
            AssertTrue(rejected); AssertTrue(hooks.Last!.Disposed); AssertFalse(hooks.Last.Detached);
            AssertTrue(processes.ListSessions().All(x => x.State is not (MinecraftProcessState.Created or MinecraftProcessState.Running)));
        }
        finally { Directory.Delete(root, true); }
    }
}
