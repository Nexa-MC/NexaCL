using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SettingsRecoveryWorkspaceRetainsControlsScrollAndLiteralRows()
    {
        using var fixture = new RecoveryNxmlFixture();
        fixture.OpenRecovery();
        var shell = fixture.Host.Shell;
        var workspace = fixture.Find("RecoveryWorkspace");
        var policy = fixture.Find("SettingsOption.recovery.keep-history.true");
        var historyNext = fixture.Find("RecoveryHistory.Next");
        var changeNext = fixture.Find("RecoveryChanges.Next");
        var sections = shell.Tree.Parent(workspace);
        var scroll = shell.Tree.GetComponent<XsrUiScroll>(sections)!;
        AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(sections) is not null);
        AssertTrue(shell.Renderer.Focus(policy, showIndicator: false));
        fixture.CompleteRecovery();
        fixture.Wait(() => fixture.Rows("RecoverySnapshot.").Length == 8);
        AssertEqual(workspace, fixture.Find("RecoveryWorkspace"));
        AssertEqual(policy, fixture.Find("SettingsOption.recovery.keep-history.true"));
        AssertEqual(policy, shell.Renderer.Focused);
        AssertTrue(ReferenceEquals(scroll, shell.Tree.GetComponent<XsrUiScroll>(sections)));
        scroll.OffsetY = 17; fixture.Render();
        AssertEqual(17d, scroll.OffsetY);
        AssertEqual(8, fixture.Rows("RecoveryChange.").Length);
        AssertTrue(fixture.HasText("config/<literal> {data fake} {state secret} 0.cfg"));
        var firstSnapshot = fixture.Rows("RecoverySnapshot.")[0];
        AssertTrue(shell.Renderer.Focus(historyNext, showIndicator: false));
        fixture.Click("RecoveryHistory.Next");
        AssertEqual(1, fixture.Rows("RecoverySnapshot.").Length);
        AssertFalse(shell.Tree.IsAlive(firstSnapshot));
        AssertEqual(workspace, fixture.Find("RecoveryWorkspace"));
        AssertEqual(historyNext, fixture.Find("RecoveryHistory.Next"));
        AssertEqual(historyNext, shell.Renderer.Focused);
        var firstChange = fixture.Rows("RecoveryChange.")[0];
        fixture.Click("RecoveryChanges.Next");
        AssertEqual(1, fixture.Rows("RecoveryChange.").Length);
        AssertFalse(shell.Tree.IsAlive(firstChange));
        AssertEqual(changeNext, fixture.Find("RecoveryChanges.Next"));
        AssertEqual(historyNext, shell.Renderer.Focused);
        var lastChange = fixture.Rows("RecoveryChange.")[0];
        shell.Renderer.TextLocalizer = source => source switch
        {
            "回滚所选更改" => "Confirm rollback",
            "更改 {data page}/{data pages} · 下一页" => "Changes {data page}/{data pages} · Next",
            "快照 {data page}/{data pages} · 下一页" => "Snapshots {data page}/{data pages} · Next",
            _ => source,
        };
        fixture.Render();
        AssertEqual("Changes 2/2 · Next", shell.Tree.GetComponent<XsrUiText>(changeNext)!.Content);
        AssertEqual(lastChange, fixture.Rows("RecoveryChange.")[0]);
        AssertEqual(historyNext, shell.Renderer.Focused);
        AssertEqual(0, fixture.Writes.Count);
        AssertTrue(fixture.HasText("config/<literal> {data fake} {state secret} 8.cfg"));
    }

    private static void SettingsRecoveryConfirmationsRetireAcrossScopeChanges()
    {
        using var fixture = new RecoveryNxmlFixture();
        fixture.OpenRecovery(); fixture.CompleteRecovery();
        fixture.Wait(() => fixture.Rows("RecoveryChange.").Length == 8);
        fixture.Click("RecoveryChanges.RestoreAll");
        var retired = fixture.Host.Feedback.Snapshot().Dialog!;
        AssertTrue(retired.Message.Contains("9 项更改", StringComparison.Ordinal));
        fixture.SelectedInstance = Path.Combine(fixture.Host.TemporaryDirectory, "other-root", "versions", "recovery");
        AssertTrue(fixture.Host.Feedback.ResolveDialog(retired.Id, true));
        AssertEqual(0, fixture.Writes.Count);
        fixture.SelectedInstance = fixture.Instance;
        var oldChange = fixture.Rows("RecoveryChange.")[0];
        fixture.Click("RecoveryChanges.RestoreAll");
        retired = fixture.Host.Feedback.Snapshot().Dialog!;
        fixture.Click("SettingsNav.game", "ui.settings.section");
        AssertTrue(fixture.Host.Feedback.ResolveDialog(retired.Id, true));
        AssertEqual(0, fixture.Writes.Count);
        AssertFalse(fixture.Host.Shell.Tree.IsAlive(oldChange));
        fixture.Click("SettingsNav.recovery", "ui.settings.section");
        fixture.Wait(() => fixture.Rows("RecoveryChange.").Length == 8);
        var workspace = fixture.Find("RecoveryWorkspace");
        fixture.Click("RecoveryChanges.RestoreAll");
        var current = fixture.Host.Feedback.Snapshot().Dialog!;
        AssertTrue(fixture.Host.Feedback.ResolveDialog(current.Id, true));
        fixture.Wait(() => fixture.Writes.Count == 1);
        AssertEqual(1, fixture.Writes.Count);
        AssertEqual(fixture.Instance, fixture.Writes[0].InstanceDirectory);
        AssertEqual(fixture.Comparison.BaselineRevision!.Value, fixture.Writes[0].BaselineRevision);
        AssertEqual(fixture.Comparison.Fingerprint, fixture.Writes[0].Fingerprint);
        AssertEqual(9, fixture.Writes[0].Changes.Count);
        fixture.Render();
        AssertEqual(workspace, fixture.Find("RecoveryWorkspace"));
        AssertFalse(fixture.Host.Shell.Tree.GetComponent<XsrUiInput>(fixture.Find("RecoveryChanges.RestoreAll"))!.Enabled);
        current.Resolve(true);
        AssertEqual(1, fixture.Writes.Count);
    }

    private sealed class RecoveryNxmlFixture : IDisposable
    {
        internal readonly LaunchPageFixture Host = new(new ImmediateInstanceSource([]));
        internal readonly List<InstanceRecoveryRestoreCommand> Writes = [];
        private readonly TaskCompletionSource<XsrResult<InstanceManagementSnapshot>> _recoveryRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<XsrResult> _restore = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SettingsPageController _settings;
        private bool _recoveryComplete;
        private int _recoveryReads;
        internal string Instance { get; }
        internal string SelectedInstance { get; set; }
        internal InstanceRecoveryReport Comparison { get; }

        internal RecoveryNxmlFixture()
        {
            Instance = Path.Combine(Host.TemporaryDirectory, "minecraft", "versions", "recovery");
            SelectedInstance = Instance; EnableInstanceDevelopmentMode(Host, Instance);
            Comparison = new(Instance, Guid.NewGuid(), DateTimeOffset.UtcNow,
                Enumerable.Range(0, 9).Select(index => new InstanceRecoveryChange(InstanceRecoveryChangeKind.Modified,
                    "配置", "config/<literal> {data fake} {state secret}\t" + index + ".cfg")).ToArray(), "fixture-fingerprint");
            var queries = new XsrQueryRouterBuilder();
            Forward<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery);
            Forward<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery);
            queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query, (query, _) =>
            {
                if (query.IncludeRecoveryStorage)
                {
                    _recoveryReads++;
                    if (!_recoveryComplete) return new(_recoveryRead.Task);
                }
                return ValueTask.FromResult(XsrResult.Success(Snapshot(query.InstanceDirectory, query.IncludeRecoveryStorage)));
            });
            var commands = new XsrCommandRouterBuilder();
            commands.Register<InstanceRecoveryRestoreCommand>(InstanceRecoveryContract.Restore, (command, _) =>
            { Writes.Add(command); return new(_restore.Task); });
            _settings = new(Host.Shell, Host.Intents, queries.Build(new NoopDispatchObserver()), commands.Build(new NoopDispatchObserver()),
                Host.Store, Host.Feedback, () => SelectedInstance);
            Host.Shell.Renderer.ReducedMotion = true; Host.Shell.Stage.Navigation.Replace(_settings.Page);
            Wait(() => Find("SettingsNav.recovery", false).IsAssigned);

            void Forward<TQuery, TResponse>(XsrSemanticId semantic) where TQuery : notnull
            {
                AssertTrue(Host.Foundation.Queries.TryResolve(semantic, out var route));
                queries.Register<TQuery, TResponse>(semantic,
                    (query, token) => Host.Foundation.Queries.QueryAsync<TQuery, TResponse>(route, query, cancellationToken: token));
            }
        }

        internal void OpenRecovery()
        { Click("SettingsNav.recovery", "ui.settings.section"); Wait(() => _recoveryReads == 1 && Find("RecoveryWorkspace", false).IsAssigned); }
        internal void CompleteRecovery()
        { _recoveryComplete = true; _recoveryRead.SetResult(XsrResult.Success(Snapshot(Instance, true))); }
        private InstanceManagementSnapshot Snapshot(string instance, bool loaded) => new(instance, instance, "1.21.1", [],
            [new("overview", "总览"), new("game", "游戏设置"), new("recovery", "快照与存储")], true, "")
        {
            RecoveryComparison = loaded ? Comparison : null,
            RecoveryStorage = loaded ? new(2048, 1024, true, Enumerable.Range(0, 9).Select(index =>
                new InstanceRecoverySnapshotSummary(index == 0 ? Comparison.BaselineRevision!.Value : Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-index), index + 1, 1024)).ToArray()) : null,
        };
        internal void Render() => Host.Shell.Render(new(1400, 650));
        internal void Click(string key, string command = "ui.settings.management.action")
        { Emit(Host.Intents, command, Find(key)); Render(); }
        internal void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() => { Render(); return condition(); }, TimeSpan.FromSeconds(5)));
        internal XsrUiEntityId Find(string key, bool required = true)
        {
            XsrUiEntityId found = default;
            Host.Shell.Tree.Walk(_settings.Page, entity => { if (Host.Shell.Tree.Name(entity) == key) found = entity; return true; });
            if (required) AssertTrue(found.IsAssigned); return found;
        }
        internal XsrUiEntityId[] Rows(string prefix)
        {
            List<XsrUiEntityId> rows = [];
            Host.Shell.Tree.Walk(_settings.Page, entity =>
            { if (Host.Shell.Tree.Name(entity).StartsWith(prefix, StringComparison.Ordinal) && !Host.Shell.Tree.Name(entity).Contains("Restore", StringComparison.Ordinal)) rows.Add(entity); return true; });
            return rows.ToArray();
        }
        internal bool HasText(string text)
        {
            bool found = false; Host.Shell.Tree.Walk(_settings.Page, entity =>
            { if (Host.Shell.Tree.GetComponent<XsrUiText>(entity)?.Content == text) found = true; return true; }); return found;
        }
        public void Dispose() { _restore.TrySetResult(XsrResult.Success()); _settings.Dispose(); Host.Dispose(); }
    }
}
