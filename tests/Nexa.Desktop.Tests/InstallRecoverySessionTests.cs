using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void StartupInstallRecoveryOwnsDispatchAndShutdown()
    {
        using var entered = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim();
        string? observed = null;
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        XsrCommandRouterBuilder builder = new();
        builder.Register<MinecraftInstallRecoveryCommand>(MinecraftInstallRoutes.Recover, async (command, token) =>
        {
            observed = command.RootDirectories[0]; entered.Set();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { stopped.Set(); await release.Task.ConfigureAwait(false); }
            return XsrResult.Success();
        });
        List<string> reports = [];
        string[] roots = ["original-root"];
        using var session = new DesktopInstallRecoverySession(builder.Build(new NoopDispatchObserver()), roots, reports.Add);
        roots[0] = "changed-root";
        AssertTrue(entered.Wait(TimeSpan.FromSeconds(5)));
        session.Dispose(); session.Dispose();
        Task shutdown = session.DisposeAsync().AsTask();
        AssertFalse(shutdown.IsCompleted);
        release.SetResult();
        shutdown.GetAwaiter().GetResult();
        AssertTrue(stopped.IsSet); AssertEqual("original-root", observed); AssertEqual(0, reports.Count);
    }

    private static void StartupRecoveryConflictsKeepShellAvailableWithoutResumingWrites()
    {
        int installCalls = 0, snapshotCalls = 0;
        XsrCommandRouterBuilder install = new();
        install.Register<MinecraftInstallRecoveryCommand>(MinecraftInstallRoutes.Recover, (_, _) =>
        {
            installCalls++;
            return ValueTask.FromResult(XsrResult.Success());
        });
        XsrCommandRouterBuilder recovery = new();
        recovery.Register<InstanceRecoveryResumeCommand>(InstanceRecoveryContract.Recover, (_, _) =>
        {
            snapshotCalls++;
            return ValueTask.FromResult(XsrResult.Failure(MinecraftErrors.InvalidRequest("保留冲突备份")));
        });
        List<string> reports = [];
        using var session = new DesktopInstallRecoverySession(install.Build(new NoopDispatchObserver()), ["root"],
            reports.Add, recoveryCommands: recovery.Build(new NoopDispatchObserver()));
        new DesktopStartupReadiness(_ => { }, default).RunAsync("startup_local_recovery", token => session.InitialReady.WaitAsync(token))
            .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        AssertEqual(1, snapshotCalls);
        AssertEqual(0, installCalls);
        AssertEqual(1, reports.Count);
        AssertTrue(reports[0].Contains("已暂停自动安装恢复", StringComparison.Ordinal));
    }
}
