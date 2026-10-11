using Nexa.Desktop.Ui;
using Nexa.Services.Composition;
using Nexa.Services.Minecraft;
using Nexa.Services.Settings;
using Nexa.Services.Setup;
using Nexa.UI.Next;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void StorageLocationsPresentationFollowsActiveRootWithoutGuessingJava()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        string data = Path.Combine(fixture.TemporaryDirectory, "data-locations");
        using var preferences = new StoragePreferencesService(new(data), Path.Combine(fixture.TemporaryDirectory, "storage-locations.json"));
        var runtime = FoundationRuntimeComposer.ComposeWithStorage(fixture.Foundation.Host, preferences);
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, runtime.Queries, runtime.Commands, fixture.Store, fixture.Feedback);
        var state = fixture.Store.Resolve(MinecraftLibraryContract.StateKey);
        var before = fixture.Store.Read<MinecraftLibrarySnapshot>(state).Value!;
        string first = Path.Combine(fixture.TemporaryDirectory, "first-game"), second = Path.Combine(fixture.TemporaryDirectory, "second-game");
        Publish(first, before.Revision + 1);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1200, 1600));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.storage").Entity);
        Wait(() => Value("ActiveMinecraft") == first && Value("SelectedInstance") == Path.Combine(first, "versions", "same-id"));
        AssertEqual(Path.Combine(data, "settings"), Value("Settings"));
        AssertEqual(Path.Combine(data, "logs"), Value("Logs"));
        AssertEqual("未配置", Value("Cache"));
        string java = fixture.Foundation.Host.JavaManagedRuntimeRoots.Single();
        AssertEqual(java, Value("JavaRuntimes"));
        AssertTrue(!java.StartsWith(first, StringComparison.Ordinal));
        var instanceValue = TextEntity("SelectedInstance");
        AssertEqual(XsrUiTextAlignment.End, fixture.Shell.Tree.GetComponent<XsrUiVisualStyle>(instanceValue)!.TextAlignment);
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiVisualStyle>(instanceValue)!.WrapText);
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiText>(instanceValue)!.Localize);
        scene = fixture.Shell.Render(new(1200, 1600));
        AssertTrue(scene.Nodes.Any(node => node.Entity == instanceValue));
        var row = Find("SettingsStoragePath.SelectedInstance");
        AssertEqual(9d, fixture.Shell.Tree.GetComponent<XsrUiElement>(row)!.Padding.Top);
        AssertEqual(12d, fixture.Shell.Tree.GetComponent<XsrUiElement>(Find("SettingsStorageDirectories.Body"))!.Padding.Left);
        Publish(second, before.Revision + 2);
        Wait(() => Value("ActiveMinecraft") == second && Value("SelectedInstance") == Path.Combine(second, "versions", "same-id"));
        AssertEqual(java, Value("JavaRuntimes"));
        AssertEqual(Path.Combine(second, "versions"), Value("Instances"));
        AssertFalse(Directory.Exists(first)); AssertFalse(Directory.Exists(second)); AssertFalse(Directory.Exists(data));

        void Publish(string root, long revision)
        {
            string directory = Path.Combine(root, "versions", "same-id");
            var descriptor = Instance("same-id") with { DirectoryPath = directory };
            fixture.Store.Publish(state, before with
            {
                Revision = revision,
                RootDirectory = root,
                Directories = [new(first), new(second)],
                Instances = [descriptor],
                SelectedInstanceId = "same-id",
                IsLoading = false
            });
        }
        void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        {
            fixture.Shell.Render(new(1200, 1600)); return condition();
        }, TimeSpan.FromSeconds(5)));
        string? Value(string key)
        {
            var text = TextEntity(key, false);
            return text.IsAssigned ? fixture.Shell.Tree.GetComponent<XsrUiText>(text)?.Content : null;
        }
        XsrUiEntityId TextEntity(string key, bool required = true)
        {
            var values = Find("SettingsStorageValue." + key, required);
            if (!values.IsAssigned) return default;
            var children = fixture.Shell.Tree.Children(values);
            return children.Count == 0 ? default : children[0];
        }
        XsrUiEntityId Find(string name, bool required = true)
        {
            XsrUiEntityId entity = default;
            fixture.Shell.Tree.Walk(settings.Page, current => { if (fixture.Shell.Tree.Name(current) == name) entity = current; return true; });
            if (required) AssertTrue(entity.IsAssigned); return entity;
        }
    }

    private static void StorageLocationsPresentationWithoutLibraryOrRouteStaysSafe()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var builder = new XsrStateStoreBuilder();
        SettingsPolicyContract.DeclareState(builder); SettingsPresentationState.DeclareState(builder);
        var state = builder.Build();
        AssertFalse(state.TryResolve(MinecraftLibraryContract.StateKey, out _));
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, state, fixture.Feedback);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1200, 1600));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.storage").Entity);
        for (int frame = 0; frame < 5; frame++) scene = fixture.Shell.Render(new(1200, 1600));
        AssertTrue(scene.Nodes.Any(node => node.Text == "目录信息尚未接入。"));
    }

    private static void StorageLocationsPresentationRetriesScopeAndDropsRetiredFacts()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        var state = fixture.Store.Resolve(MinecraftLibraryContract.StateKey);
        var initial = fixture.Store.Read<MinecraftLibrarySnapshot>(state).Value!;
        var requests = new List<(TaskCompletionSource<XsrResult<StorageLocationsSnapshot>> Result, CancellationToken Stop)>();
        var builder = new XsrQueryRouterBuilder();
        builder.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
            (query, _) => ValueTask.FromResult(XsrResult.Success(SettingsCatalog.Read(query))));
        builder.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
            (query, _) => ValueTask.FromResult(fixture.Foundation.Host.SettingsPolicy.Read(query)));
        builder.Register<StorageLocationsQuery, StorageLocationsSnapshot>(StorageLocationsContract.Query, (_, token) =>
        {
            var done = new TaskCompletionSource<XsrResult<StorageLocationsSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Add((done, token)); return new(done.Task);
        });
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, builder.Build(new NoopDispatchObserver()),
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        var scene = fixture.Shell.Render(new(1200, 1600));
        Emit(fixture.Intents, "ui.settings.section", FindByKey(fixture.Shell, scene, "SettingsNav.storage").Entity);
        Wait(() => requests.Count == 1);
        requests[0].Result.SetResult(XsrResult.Failure<StorageLocationsSnapshot>(new(XsrErrorKind.Rejected,
            XsrSemanticId.Parse("fixture.storage.error"), "fixture")));
        Wait(() => fixture.Shell.Render(new(1200, 1600)).Nodes.Any(node => node.Text == "目录信息暂不可用，请重新打开此页。"));
        string recovered = Path.Combine(fixture.TemporaryDirectory, "recovered-root");
        Publish(recovered, initial.Revision + 1); Wait(() => requests.Count == 2);
        requests[1].Result.SetResult(XsrResult.Success(Snapshot(recovered, initial.Revision + 1)));
        Wait(() => HasCurrentRoot(recovered));
        string retired = Path.Combine(fixture.TemporaryDirectory, "retired-root");
        Publish(retired, initial.Revision + 2); Wait(() => requests.Count == 3);
        AssertFalse(HasCurrentRoot(recovered));
        string current = Path.Combine(fixture.TemporaryDirectory, "current-root");
        Publish(current, initial.Revision + 3); Wait(() => requests.Count == 4);
        AssertTrue(requests[2].Stop.IsCancellationRequested);
        requests[2].Result.SetResult(XsrResult.Success(Snapshot(retired, initial.Revision + 2)));
        fixture.Shell.Render(new(1200, 1600)); AssertFalse(HasCurrentRoot(retired));
        requests[3].Result.SetResult(XsrResult.Success(Snapshot(current, initial.Revision + 3)));
        Wait(() => HasCurrentRoot(current));
        AssertFalse(HasCurrentRoot(recovered)); AssertFalse(HasCurrentRoot(retired));

        void Publish(string root, long revision) => fixture.Store.Publish(state, initial with
        { Revision = revision, RootDirectory = root, Directories = [new(root)], Instances = [], SelectedInstanceId = "", IsLoading = false });
        StorageLocationsSnapshot Snapshot(string root, long revision) => new(revision, fixture.TemporaryDirectory,
            Path.Combine(fixture.TemporaryDirectory, "settings"), Path.Combine(fixture.TemporaryDirectory, "logs"),
            null, [], [root], root, Path.Combine(root, "versions"), null);
        bool HasCurrentRoot(string root)
        {
            bool present = false;
            fixture.Shell.Tree.Walk(settings.Page, entity =>
            {
                if (fixture.Shell.Tree.Name(entity) == "SettingsStorageValue.ActiveMinecraft")
                {
                    var children = fixture.Shell.Tree.Children(entity);
                    present = children.Count != 0 && fixture.Shell.Tree.GetComponent<XsrUiText>(children[0])?.Content == root;
                }
                return true;
            });
            return present;
        }
        void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        { fixture.Shell.Render(new(1200, 1600)); return condition(); }, TimeSpan.FromSeconds(5)));
    }
}
