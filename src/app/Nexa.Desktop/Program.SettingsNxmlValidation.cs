using System.Globalization;
using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop;

internal static partial class Program
{
    private static async Task ValidateDynamicSettingsMigrationAsync(XsrUiShell shell, XsrStateStore store, DesktopUiIntentSink intents)
    {
        await ValidateDynamicSettingsGlobalAsync(shell, store, intents).ConfigureAwait(false);
        await ValidateDynamicSettingsContentAsync(shell, store, intents).ConfigureAwait(false);
        await ValidateDynamicSettingsWorldsAsync(shell, store, intents).ConfigureAwait(false);
        Console.WriteLine("Dynamic settings migration validation passed: actual controllers and shipped external assets, controlled typed global/content/world/server/export/recovery facts, retained interaction state, both themes, locale and retired asynchronous scope; no live provider/game or user-file mutations.");
    }

    private static async Task ValidateDynamicSettingsWorldsAsync(XsrUiShell shell, XsrStateStore store, DesktopUiIntentSink intents)
    {
        string directory = Path.Combine(Path.GetTempPath(), "nexa-settings-nxml-validation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previous = shell.Stage.Navigation.Current;
        var previousLocalizer = shell.Renderer.TextLocalizer;
        var previousScheme = shell.Renderer.ColorScheme;
        bool previousMotion = shell.Renderer.ReducedMotion;
        var languageKey = store.Resolve(XsrSemanticId.Parse("UiLanguage"));
        string? previousLanguage = store.Read<string>(languageKey).Value;
        var language = new UiLocalizationCatalog();
        language.SetLanguage("zh-Hans");
        shell.Renderer.TextLocalizer = language.Translate;
        store.Publish(languageKey, "zh-Hans");
        shell.Renderer.ReducedMotion = true;
        var fixture = new SettingsWorldValidationFacts(directory, store.Read<long>(store.Resolve(SettingsPolicyContract.RevisionKey)).Value);
        string instance = directory;
        using var feedback = new DesktopFeedbackService();
        using var controller = new SettingsPageController(shell, intents, fixture.Queries,
            fixture.Commands, store, feedback, () => instance);
        var size = new XsrUiSize(810, 500);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        string step = "instance navigation";
        try
        {
            shell.Stage.Navigation.Replace(controller.Page);
            await Pump(() => Optional("SettingsNav.servers").IsAssigned, "instance navigation").ConfigureAwait(false);
            Progress("server editor and status");
            await Select("servers").ConfigureAwait(false);
            await Pump(() => fixture.ServerListReads == 1 && Optional("ServerEntry.0").IsAssigned, "server list").ConfigureAwait(false);
            var servers = Find("ServersWorkspace");
            var serverRow = Find("ServerEntry.0");
            RequireOwned(servers); RequireOwned(serverRow);
            await Activate(SettingsMigrationWorldKey("ServerEdit")).ConfigureAwait(false);
            await Show("ServerName").ConfigureAwait(false);
            var name = Find("ServerName");
            const string serverDraft = "literal <server draft> {state private}";
            shell.Renderer.SetTextInputValue(name, serverDraft);
            if (!shell.Renderer.Focus(name, showIndicator: false)) throw new InvalidOperationException("Server fixture editor could not focus.");
            shell.Renderer.EditText(XsrUiTextEdit.Left, extendSelection: true);
            var draft = shell.Tree.GetComponent<XsrUiTextInput>(name)!;
            int caret = draft.SelectionEnd, anchor = draft.SelectionStart;
            await Activate(SettingsMigrationWorldKey("ServerCheck")).ConfigureAwait(false);
            await Pump(() => fixture.ServerStatusReads == 1, "scoped server status query").ConfigureAwait(false);
            fixture.CompleteServerStatus();
            await Pump(() => HasLiteralText(serverRow, SettingsWorldValidationFacts.StatusLiteral), "server status result").ConfigureAwait(false);
            await Show("ServerName").ConfigureAwait(false);
            if (Find("ServersWorkspace") != servers || Find("ServerEntry.0") != serverRow || Find("ServerName") != name
                || shell.Renderer.Focused != name || draft.ReadDraft() != serverDraft || draft.SelectionEnd != caret || draft.SelectionStart != anchor)
                throw new InvalidOperationException("Server status reload lost owned rows, editor draft, caret or focus.");
            foreach (bool dark in new[] { false, true })
            {
                shell.Renderer.ColorScheme = new(dark);
                var scene = shell.Render(size);
                if (Find("ServerName") != name || !ReferenceEquals(draft, shell.Tree.GetComponent<XsrUiTextInput>(name))
                    || draft.ReadDraft() != serverDraft || draft.SelectionEnd != caret || draft.SelectionStart != anchor
                    || shell.Renderer.Focused != name || !scene.Nodes.Any(node => node.Entity == servers && node.Rect.Width > 0))
                    throw new InvalidOperationException("Constrained themed server scene lost retained state.");
            }
            store.Publish(languageKey, "en"); language.SetLanguage("en"); shell.Render(size);
            if (Find("ServerName") != name || draft.ReadDraft() != serverDraft || draft.SelectionEnd != caret
                || shell.Tree.GetComponent<XsrUiText>(Find("ServerEntry.0.Address")) is not { Localize: false } address
                || address.Content != SettingsWorldValidationFacts.AddressLiteral || !HasLiteralText(serverRow, SettingsWorldValidationFacts.StatusLiteral))
                throw new InvalidOperationException("Server locale reload changed literal authority or editor state.");

            Progress("world details and health");
            await Select("saves").ConfigureAwait(false);
            await Pump(() => Optional("ManagementContentDetails.FixtureWorld").IsAssigned, "controlled world content").ConfigureAwait(false);
            await Activate("ManagementContentDetails.FixtureWorld").ConfigureAwait(false);
            await Pump(() => Optional("WorldDetails").IsAssigned && Optional("WorldSnapshots").IsAssigned, "world detail and snapshot slots").ConfigureAwait(false);
            var details = Find("WorldDetails"); var snapshots = Find("WorldSnapshots");
            RequireOwned(details); RequireOwned(snapshots);
            await Activate(SettingsMigrationWorldKey("Health")).ConfigureAwait(false);
            await Pump(() => fixture.HealthReads == 1 && HasLiteralText(Optional("WorldHealth"), "128", exact: true), "captured world health result").ConfigureAwait(false);
            if (!HasLiteralText(details, SettingsWorldValidationFacts.WorldLiteral, exact: true)
                || details != Find("WorldDetails") || snapshots != Find("WorldSnapshots"))
                throw new InvalidOperationException("World health completion lost literal facts or workspace identities.");
            Progress("snapshot append reorder and removal");
            await Activate(SettingsMigrationWorldKey("SnapshotRead")).ConfigureAwait(false);
            await Pump(() => fixture.SnapshotReads == 1 && Optional("WorldSnapshots.Rows").IsAssigned
                && shell.Tree.Children(Find("WorldSnapshots.Rows")).Count == 2, "snapshot append").ConfigureAwait(false);
            var rowsSlot = Find("WorldSnapshots.Rows");
            var firstOrder = shell.Tree.Children(rowsSlot).ToArray();
            foreach (var row in firstOrder) RequireOwned(row);
            await Activate(SettingsMigrationWorldKey("SnapshotRead")).ConfigureAwait(false);
            await Pump(() => fixture.SnapshotReads == 2 && shell.Tree.Children(rowsSlot).SequenceEqual(firstOrder.Reverse()), "retained snapshot reorder").ConfigureAwait(false);
            await Activate(SettingsMigrationWorldKey("SnapshotRead")).ConfigureAwait(false);
            await Pump(() => fixture.SnapshotReads == 3 && shell.Tree.Children(rowsSlot).Count == 1, "owned snapshot removal").ConfigureAwait(false);
            if (shell.Tree.IsAlive(firstOrder[0]) || shell.Tree.Children(rowsSlot)[0] != firstOrder[1])
                throw new InvalidOperationException("Snapshot collection did not remove only the retired fragment and retain the survivor.");
            var verify = Find(shell.Tree.Name(firstOrder[1]) + ".Verify");
            await Activate(shell.Tree.Name(verify)).ConfigureAwait(false);
            await Pump(() => fixture.VerificationReads == 1, "captured snapshot verification").ConfigureAwait(false);
            Progress("retired instance cancellation");
            instance = Path.Combine(directory, "retired-scope");
            await Pump(() => controller.SelectedSection == "overview", "scope retirement").ConfigureAwait(false);
            fixture.CompleteRetiredVerification();
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
            var retiredScene = shell.Render(size);
            if (shell.Tree.IsAlive(verify) || !fixture.VerificationCancelled
                || HasLiteralText(controller.Page, SettingsWorldValidationFacts.RetiredLiteral)
                || retiredScene.Nodes.Any(node => node.Text?.Contains(SettingsWorldValidationFacts.RetiredLiteral, StringComparison.Ordinal) == true))
                throw new InvalidOperationException("Retired world query escaped its cancelled instance scope.");
            instance = directory;
            await Pump(() => controller.SelectedSection == "overview" && Optional("SettingsNav.modpack").IsAssigned, "restored fixture scope").ConfigureAwait(false);

            Progress("export draft and checkbox");
            await Select("modpack").ConfigureAwait(false);
            await Pump(() => fixture.ExportPreviewReads == 1 && Optional("ExportCategoryChoice.saves").IsAssigned, "typed export preview").ConfigureAwait(false);
            var export = Find("ExportWorkspace"); RequireOwned(export);
            await Show("ExportName").ConfigureAwait(false);
            var packName = Find("ExportName");
            const string packDraft = "literal <pack draft> {data private}";
            shell.Renderer.SetTextInputValue(packName, packDraft); shell.Renderer.Focus(packName, showIndicator: false);
            shell.Renderer.EditText(XsrUiTextEdit.Left, extendSelection: true);
            var packInput = shell.Tree.GetComponent<XsrUiTextInput>(packName)!;
            int packCaret = packInput.SelectionEnd;
            var choice = Find("ExportCategoryChoice.saves"); var choiceInput = shell.Tree.GetComponent<XsrUiInput>(choice);
            await Activate("ExportCategoryChoice.saves").ConfigureAwait(false);
            var exportScene = shell.Render(size);
            if (!exportScene.Nodes.Any(node => node.Entity == choice && node.IsChecked == true)
                || Find("ExportWorkspace") != export || Find("ExportName") != packName || packInput.ReadDraft() != packDraft
                || packInput.SelectionEnd != packCaret || shell.Renderer.Focused != packName
                || !ReferenceEquals(choiceInput, shell.Tree.GetComponent<XsrUiInput>(choice)) || fixture.Writes != 0)
                throw new InvalidOperationException("Export checkbox reload lost retained draft/control state or performed a write.");
            foreach (bool dark in new[] { false, true })
            {
                shell.Renderer.ColorScheme = new(dark); shell.Render(size);
                if (Find("ExportName") != packName || packInput.ReadDraft() != packDraft || packInput.SelectionEnd != packCaret)
                    throw new InvalidOperationException("Constrained export theme changed editor state.");
            }
            Progress("recovery paging");
            await Select("recovery").ConfigureAwait(false);
            await Pump(() => Optional("RecoveryChanges.Next").IsAssigned, "captured recovery workspace").ConfigureAwait(false);
            var recovery = Find("RecoveryWorkspace"); RequireOwned(recovery);
            await Activate("RecoveryChanges.Next").ConfigureAwait(false);
            await Show(shell.Tree.Name(shell.Tree.Children(Find("RecoveryChanges.Rows"))[0])).ConfigureAwait(false);
            var recoveryScene = shell.Render(size);
            if (Find("RecoveryWorkspace") != recovery || !HasLiteralText(recovery, "fixture-change-08")
                || !recoveryScene.Nodes.Any(node => node.Text?.Contains("fixture-change-08", StringComparison.Ordinal) == true)
                || fixture.Writes != 0)
                throw new InvalidOperationException("Recovery paging replaced its owned workspace or dispatched a restore.");
            Console.WriteLine("Dynamic settings world/server/export/recovery validation passed: typed scoped query fixtures, owned snapshot append/reorder/remove, cancelled retired scope, literal status/world data, retained drafts/checkbox focus and light/dark 810x500 scenes; zero world/server/export/restore writes.");
        }
        catch (OperationCanceledException error) when (timeout.IsCancellationRequested)
        {
            Console.Error.WriteLine("Settings world fixture timeout during: " + step);
            throw new InvalidOperationException("Settings world fixture timed out during: " + step, error);
        }
        finally
        {
            fixture.Dispose(); controller.Dispose();
            store.Publish(languageKey, previousLanguage ?? "auto");
            shell.Renderer.TextLocalizer = previousLocalizer; shell.Renderer.ColorScheme = previousScheme; shell.Renderer.ReducedMotion = previousMotion;
            if (shell.Tree.IsAlive(previous)) shell.Stage.Navigation.Replace(previous);
            if (shell.Tree.IsAlive(controller.Page)) shell.Tree.Destroy(controller.Page);
            Directory.Delete(directory, recursive: true);
        }

        XsrUiEntityId Optional(string key)
        {
            XsrUiEntityId found = default; int matches = 0;
            shell.Tree.Walk(controller.Page, entity => { if (shell.Tree.Name(entity) == key) { found = entity; matches++; } return true; });
            if (matches > 1) throw new InvalidOperationException("Settings fixture key is ambiguous within its owned page: " + key);
            return found;
        }
        XsrUiEntityId Find(string key) => Optional(key) is { IsAssigned: true } found ? found
            : throw new InvalidOperationException("Settings fixture key is missing from its owned page: " + key);
        bool HasLiteralText(XsrUiEntityId root, string value, bool exact = false)
        {
            if (!shell.Tree.IsAlive(root)) return false;
            bool found = false;
            shell.Tree.Walk(root, entity =>
            {
                if (shell.Tree.GetComponent<XsrUiText>(entity) is { Localize: false } text
                    && (exact ? text.Content == value : text.Content.Contains(value, StringComparison.Ordinal))) found = true;
                return true;
            });
            return found;
        }
        void Progress(string operation)
        {
            step = operation; Console.WriteLine("Settings world fixture step: " + operation);
        }
        void RequireOwned(XsrUiEntityId root)
        {
            if (shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(root)) is null)
                throw new InvalidOperationException("Settings fixture fragment left its owned Dynamic slot: " + shell.Tree.Name(root));
        }
        async Task Pump(Func<bool> ready, string operation)
        {
            try
            {
                while (true)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    shell.Render(size);
                    if (ready()) return;
                    await Task.Delay(5, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException error) when (timeout.IsCancellationRequested)
            {
                Console.Error.WriteLine("Settings world fixture timeout waiting for: " + operation);
                throw new InvalidOperationException("Settings world fixture timed out waiting for: " + operation, error);
            }
        }
        async Task Select(string section)
        {
            intents.Emit(XsrSemanticId.Parse("ui.settings.section"), Find("SettingsNav." + section), XsrCorrelationId.Create());
            await Pump(() => controller.SelectedSection == section, "section " + section).ConfigureAwait(false);
        }
        async Task Show(string key)
        {
            int searchDirection = 1;
            bool reversedSearch = false;
            for (int iteration = 0; iteration < 64; iteration++)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var scene = shell.Render(size); var entity = Find(key);
                var target = scene.Nodes.Where(item => item.Entity == entity).Select(item => (XsrUiSceneNode?)item).FirstOrDefault();
                if (target is { } node)
                {
                    var point = new XsrUiPoint(node.Rect.X + Math.Min(12, node.Rect.Width / 2), node.Rect.Y + Math.Min(12, node.Rect.Height / 2));
                    if (node.IsAccessible && node.Rect.Width > 0 && node.Rect.Height > 0 && (node.ClipRect is null || node.ClipRect.Value.Contains(point))) return;
                }
                XsrUiSceneNode? viewport = null; XsrUiPoint gesture = default; XsrUiEntityId scrollEntity = default;
                for (var ancestor = shell.Tree.Parent(entity); ancestor.IsAssigned && shell.Tree.IsAlive(ancestor); ancestor = shell.Tree.Parent(ancestor))
                {
                    if (shell.Tree.GetComponent<XsrUiScroll>(ancestor) is null) continue;
                    var candidate = scene.Nodes.Where(item => item.Entity == ancestor).Select(item => (XsrUiSceneNode?)item).FirstOrDefault();
                    if (candidate is not { IsAccessible: true } scrollNode) continue;
                    double left = Math.Max(scrollNode.Rect.X, scrollNode.ClipRect?.X ?? scrollNode.Rect.X);
                    double top = Math.Max(scrollNode.Rect.Y, scrollNode.ClipRect?.Y ?? scrollNode.Rect.Y);
                    double right = Math.Min(scrollNode.Rect.X + scrollNode.Rect.Width, scrollNode.ClipRect is { } horizontalClip ? horizontalClip.X + horizontalClip.Width : scrollNode.Rect.X + scrollNode.Rect.Width);
                    double bottom = Math.Min(scrollNode.Rect.Y + scrollNode.Rect.Height, scrollNode.ClipRect is { } verticalClip ? verticalClip.Y + verticalClip.Height : scrollNode.Rect.Y + scrollNode.Rect.Height);
                    if (right <= left || bottom <= top) continue;
                    viewport = scrollNode; scrollEntity = ancestor;
                    gesture = new(left + Math.Min(24, (right - left) / 2), top + Math.Min(24, (bottom - top) / 2));
                    break;
                }
                if (viewport is not { } visibleViewport)
                    throw new InvalidOperationException("Offscreen Settings fixture action has no visible live scroll ancestor: " + key);
                bool horizontal = shell.Tree.GetComponent<XsrUiStackPanel>(scrollEntity)?.Direction == XsrUiOrientation.Horizontal;
                double delta = target is { } hidden
                    ? (horizontal ? hidden.Rect.X < visibleViewport.Rect.X : hidden.Rect.Y < visibleViewport.Rect.Y) ? -120 : 120
                    : searchDirection * 120;
                if (!shell.Renderer.PointerScroll(gesture, horizontal ? 0 : delta, horizontal ? delta : 0))
                {
                    if (target is null && !reversedSearch) { searchDirection = -searchDirection; reversedSearch = true; continue; }
                    throw new InvalidOperationException("Settings viewport rejected the action scroll: " + key);
                }
                await Task.Delay(1, timeout.Token).ConfigureAwait(false);
            }
            throw new InvalidOperationException("Settings fixture action could not enter the constrained viewport: " + key);
        }
        async Task Activate(string key)
        {
            await Show(key).ConfigureAwait(false);
            if (!shell.Renderer.Activate(Find(key))) throw new InvalidOperationException("Settings fixture action did not activate: " + key);
            shell.Render(size);
        }
    }

