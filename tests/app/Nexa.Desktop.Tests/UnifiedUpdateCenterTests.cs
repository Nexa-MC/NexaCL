using Nexa.Desktop.Ui;
using Nexa.Services.Settings;
using Nexa.Services.Updates;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void UnifiedUpdatesSerializeFailuresAndRestart()
    { RunUnifiedUpdateQueue(false); RunUnifiedUpdateQueue(true); }

    private static void RunUnifiedUpdateQueue(bool changeChannelDuringInstall)
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        fixture.Shell.Renderer.ReducedMotion = true;
        List<string> installs = []; int restarts = 0, errors = 0;
        var sidecars = new QueuedSidecarFixture(installs); var launcher = new QueuedLauncherFixture(installs);
        var queries = new XsrQueryRouterBuilder();
        queries.Register<NexaUpdateQuery, NexaUpdateStatus>(NexaUpdateContract.Check, (_, _) =>
            new(XsrResult.Success(new NexaUpdateStatus(new("2.0.0.alpha.30", "https://example.com/setup", "https://example.com/zip", "https://example.com/notes")))));
        settings.ConfigureUpdates(queries.Build(new NoopDispatchObserver()), new("2.0.0.alpha.29", "linux-x64", "alpha"), _ => { }, launcher, () => restarts++, _ => errors++);
        Dictionary<string, SidecarUpdateRegistration> registrations = new() { ["A"] = new("Friendly Market", "1.0.0"), ["C"] = new("Registered without catalog", "1.0.0") };
        settings.ConfigureSidecarUpdates(sidecars, () => restarts++, () => registrations);
        fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1040, 1200));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.advanced").Entity);
        Pump(() => Enabled("SettingsInstallAllUpdates"));
        AssertEqual(1, sidecars.Checks);
        scene = fixture.Shell.Render(new(1040, 1200));
        AssertTrue(scene.Nodes.Any(node => node.Text == "Friendly Market"));
        AssertTrue(scene.Nodes.Any(node => node.Text == "Registered without catalog"));
        var name = scene.Nodes.First(node => node.Text == "Friendly Market");
        var state = scene.Nodes.First(node => fixture.Shell.Tree.Name(node.Entity) == "SidecarUpdateStatus");
        AssertTrue(state.Rect.X > name.Rect.X);
        Click("ui.settings.update.all", "SettingsInstallAllUpdates");
        AssertEqual("A", string.Join(',', installs));
        Click("ui.settings.update.all", "SettingsInstallAllUpdates");
        Click("ui.settings.update.restartAll", "SettingsRestartAllUpdates");
        AssertEqual(0, restarts); AssertEqual(1, installs.Count);
        if (changeChannelDuringInstall)
        {
            AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("updates.channel", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "beta"))).IsSuccess);
            fixture.Shell.Render(new(1040, 1200)); fixture.Shell.Render(new(1040, 1200));
            sidecars.Complete("A", fail: false);
            Pump(() => Enabled("SettingsRestartAllUpdates"));
            AssertEqual(1, installs.Count); AssertFalse(Enabled("SettingsInstallAllUpdates"));
            return;
        }
        sidecars.Complete("A", fail: true);
        Pump(() => installs.Count == 2); AssertEqual("A,B", string.Join(',', installs));
        sidecars.Complete("B", fail: false);
        Pump(() => installs.Count == 3); AssertEqual("A,B,launcher", string.Join(',', installs));
        launcher.Complete();
        Pump(() => Enabled("SettingsRestartAllUpdates"));
        AssertEqual(1, errors);
        Click("ui.settings.update.restartAll", "SettingsRestartAllUpdates"); AssertEqual(1, restarts);
        // Failed A is still retryable, but changing channel retires its old offer.
        AssertTrue(Enabled("SettingsInstallAllUpdates"));
        AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("updates.channel", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "beta"))).IsSuccess);
        Pump(() => !Enabled("SettingsInstallAllUpdates"));
        Click("ui.settings.update.all", "SettingsInstallAllUpdates"); AssertEqual(3, installs.Count);
        registrations.Clear(); fixture.Shell.Render(new(1040, 1200));
        scene = fixture.Shell.Render(new(1040, 1200));
        AssertFalse(scene.Nodes.Any(node => node.Text == "Friendly Market"));
        AssertFalse(scene.Nodes.Any(node => node.Text == "Registered without catalog"));

        XsrUiEntityId Find(string key)
        {
            XsrUiEntityId found = default;
            fixture.Shell.Tree.Walk(settings.Page, entity => { if (fixture.Shell.Tree.Name(entity) == key) found = entity; return true; });
            return found;
        }
        bool Enabled(string key) => fixture.Shell.Tree.GetComponent<XsrUiInput>(Find(key))?.Enabled == true;
        void Click(string command, string key) { Emit(fixture.Intents, command, Find(key)); fixture.Shell.Render(new(1040, 1200)); }
        void Pump(Func<bool> done)
        {
            var end = DateTime.UtcNow.AddSeconds(5);
            do { fixture.Shell.Render(new(1040, 1200)); if (done()) return; Thread.Sleep(5); } while (DateTime.UtcNow < end);
            throw new InvalidOperationException("Update queue did not reach the expected state.");
        }
    }

    private sealed class QueuedSidecarFixture(List<string> installs) : ISidecarUpdateControl
    {
        private TaskCompletionSource? _pending;
        public SidecarUpdateSnapshot Snapshot { get; private set; } = new("initial", [new("A", "1", null, "installed", false), new("B", "1", null, "installed", false)]);
        internal int Checks;
        public Task CheckAsync(string channel, CancellationToken token)
        { Checks++; Snapshot = new("checked", Snapshot.Items.Select(item => item with { AvailableVersion = "2", Phase = "available", CanInstall = true }).ToArray()); return Task.CompletedTask; }
        public Task InstallAsync(string id, CancellationToken token)
        { installs.Add(id); Set(id, "downloading", false); _pending = new(TaskCreationOptions.RunContinuationsAsynchronously); return _pending.Task; }
        internal void Complete(string id, bool fail)
        { Set(id, fail ? "failed" : "restart", fail); if (fail) _pending!.SetException(new IOException("fixture")); else _pending!.SetResult(); }
        private void Set(string id, string phase, bool canInstall) => Snapshot = Snapshot with { Items = Snapshot.Items.Select(item => item.Id == id ? item with { Phase = phase, CanInstall = canInstall } : item).ToArray() };
    }

    private sealed class QueuedLauncherFixture(List<string> installs) : IAutomaticUpdateControl
    {
        private readonly TaskCompletionSource<AutomaticUpdateStatus> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private AutomaticUpdateStatus _status = new(null, null, "idle", true, false);
        public Task<AutomaticUpdateStatus> ReadAsync(CancellationToken token) => Task.FromResult(_status);
        public Task<AutomaticUpdateStatus> InstallAsync(string version, string channel, CancellationToken token)
        { installs.Add("launcher"); _status = new(version, channel, "downloading", true, false); return _pending.Task; }
        internal void Complete() { _status = _status with { Phase = "complete" }; _pending.SetResult(_status); }
        public Task<AutomaticUpdateStatus> RollbackAsync(CancellationToken token) => throw new NotSupportedException();
        public void Restart() => throw new NotSupportedException();
    }
}
