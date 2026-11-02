using System.Diagnostics;
using Nexa.Platform;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.ModLoaders;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask GameModeOwnsRegistrationThroughProcessCompletion()
    {
        string[] args = PlatformGameMode.Arguments("RegisterGameByPID", 123, 456);
        AssertTrue(args.TakeLast(3).SequenceEqual(["ii", "123", "456"]));
        AssertTrue(args.Contains("--timeout=4s"));
        AssertEqual("false", SettingsPolicySchema.ByKey["game.gamemode"].DefaultValue);
        AssertTrue(SettingsPolicySchema.ByKey["game.gamemode"].InstanceOverride);
        AssertFalse(SettingsPolicySchema.ByKey["game.gamemode"].Exportable);
        string executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Nexa.Services.Tests.exe" : "Nexa.Services.Tests");
        MinecraftLaunchPlan plan = new("java", Path.GetTempPath(), ["-cp", "fixture", "fixture.Main", "", "private-token"],
            ["fixture"], [], new MinecraftModLoaderDescriptor(MinecraftModLoaderKind.Vanilla, null, "fixture.Main", []))
        { MainClassIndex = 2, GameMode = true };
        var native = new GameModeFixture();
        await using (var service = new MinecraftProcessService(jvmHostExecutable: executable) { GameMode = native })
        {
            var session = await service.StartAsync(plan, "game-mode");
            AssertEqual(1, native.Starts);
            AssertEqual(0, await session.WaitForExitAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)));
            await session.DisposeAsync();
            AssertEqual(1, native.Releases);
            bool rejected = false;
            try { await service.StartAsync(plan with { WrapperCommand = "wrapper" }, "game-mode-wrapper"); }
            catch (InvalidOperationException) { rejected = true; }
            AssertTrue(rejected); AssertEqual(1, native.Starts);
            native.Reject = true; rejected = false;
            try { await service.StartAsync(plan, "game-mode-refused"); }
            catch (InvalidOperationException) { rejected = true; }
            AssertTrue(rejected); AssertTrue(native.Child!.HasExited);
            await service.StartAsync(plan with { GameMode = false }, "game-mode-default");
            AssertEqual(2, native.Starts);
        }
        AssertEqual(1, native.Releases);
        native = new GameModeFixture { ExitEarly = true };
        await using (var service = new MinecraftProcessService(jvmHostExecutable: executable) { GameMode = native })
        {
            try { await service.StartAsync(plan, "exit-before-registration-returns"); }
            catch (Exception error) when (error is IOException or InvalidOperationException) { }
        }
        AssertEqual(1, native.Starts); AssertEqual(1, native.Releases);
    }

    private sealed class GameModeFixture : IPlatformGameMode
    {
        internal int Starts, Releases;
        internal bool Reject, ExitEarly;
        internal Process? Child;
        public async ValueTask<IAsyncDisposable> StartAsync(Process process, CancellationToken cancellationToken = default)
        {
            Starts++; Child = process; AssertFalse(process.HasExited);
            if (Reject) throw new InvalidOperationException("fixture refusal");
            if (ExitEarly) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(cancellationToken); }
            return new GameModeFixtureLease(this);
        }
    }

    private sealed class GameModeFixtureLease(GameModeFixture owner) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Increment(ref owner.Releases);
            return ValueTask.CompletedTask;
        }
    }
}
