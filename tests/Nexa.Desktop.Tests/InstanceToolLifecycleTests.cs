using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void FileWorkspaceRetiresLateReadsPreservesPagingAndRejectsEditedPreview()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        string instance = Path.Combine(fixture.TemporaryDirectory, "root-a", "versions", "same-name");
        EnableInstanceDevelopmentMode(fixture, instance);
        var queries = new XsrQueryRouterBuilder();
        Forward(SettingsPolicyContract.CatalogQuery);
        ForwardEffective();
        int reads = 0, previews = 0, writes = 0;
        CancellationToken retiredToken = default;
        var retired = new TaskCompletionSource<XsrResult<InstanceFileDocument>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingPreview = new TaskCompletionSource<XsrResult<InstanceFileSavePreview>>(TaskCreationOptions.RunContinuationsAsynchronously);
        InstanceFileSavePreviewQuery? requestedPreview = null;
        string longName = new('x', 230); longName += ".cfg";
        queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query,
            (q, _) => ValueTask.FromResult(XsrResult.Success(new InstanceManagementSnapshot(q.InstanceDirectory, q.InstanceDirectory, "1.21.1", [],
                [new("overview", "总览"), new("game", "游戏设置"), new("files", "文件工作区")], true, ""))));
        queries.Register<InstanceFileListQuery, InstanceFileListing>(InstanceFileWorkspaceContract.List,
            (q, _) => ValueTask.FromResult(XsrResult.Success(new InstanceFileListing(q.InstanceDirectory, q.Area, q.RelativeDirectory, q.InstanceDirectory,
                Enumerable.Range(0, 30).Select(index => new InstanceFileEntry(index == 0 ? longName : "fixture-" + index + ".cfg", false, 123, 42)).ToArray(), true))));
        queries.Register<InstanceFileReadQuery, InstanceFileDocument>(InstanceFileWorkspaceContract.Read, (q, token) =>
        {
            reads++;
            AssertEqual(123L, q.ExpectedSize); AssertEqual(42L, q.ExpectedModifiedUtcTicks);
            if (reads == 1) { retiredToken = token; return new(retired.Task); }
            return ValueTask.FromResult(XsrResult.Success(new InstanceFileDocument(q,
                string.Join("\r\n", Enumerable.Range(0, 40).Select(index => "\tline=" + index)) + "\r\n", "original", "utf8", true)));
        });
        queries.Register<InstanceFileSavePreviewQuery, InstanceFileSavePreview>(InstanceFileWorkspaceContract.Preview, (q, _) =>
        {
            previews++; requestedPreview = q;
            return previews == 1 ? new(pendingPreview.Task) : ValueTask.FromResult(XsrResult.Success(Preview(q)));
        });
        var commands = new XsrCommandRouterBuilder();
        commands.Register<InstanceFileSaveCommand>(InstanceFileWorkspaceContract.Save,
            (_, _) => { writes++; return ValueTask.FromResult(XsrResult.Success()); });
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            commands.Build(new NoopDispatchObserver()), fixture.Store, fixture.Feedback, () => instance);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        Wait(() => Find("SettingsNav.files", false).IsAssigned);
        Click("SettingsNav.files", "ui.settings.section");
        Wait(() => Rows().Count == 25);
        var root = Find("FileWorkspace"); var area = Find("FileWorkspace.Area.config");
        var firstAction = Find("FileWorkspace.Open." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("config//" + longName)))[..24]);
        AssertTrue(Rows().All(row => fixture.Shell.Tree.Name(row).Length < 80));
        Click("FileWorkspace.NextFiles"); AssertEqual(5, Rows().Count);
        AssertEqual(root, Find("FileWorkspace")); AssertEqual(area, Find("FileWorkspace.Area.config"));
        Emit(fixture.Intents, "ui.settings.management.action", firstAction); fixture.Shell.Render(new(1400, 8000)); AssertEqual(0, reads);
        Click("FileWorkspace.NextFiles"); Click(fixture.Shell.Tree.Name(FindOpen())); AssertEqual(1, reads);
        Click(fixture.Shell.Tree.Name(FindOpen())); AssertEqual(1, reads);
        Click("SettingsNav.game", "ui.settings.section"); AssertTrue(retiredToken.IsCancellationRequested);
        retired.SetResult(XsrResult.Success(new InstanceFileDocument(new(instance, "config", longName, 123, 42), "retired", "old", "utf8", true)));
        Click("SettingsNav.files", "ui.settings.section");
        Wait(() => Rows().Count == 25);
        Click(fixture.Shell.Tree.Name(FindOpen()));
        Wait(() => Find("FileWorkspaceLineEditor", false).IsAssigned && fixture.Shell.Tree.GetComponent<XsrUiTextInput>(Find("FileWorkspaceLineEditor"))!.ReadDraft() == "\tline=0");
        var editor = Find("FileWorkspaceLineEditor");
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiTextInput>(editor)!.PreserveTabs);
        fixture.Shell.Renderer.SetTextInputValue(editor, "\tchanged=1"); fixture.Shell.Renderer.Focus(editor, showIndicator: false);
        int selection = fixture.Shell.Tree.GetComponent<XsrUiTextInput>(editor)!.SelectionEnd;
        Click("FileWorkspace.NextText");
        AssertEqual(editor, Find("FileWorkspaceLineEditor")); AssertEqual(editor, fixture.Shell.Renderer.Focused);
        AssertEqual(selection, fixture.Shell.Tree.GetComponent<XsrUiTextInput>(editor)!.SelectionEnd);
        Click("FileWorkspace.Preview"); AssertEqual(1, previews);
        Click("FileWorkspace.Preview"); AssertEqual(1, previews);
        AssertTrue(requestedPreview!.Text.StartsWith("\tchanged=1\r\n\tline=1\r\n", StringComparison.Ordinal));
        fixture.Shell.Renderer.SetTextInputValue(editor, "\tchanged=2");
        pendingPreview.SetResult(XsrResult.Success(Preview(requestedPreview)));
        Wait(() => fixture.Shell.Tree.GetComponent<XsrUiText>(Find("FileWorkspace.Error"))!.Content.Contains("预览", StringComparison.Ordinal));
        AssertFalse(Find("FileWorkspace.Save", false).IsAssigned); AssertEqual(0, writes);
        AssertEqual(editor, Find("FileWorkspaceLineEditor")); AssertEqual(editor, fixture.Shell.Renderer.Focused);
        Click("FileWorkspace.Preview"); Wait(() => Find("FileWorkspace.Save", false).IsAssigned);
        AssertTrue(requestedPreview!.Text.EndsWith("\tline=39\r\n", StringComparison.Ordinal));
        AssertTrue(requestedPreview.Text.StartsWith("\tchanged=2\r\n", StringComparison.Ordinal));
        Click("FileWorkspace.Save"); AssertEqual(1, writes);

        void Forward(XsrSemanticId key)
        { fixture.Foundation.Queries.TryResolve(key, out var route); queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(key,
            (q, ct) => fixture.Foundation.Queries.QueryAsync<SettingsCatalogQuery, SettingsCatalogSnapshot>(route, q, cancellationToken: ct)); }
        void ForwardEffective()
        { fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var route); queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
            (q, ct) => fixture.Foundation.Queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(route, q, cancellationToken: ct)); }
        static InstanceFileSavePreview Preview(InstanceFileSavePreviewQuery q) => new(q.Original, q.Text, "updated", 123, 124, 41, 41);
        XsrUiEntityId Find(string key, bool required = true)
        { XsrUiEntityId entity = default; fixture.Shell.Tree.Walk(settings.Page, e => { if (fixture.Shell.Tree.Name(e) == key) entity = e; return true; }); if (required) AssertTrue(entity.IsAssigned); return entity; }
        List<XsrUiEntityId> Rows()
        { List<XsrUiEntityId> rows = []; fixture.Shell.Tree.Walk(settings.Page, e => { if (fixture.Shell.Tree.Name(e).StartsWith("FileWorkspaceRow.", StringComparison.Ordinal)) rows.Add(e); return true; }); return rows; }
        XsrUiEntityId FindOpen() => fixture.Shell.Tree.Children(Rows()[0])[^1];
        void Click(string key, string command = "ui.settings.management.action")
        { Emit(fixture.Intents, command, Find(key)); fixture.Shell.Render(new(1400, 8000)); }
        void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() => { fixture.Shell.Render(new(1400, 8000)); return condition(); }, TimeSpan.FromSeconds(5)));
    }

    private static void OfflineReadinessCoalescesRetiresScopesAndInvalidatesSettingsRevision()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        string first = Path.Combine(fixture.TemporaryDirectory, "root-a", "versions", "same-name");
        string second = Path.Combine(fixture.TemporaryDirectory, "root-b", "versions", "same-name"), instance = first;
        var queries = new XsrQueryRouterBuilder();
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.CatalogQuery, out var catalog);
        fixture.Foundation.Queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var effective);
        queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
            (q, ct) => fixture.Foundation.Queries.QueryAsync<SettingsCatalogQuery, SettingsCatalogSnapshot>(catalog, q, cancellationToken: ct));
        queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery,
            (q, ct) => fixture.Foundation.Queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(effective, q, cancellationToken: ct));
        queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query,
            (q, _) => ValueTask.FromResult(XsrResult.Success(new InstanceManagementSnapshot(q.InstanceDirectory, q.InstanceDirectory, "1.21.1", [],
                [new("overview", "总览"), new("game", "游戏设置")], true, ""))));
        List<(InstanceOfflineReadinessQuery Query, CancellationToken Token, TaskCompletionSource<XsrResult<InstanceOfflineReadinessReport>> Completion)> requests = [];
        queries.Register<InstanceOfflineReadinessQuery, InstanceOfflineReadinessReport>(InstanceOfflineReadinessContract.Query, (q, ct) =>
        {
            var pending = new TaskCompletionSource<XsrResult<InstanceOfflineReadinessReport>>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Add((q, ct, pending)); return new(pending.Task);
        });
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback, () => instance);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        Wait(() => Find("InstanceOfflineReadiness.Check", false).IsAssigned);
        var button = Find("InstanceOfflineReadiness.Check");
        Click(); Click(); AssertEqual(1, requests.Count); AssertEqual(button, Find("InstanceOfflineReadiness.Check"));
        instance = second; Wait(() => requests[0].Token.IsCancellationRequested && Find("InstanceOfflineReadiness.Check", false).IsAssigned);
        requests[0].Completion.SetResult(XsrResult.Success(Report(first)));
        fixture.Shell.Render(new(1400, 8000)); AssertFalse(HasText("本地离线依赖校验通过"));
        Click(); AssertEqual(2, requests.Count);
        AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("general.language", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "zh-Hans"))).IsSuccess);
        requests[1].Completion.SetResult(XsrResult.Success(Report(second)));
        Wait(() => HasText("设置在检查期间改变，请重新检查。"));
        AssertFalse(HasText("本地离线依赖校验通过")); AssertEqual(2, requests.Count);
        Click(); AssertEqual(3, requests.Count);
        requests[2].Completion.SetResult(XsrResult.Success(Report(second)));
        Wait(() => HasText("本地离线依赖校验通过"));
        fixture.Shell.Render(new(1400, 8000)); AssertEqual(3, requests.Count);
        static InstanceOfflineReadinessReport Report(string path) => new(path, DateTimeOffset.UtcNow, true, true, "{data literal}",
            [new("client", "client.jar", OfflineArtifactState.Verified, 123, "verified")]);
        XsrUiEntityId Find(string key, bool required = true)
        { XsrUiEntityId entity = default; fixture.Shell.Tree.Walk(settings.Page, e => { if (fixture.Shell.Tree.Name(e) == key) entity = e; return true; }); if (required) AssertTrue(entity.IsAssigned); return entity; }
        bool HasText(string value)
        { bool found = false; fixture.Shell.Tree.Walk(settings.Page, e => { if (fixture.Shell.Tree.GetComponent<XsrUiText>(e)?.Content == value) found = true; return true; }); return found; }
        void Click() { Emit(fixture.Intents, "ui.settings.management.action", Find("InstanceOfflineReadiness.Check")); fixture.Shell.Render(new(1400, 8000)); }
        void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() => { fixture.Shell.Render(new(1400, 8000)); return condition(); }, TimeSpan.FromSeconds(5)));
    }
}