    private static string SettingsMigrationWorldKey(string name) => DesktopMarkup.Load("SettingsWorldTerms.nxml").Root.Children
        .Single(node => node.Key == "action." + name).Content!;

    private sealed class SettingsWorldValidationFacts : IDisposable
    {
        internal const string AddressLiteral = "literal <address> {state private}";
        internal const string StatusLiteral = "literal <status> {data private}";
        internal const string WorldLiteral = "literal <world> {state private}";
        internal const string RetiredLiteral = "retired <verification> {data private}";
        private readonly TaskCompletionSource<XsrResult<InstanceServerStatus>> _status = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<XsrResult<InstanceWorldSnapshotVerification>> _verification = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _verificationCancellation;
        internal bool VerificationCancelled { get; private set; }
        internal int ServerStatusReads { get; private set; }
        internal int ServerListReads { get; private set; }
        internal int HealthReads { get; private set; }
        internal int SnapshotReads { get; private set; }
        internal int VerificationReads { get; private set; }
        internal int ExportPreviewReads { get; private set; }
        internal int Writes { get; private set; }
        internal XsrQueryRouter Queries { get; }
        internal XsrCommandRouter Commands { get; }
        internal SettingsWorldValidationFacts(string directory, long revision)
        {
            string retiredDirectory = Path.Combine(directory, "retired-scope");
            foreach (string scope in new[] { directory, retiredDirectory })
                Directory.CreateDirectory(Path.Combine(scope, "saves", "FixtureWorld"));
            var queries = new XsrQueryRouterBuilder();
            SettingsCatalogPage[] pages = [new("overview", "Overview"), new("game", "Game"), new("servers", "Servers"), new("saves", "Worlds"), new("modpack", "Export"), new("recovery", "Recovery")];
            queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery, (query, _) =>
                ValueTask.FromResult(XsrResult.Success(SettingsCatalog.Read(query))));
            queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery, (_, _) =>
                ValueTask.FromResult(XsrResult.Success(new SettingsEffectiveSnapshot(revision, SettingsPolicySchema.Definitions.Select(definition =>
                    new SettingsEffectiveValue(definition.Key, new(SettingsOverrideMode.Custom,
                        definition.Key is "developer.enabled" or "instance.development-mode" ? "true" : definition.DefaultValue), SettingsLayer.Instance, definition.Timing, null)).ToArray()))));
            var metadata = new InstanceWorldMetadata(WorldLiteral, "1.21.1", 4189, null, 42, 1, 2, false, 1024, true, "world-revision", false, []) { DataPacksSupported = true };
            Guid baseline = Guid.Parse("68f78247-788a-48af-a4b4-c4a1d4d463c9");
            var changes = Enumerable.Range(0, 11).Select(index => new InstanceRecoveryChange(InstanceRecoveryChangeKind.Modified,
                "mods", "fixture-change-" + index.ToString("00", CultureInfo.InvariantCulture) + ".txt")).ToArray();
            queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query, (query, _) =>
            {
                if (query.InstanceDirectory != directory && query.InstanceDirectory != retiredDirectory)
                    throw new InvalidOperationException("Management fixture query crossed its two controlled instance scopes.");
                return ValueTask.FromResult(XsrResult.Success(new InstanceManagementSnapshot(query.InstanceDirectory, query.InstanceDirectory, "1.21.1", [],
                    pages.Select(page => new InstanceManagementPage(page.Id, page.Label,
                        page.Id == "saves" ? Path.Combine(query.InstanceDirectory, "saves") : null)).ToArray(), true, "fixture")
                {
                    Contents = [new("saves", [new("FixtureWorld", true, 1024) { World = metadata }], true, null)],
                    RecoveryStorage = new(4096, 1024, true, [new(baseline, DateTimeOffset.UnixEpoch, 11, 1024)]),
                    RecoveryComparison = new(query.InstanceDirectory, baseline, DateTimeOffset.UnixEpoch, changes, "fixture-fingerprint"),
                }));
            });
            queries.Register<InstanceServerListQuery, InstanceServerList>(InstanceServerListContract.Read, (query, _) =>
            {
                RequireScope(query.InstanceDirectory, directory); ServerListReads++;
                return ValueTask.FromResult(XsrResult.Success(new InstanceServerList("fixture-server-revision", [new(37, "FixtureServer", AddressLiteral)])));
            });
            queries.Register<InstanceServerStatusQuery, InstanceServerStatus>(InstanceServerListContract.Status, (query, _) =>
            {
                RequireScope(query.InstanceDirectory, directory);
                if (query.ExpectedRevision != "fixture-server-revision" || query.SourceIndex != 37) throw new InvalidOperationException("Server fixture lost captured revision/source identity.");
                ServerStatusReads++; return new(_status.Task);
            });
            queries.Register<InstanceWorldHealthQuery, InstanceWorldHealth>(InstanceWorldHealthContract.Read, (query, _) =>
            {
                RequireWorld(query.InstanceDirectory, query.WorldName, directory); HealthReads++;
                return ValueTask.FromResult(XsrResult.Success(new InstanceWorldHealth(true, 2, 128, 0, [])));
            });
            var a = new InstanceWorldSnapshot(new string('a', 64), DateTimeOffset.UnixEpoch, 7, 1024);
            var b = new InstanceWorldSnapshot(new string('b', 64), DateTimeOffset.UnixEpoch.AddHours(1), 9, 2048);
            queries.Register<InstanceWorldSnapshotListQuery, IReadOnlyList<InstanceWorldSnapshot>>(InstanceWorldSnapshotContract.List, (query, _) =>
            {
                RequireWorld(query.InstanceDirectory, query.WorldName, directory); SnapshotReads++;
                IReadOnlyList<InstanceWorldSnapshot> rows = SnapshotReads switch { 1 => [a, b], 2 => [b, a], _ => [b] };
                return ValueTask.FromResult(XsrResult.Success(rows));
            });
            queries.Register<InstanceWorldSnapshotVerifyQuery, InstanceWorldSnapshotVerification>(InstanceWorldSnapshotContract.Verify, (query, token) =>
            {
                RequireWorld(query.InstanceDirectory, query.WorldName, directory);
                if (query.Identity != b.Identity) throw new InvalidOperationException("Snapshot verification lost its captured identity.");
                VerificationReads++; _verificationCancellation = token.Register(() => VerificationCancelled = true);
                return new(_verification.Task);
            });
            queries.Register<InstanceModpackExportQuery, InstanceModpackExportPreview>(InstanceModpackExportContract.Preview, (query, _) =>
            {
                RequireScope(query.InstanceDirectory, directory); ExportPreviewReads++;
                return ValueTask.FromResult(XsrResult.Success(new InstanceModpackExportPreview(directory, [new("mods", "Mods", 40, 1048576), new("saves", "Saves", 1, 1024)])));
            });
            var commands = new XsrCommandRouterBuilder();
            commands.Register<InstanceServerListSaveCommand>(InstanceServerListContract.Save, (_, _) => { Writes++; return ValueTask.FromResult(XsrResult.Success()); });
            commands.Register<InstanceModpackExportCommand>(InstanceModpackExportContract.Export, (_, _) => { Writes++; return ValueTask.FromResult(XsrResult.Success()); });
            Queries = queries.Build(new ValidationDispatchObserver()); Commands = commands.Build(new ValidationDispatchObserver());
        }
        internal void CompleteServerStatus() => _status.TrySetResult(XsrResult.Success(new InstanceServerStatus(true, StatusLiteral, "fixture-version", 3, 20, 5) { ClientProtocol = 763, ServerProtocol = 764 }));
        internal void CompleteRetiredVerification() => _verification.TrySetResult(XsrResult.Success(new InstanceWorldSnapshotVerification(new string('b', 64), 9, 8, [RetiredLiteral])));
        public void Dispose() { _verificationCancellation.Dispose(); _status.TrySetCanceled(); _verification.TrySetCanceled(); }
        private static void RequireScope(string actual, string expected)
        {
            if (actual != expected) throw new InvalidOperationException("Settings fixture query crossed its exact instance scope.");
        }
        private static void RequireWorld(string actual, string world, string expected)
        {
            RequireScope(actual, expected);
            if (world != "FixtureWorld") throw new InvalidOperationException("Settings fixture query crossed its world identity.");
        }
    }
}
