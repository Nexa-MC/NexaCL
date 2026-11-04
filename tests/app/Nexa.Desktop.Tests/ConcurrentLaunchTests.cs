using Nexa.Desktop.Ui;
using Nexa.Services.Accounts;
using Nexa.Services.Minecraft.Launch;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ConcurrentLaunchUiScopesCancellation()
    {
        RecordingStartRoute recording = new() { Hang = true };
        using var runtime = CreateRecordingRuntime(recording, includeAttemptCancel: true);
        using LaunchPageFixture fixture = new(new ImmediateInstanceSource([Instance("a"), Instance("b")]), runtime, addProfile: true, ownsMinecraftRuntime: false);
        AssertTrue(AddFixtureProfile(fixture.Service, new LaunchProfile { Username = "Selected" }).IsSuccess);
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        fixture.Shell.Renderer.ReducedMotion = true;
        SelectFirstAccountAndLaunch(fixture, recording);
        string first = recording.LastCommand!.InstanceId;
        string other = first == "a" ? "b" : "a";
        var size = new XsrUiSize(1000, 720);
        fixture.Shell.Render(size);
        Emit(fixture.Intents, "ui.launch.background");
        var scene = fixture.Shell.Render(size);
        AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "InstanceListButton").Entity));
        scene = fixture.Shell.Render(size);
        AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "LibraryRow:version:" + other).Entity));
        fixture.Controller.Versions.WaitUntilIdle().GetAwaiter().GetResult();
        scene = fixture.Shell.Render(size);
        AssertTrue(FindByKey(fixture.Shell, scene, "LaunchButton").IsClickable);
        AssertTrue(fixture.Shell.Renderer.Activate(FindByKey(fixture.Shell, scene, "LaunchButton").Entity));
        AssertTrue(SpinWait.SpinUntil(() => recording.Commands.Count == 2, TimeSpan.FromSeconds(3)));
        string root = recording.LastCommand!.MinecraftRootDirectory!;
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        MinecraftLaunchAttemptSnapshot[] attempts = [new(a, first, root, true, "login", 0.25, "offline", "", false, null, false, "", 0, []),
            new(b, other, root, true, "get_java", 0.5, "offline", "", false, null, false, "", 0, [])];
        fixture.Store.Publish(fixture.Store.Resolve(MinecraftLaunchProgressState.SnapshotKey), attempts[0].ToSnapshot() with { Attempts = attempts });
        scene = fixture.Shell.Render(size);
        AssertEqual("50%", FindByKey(fixture.Shell, scene, "LaunchingPercentValue").Text);
        Emit(fixture.Intents, "ui.launch.cancel");
        AssertTrue(SpinWait.SpinUntil(() => recording.LastCancelledAttempt == b, TimeSpan.FromSeconds(3)));
        AssertFalse(recording.LastCancelledAttempt == a);
    }
}
