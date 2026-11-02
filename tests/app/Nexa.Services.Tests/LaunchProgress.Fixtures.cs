using System.Text.Json.Nodes;
using Nexa.Services.Accounts;
using Nexa.Services.Composition;
using Nexa.Services.Foundation;
using Nexa.Services.Logging;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Libraries;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{

    private static bool ReadProgressFlag(XsrStateStore store, XsrSemanticId key) =>
        store.ReadAppliedValue(store.Resolve(key)) is bool flag && flag;

    private static string ReadProgressText(XsrStateStore store, XsrSemanticId key) =>
        store.ReadAppliedValue(store.Resolve(key)) as string ?? string.Empty;

    private static double ReadProgressNumber(XsrStateStore store, XsrSemanticId key) =>
        store.ReadAppliedValue(store.Resolve(key)) is double value ? value : -1d;

    private sealed class RecordingProgressPublisher(XsrStateStore store)
        : MinecraftLaunchProgressPublisher(store)
    {
        public List<string> Stages { get; } = [];

        public List<double> Progress { get; } = [];

        public override void Report(MinecraftLaunchStageReport report)
        {
            if (report.Stage.Length > 0)
            {
                Stages.Add(report.Stage);
                Progress.Add(report.Progress);
            }

            base.Report(report);
        }
    }

    /// <summary>A process port that starts a child which exits immediately with code zero.</summary>
    /// <summary>
    /// A process port that returns a session whose process has already exited, so the terminal
    /// Changed event fires before the coordinator can subscribe.
    /// </summary>
    private sealed class ExitedBeforeReturnProcessPort : IMinecraftProcessPort
    {
        public bool UsesPrivateArgumentTransport => true;

        public ValueTask<System.Diagnostics.Process> StartAsync(
            System.Diagnostics.ProcessStartInfo startInfo,
            CancellationToken cancellationToken = default)
        {
            System.Diagnostics.ProcessStartInfo exit = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo("cmd", "/c exit 0")
                : new System.Diagnostics.ProcessStartInfo("/bin/sh", "-c exit 0");
            exit.UseShellExecute = false;
            exit.CreateNoWindow = true;
            System.Diagnostics.Process process = System.Diagnostics.Process.Start(exit)!;
            process.WaitForExit(5_000);
            return ValueTask.FromResult(process);
        }
    }

    /// <summary>A probe that always reports the game window as present.</summary>
    private sealed class ImmediateWindowProbe : IMinecraftWindowProbe
    {
        public ValueTask<MinecraftWindowProbeResult> ProbeAsync(int processId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MinecraftWindowProbeResult.Visible);
    }

    private sealed class ExitingProcessPort : IMinecraftProcessPort
    {
        public bool UsesPrivateArgumentTransport => true;

        public ValueTask<System.Diagnostics.Process> StartAsync(
            System.Diagnostics.ProcessStartInfo startInfo,
            CancellationToken cancellationToken = default)
        {
            System.Diagnostics.ProcessStartInfo exit = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo("cmd", "/c exit 0")
                : new System.Diagnostics.ProcessStartInfo("/bin/sh", "-c exit 0");
            exit.UseShellExecute = false;
            exit.CreateNoWindow = true;
            return ValueTask.FromResult(System.Diagnostics.Process.Start(exit)!);
        }
    }
}
