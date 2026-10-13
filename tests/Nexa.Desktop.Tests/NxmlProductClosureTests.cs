using Nexa.Desktop.Ui;
using Nexa.Services.Network;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void NetworkProbeReloadPreservesProxyDraftCaretFocusAndControlIdentity()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var queries = new XsrQueryRouterBuilder();
        Forward<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery);
        Forward<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery);
        List<(CancellationToken Token, TaskCompletionSource<XsrResult<NetworkManualProbeSnapshot>> Completion)> probes = [];
        queries.Register<NetworkManualProbeQuery, NetworkManualProbeSnapshot>(NetworkDiagnosticsContract.Probe, (_, token) =>
        {
            var reply = new TaskCompletionSource<XsrResult<NetworkManualProbeSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
            probes.Add((token, reply)); return new(reply.Task);
        });
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, queries.Build(new NoopDispatchObserver()),
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        Wait(() => Find("SettingsNav.network", false).IsAssigned);
        Emit(fixture.Intents, "ui.settings.section", Find("SettingsNav.network"));
        Wait(() => Find("NetworkProbe.Start", false).IsAssigned);
        Emit(fixture.Intents, "ui.settings.proxy.mode", Find("SettingsProxyMode.2")); fixture.Shell.Render(new(1100, 2400));
        var field = Find("SettingsInput.network.proxy-address"); var probe = Find("NetworkProbe.Start");
        var trackEntity = Find("SettingsProxyMode"); var track = fixture.Shell.Tree.GetComponent<XsrUiSegmentedTrack>(trackEntity)!;
        const string draft = "http://literal.invalid/<Text/>?value={data host}";
        fixture.Shell.Renderer.SetTextInputValue(field, draft); AssertTrue(fixture.Shell.Renderer.Focus(field, showIndicator: false));
        fixture.Shell.Renderer.EditText(XsrUiTextEdit.Left);
        var input = fixture.Shell.Tree.GetComponent<XsrUiTextInput>(field)!;
        int start = input.SelectionStart, end = input.SelectionEnd;
        var transition = new XsrUiTransition(); fixture.Shell.Tree.SetComponent(field, transition);
        Emit(fixture.Intents, "ui.settings.network.probe.start", probe);
        Wait(() => probes.Count == 1);
        AssertRetained(); AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiInput>(probe)!.Enabled);
        Emit(fixture.Intents, "ui.settings.network.probe.start", probe); fixture.Shell.Render(new(1100, 2400)); AssertEqual(1, probes.Count);
        var timestamp = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        const string host = "literal {data host} <Text />";
        probes[0].Completion.SetResult(XsrResult.Success(new NetworkManualProbeSnapshot(timestamp, timestamp,
            [new(host, timestamp, 403, 12.5, null)])));
        Wait(() => fixture.Shell.Tree.GetComponent<XsrUiInput>(probe)!.Enabled);
        AssertRetained();
        XsrUiEntityId observation = default;
        fixture.Shell.Tree.Walk(settings.Page, entity =>
        { if (fixture.Shell.Tree.GetComponent<XsrUiText>(entity)?.Content.Contains(host, StringComparison.Ordinal) == true) observation = entity; return true; });
        AssertTrue(observation.IsAssigned); AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiText>(observation)!.Localize);
        Emit(fixture.Intents, "ui.settings.network.probe.start", probe); Wait(() => probes.Count == 2);
        Emit(fixture.Intents, "ui.settings.section", Find("SettingsNav.general")); Wait(() => probes[1].Token.IsCancellationRequested);
        probes[1].Completion.SetResult(XsrResult.Success(new NetworkManualProbeSnapshot(timestamp, timestamp,
            [new("retired.example", timestamp, 200, 0, null)])));
        fixture.Shell.Render(new(1100, 2400));
        AssertFalse(fixture.Shell.Tree.IsAlive(probe));
        bool leaked = false;
        fixture.Shell.Tree.Walk(settings.Page, entity =>
        { leaked |= fixture.Shell.Tree.GetComponent<XsrUiText>(entity)?.Content.Contains("retired.example", StringComparison.Ordinal) == true; return true; });
        AssertFalse(leaked);

        void AssertRetained()
        {
            AssertEqual(field, Find("SettingsInput.network.proxy-address")); AssertEqual(probe, Find("NetworkProbe.Start"));
            AssertEqual(trackEntity, Find("SettingsProxyMode")); AssertEqual(field, fixture.Shell.Renderer.Focused);
            AssertTrue(ReferenceEquals(input, fixture.Shell.Tree.GetComponent<XsrUiTextInput>(field)));
            AssertTrue(ReferenceEquals(track, fixture.Shell.Tree.GetComponent<XsrUiSegmentedTrack>(trackEntity)));
            AssertTrue(ReferenceEquals(transition, fixture.Shell.Tree.GetComponent<XsrUiTransition>(field)));
            AssertEqual(draft, input.ReadDraft()); AssertEqual(start, input.SelectionStart); AssertEqual(end, input.SelectionEnd);
        }
        void Forward<TQuery, TResponse>(XsrSemanticId semantic) where TQuery : notnull
        {
            AssertTrue(fixture.Foundation.Queries.TryResolve(semantic, out var route));
            queries.Register<TQuery, TResponse>(semantic,
                (query, token) => fixture.Foundation.Queries.QueryAsync<TQuery, TResponse>(route, query, cancellationToken: token));
        }
        void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        { fixture.Shell.Render(new(1100, 2400)); return condition(); }, TimeSpan.FromSeconds(5)));
        XsrUiEntityId Find(string name, bool required = true)
        {
            XsrUiEntityId found = default;
            fixture.Shell.Tree.Walk(settings.Page, entity => { if (fixture.Shell.Tree.Name(entity) == name) found = entity; return true; });
            if (required) AssertTrue(found.IsAssigned); return found;
        }
    }
}
