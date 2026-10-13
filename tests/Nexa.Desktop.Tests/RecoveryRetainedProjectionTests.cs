using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void RecoveryReloadKeepsTimelineOfflineControlsAndPendingReadAcrossSnapshots()
    {
        using var fixture = new RecoveryRetainedFixture();
        fixture.OpenRecovery();
        var shell = fixture.Host.Shell;
        var timeline = fixture.Find("RecoveryTimeline");
        var offline = fixture.Find("InstanceOfflineReadiness");
        var check = fixture.Find("InstanceOfflineReadiness.Check");
        var policy = fixture.Find("SettingsOption.recovery.keep-history.true");
        fixture.Click("InstanceOfflineReadiness.Check");
        AssertEqual(1, fixture.OfflineReads.Count);
        AssertTrue(shell.Renderer.Focus(policy, showIndicator: false));
        fixture.CompleteManagement(0, 2);
        fixture.Wait(() => fixture.TimelineRows().Length == 2);
        AssertEqual(timeline, fixture.Find("RecoveryTimeline"));
        AssertEqual(offline, fixture.Find("InstanceOfflineReadiness"));
        AssertEqual(check, fixture.Find("InstanceOfflineReadiness.Check"));
        AssertEqual(policy, shell.Renderer.Focused);
        AssertFalse(fixture.OfflineReads[0].Token.IsCancellationRequested);
        var rows = fixture.TimelineRows();
        const string path = "libraries/路径 <Node> {data fake} {state secret}.jar";
        fixture.OfflineReads[0].Completion.SetResult(XsrResult.Success(new InstanceOfflineReadinessReport(
            fixture.Instance, DateTimeOffset.UtcNow, true, false, null,
            [new("library", path, OfflineArtifactState.Missing, null, "provider {data detail}")])));
        fixture.Wait(() => fixture.Find("InstanceOfflineReadiness.Fact.0", false).IsAssigned);
        var fact = fixture.Find("InstanceOfflineReadiness.Fact.0");
        List<string> localized = [];
        shell.Renderer.TextLocalizer = source =>
        {
            localized.Add(source);
            return source switch
            {
                "{data time} · {data files} 个文件 · {data size}" => "{data time} · {data files} files · {data size}",
                "{data time} · {data files} 个文件 · {data size} · 当前恢复基线" => "{data time} · {data files} files · {data size} · Current baseline",
                "文件数量变化 · {data delta}" => "File delta · {data delta}",
                "缺失" => "Missing",
                _ => source,
            };
        };
        fixture.Render();
        AssertEqual(timeline, fixture.Find("RecoveryTimeline"));
        AssertEqual(offline, fixture.Find("InstanceOfflineReadiness"));
        AssertEqual(check, fixture.Find("InstanceOfflineReadiness.Check"));
        AssertEqual(fact, fixture.Find("InstanceOfflineReadiness.Fact.0"));
        AssertEqual(rows[0], fixture.TimelineRows()[0]);
        AssertEqual(rows[1], fixture.TimelineRows()[1]);
        AssertTrue(shell.Tree.GetComponent<XsrUiText>(shell.Tree.Children(rows[0])[0])!.Content.Contains(" files · ", StringComparison.Ordinal));
        AssertTrue(shell.Tree.GetComponent<XsrUiText>(fact)!.Content.Contains(path, StringComparison.Ordinal));
        AssertFalse(localized.Contains(path, StringComparer.Ordinal));
        AssertEqual(1, fixture.OfflineReads.Count);
        AssertEqual(1, fixture.ManagementReads.Count);
        AssertEqual(policy, shell.Renderer.Focused);

        // Change one committed policy to request a new management snapshot. Existing
        // timeline points and the offline action remain owned by their current page.
        bool selected = shell.Tree.GetComponent<XsrUiSelection>(policy)!.IsSelected;
        fixture.Click("SettingsOption.recovery.keep-history." + (selected ? "false" : "true"), "ui.settings.choice");
        fixture.Wait(() => fixture.ManagementReads.Count == 2);
        fixture.CompleteManagement(1, 3);
        fixture.Wait(() => fixture.TimelineRows().Length == 3);
        AssertEqual(timeline, fixture.Find("RecoveryTimeline"));
        AssertEqual(offline, fixture.Find("InstanceOfflineReadiness"));
        AssertEqual(check, fixture.Find("InstanceOfflineReadiness.Check"));
        AssertEqual(rows[0], fixture.TimelineRows()[0]);
        AssertEqual(rows[1], fixture.TimelineRows()[1]);
        AssertEqual(1, fixture.OfflineReads.Count);
    }

    private sealed class RecoveryRetainedFixture : IDisposable
    {
        internal readonly LaunchPageFixture Host = new(new ImmediateInstanceSource([]));
        internal readonly List<(InstanceManagementQuery Query, TaskCompletionSource<XsrResult<InstanceManagementSnapshot>> Completion)> ManagementReads = [];
        internal readonly List<(CancellationToken Token, TaskCompletionSource<XsrResult<InstanceOfflineReadinessReport>> Completion)> OfflineReads = [];
        private readonly SettingsPageController _settings;
        private readonly InstanceRecoverySnapshotSummary[] _points;
        internal string Instance { get; }

        internal RecoveryRetainedFixture()
        {
            Instance = Path.Combine(Host.TemporaryDirectory, "minecraft", "versions", "retained-recovery");
            EnableInstanceDevelopmentMode(Host, Instance);
            var captured = DateTimeOffset.UtcNow.AddMinutes(-3);
            _points = Enumerable.Range(0, 3).Select(index => new InstanceRecoverySnapshotSummary(
                Guid.NewGuid(), captured.AddMinutes(index), index + 1, 1024)).ToArray();
            var queries = new XsrQueryRouterBuilder();
            Forward<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery);
            Forward<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery);
            queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query, (query, _) =>
            {
                if (!query.IncludeRecoveryStorage) return ValueTask.FromResult(XsrResult.Success(Snapshot(query.InstanceDirectory, 0)));
                var completion = new TaskCompletionSource<XsrResult<InstanceManagementSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
                ManagementReads.Add((query, completion));
                return new(completion.Task);
            });
            queries.Register<InstanceOfflineReadinessQuery, InstanceOfflineReadinessReport>(InstanceOfflineReadinessContract.Query, (_, token) =>
            {
                var completion = new TaskCompletionSource<XsrResult<InstanceOfflineReadinessReport>>(TaskCreationOptions.RunContinuationsAsynchronously);
                OfflineReads.Add((token, completion)); return new(completion.Task);
            });
            _settings = new(Host.Shell, Host.Intents, queries.Build(new NoopDispatchObserver()), Host.Foundation.Commands,
                Host.Store, Host.Feedback, () => Instance);
            Host.Shell.Renderer.ReducedMotion = true; Host.Shell.Stage.Navigation.Replace(_settings.Page);
            Wait(() => Find("SettingsNav.recovery", false).IsAssigned);

            void Forward<TQuery, TResponse>(XsrSemanticId semantic) where TQuery : notnull
            {
                AssertTrue(Host.Foundation.Queries.TryResolve(semantic, out var route));
                queries.Register<TQuery, TResponse>(semantic,
                    (query, token) => Host.Foundation.Queries.QueryAsync<TQuery, TResponse>(route, query, cancellationToken: token));
            }
        }

        private InstanceManagementSnapshot Snapshot(string instance, int count) => new(instance, instance, "1.21.1", [],
            [new("overview", "总览"), new("game", "游戏设置"), new("recovery", "快照与存储")], true, "")
        {
            RecoveryStorage = count == 0 ? null : new(2048, 1024, true, _points.Take(count).ToArray()),
            RecoveryComparison = count == 0 ? null : new(instance, _points[0].Revision, _points[0].CapturedAt,
                [new(InstanceRecoveryChangeKind.Modified, "配置", "config/{data literal}.cfg")], "comparison"),
        };
        internal void CompleteManagement(int index, int count) => ManagementReads[index].Completion.SetResult(
            XsrResult.Success(Snapshot(ManagementReads[index].Query.InstanceDirectory, count)));
        internal void OpenRecovery()
        {
            Click("SettingsNav.recovery", "ui.settings.section");
            Wait(() => ManagementReads.Count == 1 && Find("RecoveryTimeline", false).IsAssigned
                && Find("InstanceOfflineReadiness.Check", false).IsAssigned);
        }
        internal void Render() => Host.Shell.Render(new(1400, 8000));
        internal void Click(string key, string command = "ui.settings.management.action")
        { Emit(Host.Intents, command, Find(key)); Render(); }
        internal void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() => { Render(); return condition(); }, TimeSpan.FromSeconds(5)));
        internal XsrUiEntityId Find(string key, bool required = true)
        {
            XsrUiEntityId found = default;
            Host.Shell.Tree.Walk(_settings.Page, entity => { if (Host.Shell.Tree.Name(entity) == key) found = entity; return true; });
            if (required) AssertTrue(found.IsAssigned); return found;
        }
        internal XsrUiEntityId[] TimelineRows() => Host.Shell.Tree.Children(Find("RecoveryTimeline.Rows")).ToArray();
        public void Dispose()
        {
            foreach (var (_, completion) in ManagementReads) completion.TrySetCanceled();
            foreach (var (_, completion) in OfflineReads) completion.TrySetCanceled();
            _settings.Dispose(); Host.Dispose();
        }
    }
}
