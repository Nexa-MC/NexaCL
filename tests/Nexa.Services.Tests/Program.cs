namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static readonly (string Name, Func<ValueTask> Body)[] TestCases =
    [
        ..RegisterFoundationAndContent(),
        ..RegisterProxy(),
        ("instance development mode is local and recovery independent", InstanceDevelopmentModeIsLocalAndRecoveryIndependent),
        ..RegisterRuntimeEvidence(),
        ..RegisterPolicyAndSecurity(),
        ..RegisterTransfersAndLaunch(),
        ..RegisterAccountsAndMinecraft(),
    ];

    private static async Task<int> Main(string[] args)
    {
        if (args is [Nexa.Services.Minecraft.Process.MinecraftLaunchHookWorker.WorkerArgument])
            return await Nexa.Services.Minecraft.Process.MinecraftLaunchHookWorker.RunWorkerAsync().ConfigureAwait(false);
        if (args is ["--delta-publisher-smoke", var deltaFixture]) return await RunPublisherDeltaFixture(deltaFixture);
        if (args is ["--update-server-smoke"]) { await AutomaticUpdateLoopbackHttpTransaction(); return 0; }
        if (args is ["--serve-update-fixture"]) return await ServeAutomaticUpdateFixture();
        // Existing parity fixtures model the mainland product; international tests select US explicitly.
        if (Environment.GetEnvironmentVariable("NEXA_COUNTRY") is null) Environment.SetEnvironmentVariable("NEXA_COUNTRY", "CN");
        if (args is ["--owned-installer-child"]) return await RunOwnedInstallerChild();
        if (args is ["--owned-installer-owner"]) return await RunOwnedInstallerOwner();
        if (args is [Nexa.Services.Processes.OwnedInstallerProcess.WorkerArgument]) return await Nexa.Services.Processes.OwnedInstallerProcess.RunWorkerAsync();
        if (args is ["--interrupted-new-install", var installRoot]) return await RunInterruptedNewInstallChild(installRoot);
        if (args is ["--install-publication-child", var root, var stage]) return await RunInstallPublicationChild(root, stage);
        if (args is ["--jvm-host"]) return await ReceiveJvmHostFixture();
        if (args is ["--jvm-child-use-probe", var runtimeRoot, var component]) return RunLauncherJavaUseProbe(runtimeRoot, component);
        if (args.Contains("--live-install-catalog")) { await LiveInstallCatalogSmoke(); return 0; }
        int passed = 0;
        foreach ((string name, Func<ValueTask> body) in TestCases)
        {
            if (args is { Length: > 0 } && !args.Any(arg => name.Contains(arg, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            await body().ConfigureAwait(false);
            Console.WriteLine($"PASS: {name}");
            passed++;
        }

        Console.WriteLine($"Services tests passed: {passed}.");
        return 0;
    }

    private static Func<ValueTask> Sync(Action action) => () =>
    {
        action();
        return ValueTask.CompletedTask;
    };

    private static Func<ValueTask> Async(Func<Task> action) => async () =>
    {
        await action().ConfigureAwait(false);
    };

    internal static void AssertTrue(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }

        AssertTrue(value);
    }

    internal static void AssertTrue(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true but received false.");
        }
    }

    internal static void AssertFalse(bool value) => AssertTrue(!value);

    internal static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}' but received '{actual}'.");
        }
    }
}
