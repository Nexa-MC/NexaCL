using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft;
using Nexa.Services.Setup;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void LauncherMigrationPagePreviewsSelectsConfirmsAndRegisters()
    {
        using var ui = new LauncherMigrationUiFixture();
        ui.ChooseSourceAndDestination(); ui.Click("preview");
        ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        AssertEqual(1, ui.PreviewCalls);
        AssertFalse(Directory.Exists(ui.LastPreview!.Query.DestinationDirectory));
        var panel = ui.Find("LauncherMigrationWorkspace"); var first = ui.Find("LauncherMigration.Select.one");
        var second = ui.Find("LauncherMigration.Select.two");
        AssertEqual("Literal <Text /> {data private}", ui.Text(first));
        AssertFalse(ui.Fixture.Shell.Tree.GetComponent<XsrUiInput>(ui.Find("LauncherMigration.Select.unsupported"))!.Enabled);
        ui.Fixture.Shell.Renderer.Focus(second, showIndicator: false);
        var transition = new XsrUiTransition(); ui.Fixture.Shell.Tree.SetComponent(second, transition);
        ui.ClickKey("LauncherMigration.Select.one"); ui.Pump();
        AssertFalse(ui.Fixture.Shell.Tree.GetComponent<XsrUiToggle>(first)!.IsChecked);
        AssertEqual(panel, ui.Find("LauncherMigrationWorkspace")); AssertEqual(second, ui.Find("LauncherMigration.Select.two"));
        AssertEqual(second, ui.Fixture.Shell.Renderer.Focused);
        AssertTrue(ReferenceEquals(transition, ui.Fixture.Shell.Tree.GetComponent<XsrUiTransition>(second)));
        ui.Click("apply"); ui.Wait(() => ui.Fixture.Feedback.Snapshot().Dialog is not null);
        AssertEqual(0, ui.ApplyCalls);
        var confirmation = ui.Fixture.Feedback.Snapshot().Dialog!;
        AssertFalse(confirmation.LocalizeMessage); AssertTrue(confirmation.Message.Contains("Converted", StringComparison.Ordinal));
        AssertFalse(confirmation.Message.Contains("Literal <Text />", StringComparison.Ordinal));
        ui.Fixture.Feedback.ResolveDialog(confirmation.Id, false); ui.Wait(() => ui.Enabled("apply"));
        AssertEqual(0, ui.ApplyCalls);
        ui.Click("apply"); ui.Confirm();
        ui.Wait(() => ui.ApplyCalls == 1 && ui.Enabled("register"));
        AssertEqual("two", ui.Applied!.SelectedInstanceIds.Single());
        var receipt = ui.Receipt!;
        var library = ui.LibrarySnapshot;
        AssertTrue(library.Directories.Any(directory => directory.Path == receipt.Instances[0].MinecraftRootDirectory));
        AssertEqual(receipt.Instances[0].MinecraftRootDirectory, library.RootDirectory);
        AssertEqual("native", library.SelectedInstanceId);
        AssertTrue(Directory.Exists(ui.Source));
    }

    private static void LauncherMigrationPageRestoresReceiptAndRetriesOnlyRegistration()
    {
        using var ui = new LauncherMigrationUiFixture(failFirstRegistration: true);
        ui.ChooseSourceAndDestination(); ui.Click("preview");
        ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        ui.Click("apply"); ui.Confirm();
        ui.Wait(() => ui.ApplyCalls == 1 && ui.Enabled("register"));
        AssertFalse(ui.LibrarySnapshot.Directories.Any(directory => ui.Receipt!.Instances.Any(instance => instance.MinecraftRootDirectory == directory.Path)));
        var receipt = ui.Receipt!;
        AssertTrue(ui.AllText().Contains("不会重新复制文件", StringComparison.Ordinal));
        ui.Click("register"); ui.Wait(() => ui.LibrarySnapshot.Directories.Any(directory => directory.Path == receipt.Instances[0].MinecraftRootDirectory) && ui.Enabled("register"));
        AssertEqual(1, ui.ApplyCalls);
        ui.Picks.Enqueue(receipt.DestinationDirectory); ui.Click("recover");
        ui.Wait(() => ui.ReceiptReads >= 2 && ui.Enabled("register"));
        ui.Click("register"); ui.Wait(() => ui.Enabled("register"));
        AssertEqual(1, ui.ApplyCalls);
        AssertEqual(1, ui.LibrarySnapshot.Directories.Count(directory => directory.Path == receipt.Instances[0].MinecraftRootDirectory));
        AssertEqual(1, ui.LibrarySnapshot.Directories.Count(directory => directory.Path == receipt.Instances[1].MinecraftRootDirectory));
    }

    private static void LauncherMigrationPageCancelsStalePreviewAndRecoversFromFailure()
    {
        using var ui = new LauncherMigrationUiFixture();
        var pending = new TaskCompletionSource<XsrResult<LauncherMigrationPreview>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken previewToken = default;
        ui.PreviewHandler = (query, token) => { previewToken = token; return new(pending.Task); };
        ui.ChooseSourceAndDestination(); ui.Click("preview"); ui.Wait(() => ui.PreviewCalls == 1);
        ui.Navigate("general"); ui.Wait(() => ui.Settings.SelectedSection == "general");
        AssertTrue(previewToken.IsCancellationRequested);
        pending.SetResult(XsrResult.Success(ui.LastPreview!));
        ui.Navigate("storage"); ui.Wait(() => ui.Find("LauncherMigration.preview", false).IsAssigned);
        AssertFalse(ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        bool fail = true;
        ui.PreviewHandler = (query, _) => new(fail ? XsrResult.Failure<LauncherMigrationPreview>(XsrRuntimeErrors.TargetNotFound("Preview fixture failure")) : XsrResult.Success(ui.MakePreview(query)));
        ui.Click("preview"); ui.Wait(() => ui.PreviewCalls == 2 && ui.Enabled("preview"));
        AssertFalse(ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        fail = false; ui.Click("preview"); ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        AssertEqual(3, ui.PreviewCalls);
    }

    private static void LauncherMigrationPageUndoRefusalPreservesRegisteredCopies()
    {
        using var ui = new LauncherMigrationUiFixture();
        ui.ChooseSourceAndDestination(); ui.Click("preview");
        ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        ui.Click("apply"); ui.Confirm(); ui.Wait(() => ui.Receipt is not null && ui.Enabled("undo"));
        var roots = ui.Receipt!.Instances.Select(instance => instance.MinecraftRootDirectory).ToArray();
        ui.RefuseUndo = true; ui.Click("undo"); ui.Confirm();
        ui.Wait(() => ui.UndoCalls == 1 && ui.Enabled("undo"));
        AssertTrue(roots.All(root => ui.LibrarySnapshot.Directories.Any(directory => directory.Path == root)));
        AssertTrue(roots.All(Directory.Exists));
        ui.RefuseUndo = false; ui.Click("undo"); ui.Confirm();
        ui.Wait(() => ui.UndoCalls == 2 && !ui.Visible("undo"));
        AssertFalse(roots.Any(root => ui.LibrarySnapshot.Directories.Any(directory => directory.Path == root)));
        AssertTrue(Directory.Exists(ui.Source));
    }

    private static void LauncherMigrationPageRefusesUndoOfLastRegisteredDirectories()
    {
        using var ui = new LauncherMigrationUiFixture();
        ui.ChooseSourceAndDestination(); ui.Click("preview");
        ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        ui.Click("apply"); ui.Confirm(); ui.Wait(() => ui.Receipt is not null && ui.Enabled("undo"));
        var roots = ui.Receipt!.Instances.Select(instance => instance.MinecraftRootDirectory).ToArray();
        foreach (var directory in ui.LibrarySnapshot.Directories.Where(directory => !roots.Contains(directory.Path, StringComparer.Ordinal)).ToArray())
        {
            AssertTrue(ui.Fixture.Library.Commands.TryResolve(MinecraftLibraryRoutes.Forget, out var route));
            AssertTrue(ui.Fixture.Library.Commands.Dispatch(route, new MinecraftLibraryForgetCommand(directory.Path)).Completion.GetAwaiter().GetResult().IsSuccess);
        }
        ui.Pump(); ui.Click("undo"); ui.Pump();
        AssertEqual(0, ui.UndoCalls); AssertTrue(ui.Fixture.Feedback.Snapshot().Dialog is null);
        AssertTrue(ui.AllText().Contains("先在版本列表登记另一个游戏目录", StringComparison.Ordinal));
        AssertTrue(roots.All(Directory.Exists));
    }

    private static void LauncherMigrationPageCompletesCommittedUndoAfterNavigationCancellation()
    {
        using var ui = new LauncherMigrationUiFixture();
        ui.ChooseSourceAndDestination(); ui.Click("preview");
        ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        ui.Click("apply"); ui.Confirm(); ui.Wait(() => ui.Receipt is not null && ui.Enabled("undo"));
        var roots = ui.Receipt!.Instances.Select(instance => instance.MinecraftRootDirectory).ToArray();
        var committed = new TaskCompletionSource<XsrResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken undoToken = default;
        ui.UndoHandler = (_, token) => { undoToken = token; return new(committed.Task); };
        ui.Click("undo"); ui.Confirm(); ui.Wait(() => ui.UndoCalls == 1);
        ui.Navigate("general"); AssertTrue(undoToken.IsCancellationRequested);
        // Simulate the backend reporting a committed deletion after the page has retired.
        committed.SetResult(XsrResult.Success());
        ui.Wait(() => !roots.Any(root => ui.LibrarySnapshot.Directories.Any(directory => directory.Path == root)));
        ui.Navigate("storage"); ui.Wait(() => ui.Find("LauncherMigration.recover", false).IsAssigned);
        AssertFalse(ui.Visible("undo")); AssertEqual(1, ui.UndoCalls);
    }

    private static void LauncherMigrationPageRetriesUndoRegistrationWithoutDeletingAgain()
    {
        using var ui = new LauncherMigrationUiFixture();
        ui.ChooseSourceAndDestination(); ui.Click("preview");
        ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        ui.Click("apply"); ui.Confirm(); ui.Wait(() => ui.Receipt is not null && ui.Enabled("undo"));
        var roots = ui.Receipt!.Instances.Select(instance => instance.MinecraftRootDirectory).ToArray();
        ui.FailNextForget = true; ui.Click("undo"); ui.Confirm();
        ui.Wait(() => ui.UndoCalls == 1 && ui.Visible("cleanup") && ui.Enabled("cleanup"));
        AssertTrue(roots.Any(root => ui.LibrarySnapshot.Directories.Any(directory => directory.Path == root)));
        ui.Navigate("general"); ui.Navigate("storage"); ui.Wait(() => ui.Visible("cleanup") && ui.Enabled("cleanup"));
        ui.Click("cleanup"); ui.Wait(() => !ui.Visible("cleanup"));
        AssertFalse(roots.Any(root => ui.LibrarySnapshot.Directories.Any(directory => directory.Path == root)));
        AssertEqual(1, ui.UndoCalls);
    }

    private static void LauncherMigrationPageRegistersLongDisplayNames()
    {
        using var ui = new LauncherMigrationUiFixture();
        string name = new string('名', 79) + "👨‍👩‍👧‍👦" + new string('称', 20);
        ui.FirstDisplayName = name;
        ui.ChooseSourceAndDestination(); ui.Click("preview");
        ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        AssertEqual(name, ui.Text(ui.Find("LauncherMigration.Select.one")));
        ui.Click("apply"); ui.Confirm(); ui.Wait(() => ui.Receipt is not null && ui.Enabled("register"));
        var imported = ui.Receipt!.Instances.First(instance => instance.Id == "one");
        AssertEqual(name, imported.Name);
        var registered = ui.LibrarySnapshot.Directories.Single(directory => directory.Path == imported.MinecraftRootDirectory);
        AssertEqual(new string('名', 79), registered.Name);
        AssertEqual(imported.MinecraftRootDirectory, ui.LibrarySnapshot.RootDirectory);
        AssertEqual("native", ui.LibrarySnapshot.SelectedInstanceId);
    }

    private sealed class LauncherMigrationUiFixture : IDisposable
    {
        internal readonly LaunchPageFixture Fixture = new(new ImmediateInstanceSource([Instance("native")]));
        internal readonly Queue<string?> Picks = new();
        internal readonly SettingsPageController Settings;
        internal readonly string Source;
        internal LauncherMigrationPreview? LastPreview;
        internal LauncherMigrationApplyCommand? Applied;
        internal LauncherMigrationReceipt? Receipt;
        internal LauncherMigrationDiscoveryQuery? LastDiscovery;
        internal int PreviewCalls, ApplyCalls, ReceiptReads, UndoCalls, DiscoveryCalls;
        internal bool RefuseUndo, FailNextForget;
        internal string? FirstDisplayName;
        internal Func<LauncherMigrationUndoCommand, CancellationToken, ValueTask<XsrResult>>? UndoHandler;
        internal Func<LauncherMigrationQuery, CancellationToken, ValueTask<XsrResult<LauncherMigrationPreview>>>? PreviewHandler;
        internal Func<LauncherMigrationDiscoveryQuery, CancellationToken, ValueTask<XsrResult<LauncherMigrationDiscoveryResult>>>? DiscoveryHandler;

        internal LauncherMigrationUiFixture(bool failFirstRegistration = false)
        {
            Source = Path.Combine(Fixture.TemporaryDirectory, "foreign-source"); Directory.CreateDirectory(Source);
            var queries = WorldFixtureQueries(Fixture);
            queries.Register<LauncherMigrationDiscoveryQuery, LauncherMigrationDiscoveryResult>(LauncherMigrationContract.Discover, (query, token) =>
            {
                DiscoveryCalls++; LastDiscovery = query;
                return DiscoveryHandler?.Invoke(query, token) ?? new(XsrResult.Success(new LauncherMigrationDiscoveryResult([], [])));
            });
            queries.Register<LauncherMigrationQuery, LauncherMigrationPreview>(LauncherMigrationContract.Preview, (query, token) =>
            {
                PreviewCalls++; LastPreview = MakePreview(query);
                return PreviewHandler?.Invoke(query, token) ?? new(XsrResult.Success(LastPreview!));
            });
            queries.Register<LauncherMigrationUndoQuery, LauncherMigrationReceipt>(LauncherMigrationContract.ReadReceipt, (query, _) =>
            {
                ReceiptReads++;
                return new(Receipt is not null && query.DestinationDirectory == Receipt.DestinationDirectory
                    ? XsrResult.Success(Receipt) : XsrResult.Failure<LauncherMigrationReceipt>(XsrRuntimeErrors.TargetNotFound("No committed import")));
            });
            var commands = new XsrCommandRouterBuilder();
            commands.Register<LauncherMigrationApplyCommand>(LauncherMigrationContract.Apply, (command, _) =>
            {
                ApplyCalls++; Applied = command;
                var imported = command.SelectedInstanceIds.Select(id => new LauncherMigrationImportedInstance(id,
                    command.Preview.Instances.Single(instance => instance.Id == id).Name,
                    Path.Combine(command.Preview.Query.DestinationDirectory, id), "native")).ToArray();
                foreach (var instance in imported) Directory.CreateDirectory(instance.MinecraftRootDirectory);
                Receipt = new("fixture-receipt", command.Preview.Query.DestinationDirectory, imported);
                return new(XsrResult.Success());
            });
            commands.Register<LauncherMigrationUndoCommand>(LauncherMigrationContract.Undo, (command, token) =>
            {
                UndoCalls++;
                if (UndoHandler is not null) return UndoHandler(command, token);
                return new(RefuseUndo ? XsrResult.Failure(XsrRuntimeErrors.TargetNotFound("Modified copy must be retained")) : XsrResult.Success());
            });
            var library = new XsrCommandRouterBuilder();
            library.Register<MinecraftLibraryDirectoryCommand>(MinecraftLibraryRoutes.Directory, (command, token) =>
            {
                if (failFirstRegistration)
                { failFirstRegistration = false; return new(XsrResult.Failure(XsrRuntimeErrors.TargetNotFound("Registration fixture failure"))); }
                return new(LibraryCommandAsync(MinecraftLibraryRoutes.Directory, command, token));
            });
            library.Register<MinecraftLibraryRenameCommand>(MinecraftLibraryRoutes.Rename, (command, token) => new(LibraryCommandAsync(MinecraftLibraryRoutes.Rename, command, token)));
            library.Register<MinecraftLibrarySelectCommand>(MinecraftLibraryRoutes.Select, (command, token) => new(LibraryCommandAsync(MinecraftLibraryRoutes.Select, command, token)));
            library.Register<MinecraftLibraryForgetCommand>(MinecraftLibraryRoutes.Forget, (command, token) =>
            {
                if (FailNextForget) { FailNextForget = false; return new(XsrResult.Failure(XsrRuntimeErrors.TargetNotFound("Forget fixture failure"))); }
                return new(LibraryCommandAsync(MinecraftLibraryRoutes.Forget, command, token));
            });
            Settings = new(Fixture.Shell, Fixture.Intents, queries.Build(new NoopDispatchObserver()), commands.Build(new NoopDispatchObserver()), Fixture.Store, Fixture.Feedback);
            Settings.ConfigureLauncherMigration(_ => Task.FromResult(Picks.Dequeue()), library.Build(new NoopDispatchObserver()));
            Fixture.Shell.Renderer.ReducedMotion = true; Fixture.Shell.Stage.Navigation.Replace(Settings.Page);
            Wait(() => Find("SettingsNav.storage", false).IsAssigned);
            Navigate("storage"); Wait(() => Find("LauncherMigration.source", false).IsAssigned);
        }

        internal LauncherMigrationPreview MakePreview(LauncherMigrationQuery query) => new(query, "source-revision",
            [new("one", FirstDisplayName ?? "Literal <Text /> {data private}", Source, "1.20.1", null, null, 1024, 1, true, null, []),
             new("two", "Converted", Source, "1.21.1", "Fabric", "0.16.10", 2048, 2, true, null, ["Literal <warning> {state private}"]),
             new("unsupported", "Unsupported", Source, "1.20.1", "Custom", null, 0, 0, false, "Unknown component", [])], ["Accounts require sign-in"]);
        internal MinecraftLibrarySnapshot LibrarySnapshot => Fixture.Store.Read<MinecraftLibrarySnapshot>(Fixture.Store.Resolve(MinecraftLibraryContract.StateKey)).Value;
        internal void ChooseSourceAndDestination()
        {
            Picks.Enqueue(Source); Click("source"); Wait(() => Text(Find("LauncherMigration.Source")) == Source);
            Picks.Enqueue(Fixture.TemporaryDirectory); Click("destination"); Wait(() => Enabled("preview"));
        }
        internal void Navigate(string section) { Emit(Fixture.Intents, "ui.settings.section", Find("SettingsNav." + section)); Pump(); }
        internal void Click(string action) => ClickKey("LauncherMigration." + action);
        internal void ClickKey(string key) { Emit(Fixture.Intents, "ui.settings.launcher-migration.action", Find(key)); Pump(); }
        internal void Confirm()
        {
            Wait(() => Fixture.Feedback.Snapshot().Dialog is not null);
            Fixture.Feedback.ResolveDialog(Fixture.Feedback.Snapshot().Dialog!.Id, true); Pump();
        }
        internal bool Enabled(string action) => Find("LauncherMigration." + action, false) is { IsAssigned: true } entity && Fixture.Shell.Tree.GetComponent<XsrUiInput>(entity)?.Enabled == true;
        internal bool Visible(string action) => Find("LauncherMigration." + action, false) is { IsAssigned: true } entity && Fixture.Shell.Tree.GetComponent<XsrUiElement>(entity)?.IsVisible != false;
        internal void Pump() => Fixture.Shell.Render(new(1200, 1800));
        internal void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() => { Pump(); return condition(); }, TimeSpan.FromSeconds(5)));
        internal string Text(XsrUiEntityId entity) => Fixture.Shell.Tree.GetComponent<XsrUiText>(entity)!.Content;
        internal string AllText()
        {
            List<string> text = [];
            Fixture.Shell.Tree.Walk(Settings.Page, entity => { if (Fixture.Shell.Tree.GetComponent<XsrUiText>(entity) is { } copy) text.Add(copy.Content); return true; });
            return string.Join('\n', text);
        }
        internal XsrUiEntityId Find(string name, bool required = true)
        {
            XsrUiEntityId found = default;
            Fixture.Shell.Tree.Walk(Settings.Page, entity => { if (Fixture.Shell.Tree.Name(entity) == name) found = entity; return true; });
            if (required) AssertTrue(found.IsAssigned); return found;
        }
        private Task<XsrResult> LibraryCommandAsync<TCommand>(XsrSemanticId semantic, TCommand command, CancellationToken token) where TCommand : notnull
        {
            AssertTrue(Fixture.Library.Commands.TryResolve(semantic, out var route));
            return Fixture.Library.Commands.Dispatch(route, command, cancellationToken: token).Completion;
        }
        public void Dispose() { Settings.Dispose(); Fixture.Dispose(); }
    }
}
