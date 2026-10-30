using Nexa.Desktop.Ui;
using Nexa.Services.Settings;
using Nexa.Services.Themes;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ThemeSettingsHideEmptyOptionAndPermissionGroups()
    {
        using var page = new ThemeSettingsFixture([]);
        AssertFalse(page.Tree.GetComponent<XsrUiElement>(page.Find("Theme.Options"))!.IsVisible);
        AssertFalse(page.Tree.GetComponent<XsrUiElement>(page.Find("Theme.Grants"))!.IsVisible);
        AssertTrue(page.Tree.GetComponent<XsrUiElement>(page.Find("Theme.Settings"))!.IsVisible);
        AssertTrue(page.Tree.GetComponent<XsrUiElement>(page.Find("Theme.LauncherIcon"))!.IsVisible);
    }

    private static void ThemeOptionsRetainLiteralDraftCaretAndFocusAcrossProjection()
    {
        using var page = new ThemeSettingsFixture([ThemeTestOption("draft", ThemeOptionKind.Text, "saved")]);
        var originalScheme = page.Renderer.ColorScheme;
        foreach (bool dark in new[] { false, true })
        {
            page.Renderer.ColorScheme = originalScheme with { IsDark = dark };
            foreach (double width in new[] { 760d, 1040d })
            {
                var scene = page.Fixture.Shell.Render(new(width, 2400));
                foreach (string key in new[] { "Theme.Settings", "Theme.Options", "Theme.LauncherIcon" })
                {
                    AssertInset(page.Find(key + ".Surface"), page.Find(key + ".Body"));
                }
                var entries = page.Tree.Children(page.Find("Theme.Rows"));
                AssertTrue(entries.Count > 0);
                foreach (var entry in entries)
                {
                    AssertInset(entry, page.Tree.Children(entry).Single());
                }

                void AssertInset(XsrUiEntityId surface, XsrUiEntityId body)
                {
                    var outer = scene.Nodes.Single(node => node.Entity == surface).Rect;
                    var inner = scene.Nodes.Single(node => node.Entity == body).Rect;
                    AssertTrue(inner.X > outer.X && inner.Y > outer.Y);
                    AssertTrue(inner.X + inner.Width < outer.X + outer.Width);
                    AssertTrue(inner.Y + inner.Height < outer.Y + outer.Height);
                }
            }
        }
        page.Renderer.ColorScheme = originalScheme;
        page.Render();
        var field = page.Input("draft");
        var input = page.Tree.GetComponent<XsrUiTextInput>(field)!;
        const string literal = "<Button Command=\"untrusted\" /> {data title} 中文";
        page.Renderer.SetTextInputValue(field, literal); page.Renderer.Focus(field, showIndicator: true);
        page.Renderer.EditText(XsrUiTextEdit.Left);
        int start = input.SelectionStart, end = input.SelectionEnd;
        page.Session.Touch(error: "literal {data title} <Text />"); page.Render();
        AssertEqual(field, page.Input("draft")); AssertTrue(ReferenceEquals(input, page.Tree.GetComponent<XsrUiTextInput>(field)));
        AssertEqual(literal, input.ReadDraft()); AssertEqual(start, input.SelectionStart); AssertEqual(end, input.SelectionEnd);
        AssertEqual(field, page.Renderer.Focused); AssertFalse(page.Tree.GetComponent<XsrUiSemantic>(field)!.Localize);
        AssertEqual("literal {data title} <Text />", page.Tree.GetComponent<XsrUiText>(page.Find("Theme.Error"))!.Content);
        AssertTrue(page.Fixture.Foundation.Host.SettingsPolicy.Set(new("general.language", SettingsLayer.Global,
            new(SettingsOverrideMode.Custom, "en"))).IsSuccess);
        page.Settings.PrepareStartupAsync(CancellationToken.None).GetAwaiter().GetResult(); page.Render();
        AssertEqual(field, page.Input("draft")); AssertEqual(start, input.SelectionStart); AssertEqual(end, input.SelectionEnd);
        AssertEqual(1, page.Session.RefreshCalls);
    }

    private static void ThemeSettingsPreserveIndependentDraftsAndRejectRetiredSources()
    {
        using var page = new ThemeSettingsFixture([ThemeTestOption("draft", ThemeOptionKind.Text, "saved"), ThemeTestOption("enabled", ThemeOptionKind.Boolean, "false")], twoThemes: true);
        var firstField = page.Input("draft");
        page.Renderer.SetTextInputValue(firstField, "first theme unsaved"); page.Renderer.Focus(firstField);
        var retiredSave = page.Find("Theme.OptionsSave");
        var retiredChoice = page.ActionForOption("enabled", ".Toggle");
        page.Session.Select("theme-b"); page.Render();
        AssertFalse(page.Tree.IsAlive(firstField));
        var secondField = page.Input("draft");
        page.Renderer.SetTextInputValue(secondField, "second theme unsaved"); page.Renderer.Focus(secondField); page.Render();
        AssertFalse(page.Tree.IsAlive(retiredChoice));
        Emit(page.Fixture.Intents, "ui.settings.theme.action", retiredChoice); page.Render();
        AssertEqual(0, page.Session.SaveCalls);
        AssertFalse(page.Tree.GetComponent<XsrUiToggle>(page.ActionForOption("enabled", ".Toggle"))!.IsChecked);
        // The retained global save button receives the current source only after projection.
        AssertEqual(retiredSave, page.Find("Theme.OptionsSave"));
        page.Session.Select("theme-a"); page.Render();
        AssertEqual("first theme unsaved", page.Tree.GetComponent<XsrUiTextInput>(page.Input("draft"))!.ReadDraft());
        page.Session.Select("theme-b"); page.Render();
        AssertEqual("second theme unsaved", page.Tree.GetComponent<XsrUiTextInput>(page.Input("draft"))!.ReadDraft());
    }

    private static void ThemeFilePickersCreateScopedGrantsAndRevokeExactGrant()
    {
        using var page = new ThemeSettingsFixture([ThemeTestOption("file", ThemeOptionKind.File, "", resource: ThemeResourceKind.Image)]);
        string path = Path.Combine(page.Fixture.TemporaryDirectory, "literal {data name}.png"); File.WriteAllBytes(path, [1, 2, 3]);
        page.Settings.ConfigureThemePickers((_, _) => Task.FromResult<IReadOnlyList<string>>([path]), _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
        page.Render(); AssertEqual(0, page.Session.Grants.Count);
        AssertFalse(page.Tree.GetComponent<XsrUiElement>(page.Find("Theme.Grants"))!.IsVisible);
        Emit(page.Fixture.Intents, "ui.settings.theme.action", page.ActionForOption("file", ".Pick"));
        page.Pump(() => page.Session.Grants.Count == 1 && page.Find("Theme.Grant." + page.Session.Grants[0].GrantId.ToString("N") + ".Path", false).IsAssigned);
        var granted = page.Session.Grants.Single(); AssertEqual("theme-a", granted.SourceIdentity); AssertEqual(path, granted.CanonicalPath);
        AssertTrue(page.Tree.GetComponent<XsrUiElement>(page.Find("Theme.Grants"))!.IsVisible);
        AssertEqual(ThemeResourceKind.Image, granted.Kind); AssertEqual(ThemeGrantScope.File, granted.Scope);
        page.Pump(() => page.TextExists(path));
        var revoke = page.Find("Theme.Grant." + granted.GrantId.ToString("N") + ".Revoke");
        Emit(page.Fixture.Intents, "ui.settings.theme.action", revoke); page.Pump(() => page.Session.Grants.Count == 0);
        AssertEqual(granted.GrantId, page.Session.LastRevoked);
        AssertFalse(page.Tree.GetComponent<XsrUiElement>(page.Find("Theme.Grants"))!.IsVisible);
        Emit(page.Fixture.Intents, "ui.settings.theme.action", revoke); page.Render(); AssertEqual(1, page.Session.RevokeCalls);
    }

    private static void ThemePreviewRetirementCancelsPendingPickAndRestoresSelection()
    {
        using var page = new ThemeSettingsFixture([ThemeTestOption("file", ThemeOptionKind.File, "", resource: ThemeResourceKind.Image)]);
        page.Session.Select("theme-a", preview: true); page.Render();
        var picker = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken admitted = default;
        page.Settings.ConfigureThemePickers((_, token) => { admitted = token; return picker.Task; }, _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
        page.Render(); var action = page.ActionForOption("file", ".Pick");
        Emit(page.Fixture.Intents, "ui.settings.theme.action", action); page.Render();
        AssertTrue(admitted.CanBeCanceled);
        Emit(page.Fixture.Intents, "ui.settings.section", page.Find("SettingsNav.general")); page.Render();
        AssertTrue(admitted.IsCancellationRequested); AssertEqual(1, page.Session.EndPreviewCalls); AssertFalse(page.Session.Snapshot.IsPreview);
        picker.SetResult([Path.Combine(page.Fixture.TemporaryDirectory, "retired.png")]);
        page.Pump(() => admitted.IsCancellationRequested);
        AssertEqual(0, page.Session.Grants.Count);
        Emit(page.Fixture.Intents, "ui.settings.theme.action", action); page.Render(); AssertEqual(0, page.Session.Grants.Count);
    }

    private static void ThemeRangeAndVisibilityPreserveTypedEditors()
    {
        var enabled = ThemeTestOption("enabled", ThemeOptionKind.Boolean, "false");
        var range = ThemeTestOption("range", ThemeOptionKind.Range, "4") with { Minimum = 0, Maximum = 10, Step = 2, VisibleWhen = "option(enabled)" };
        var conditional = ThemeTestOption("conditional", ThemeOptionKind.Text, "saved") with { VisibleWhen = "option(range) == 4" };
        using var page = new ThemeSettingsFixture([enabled, range, conditional]);
        var rangeField = page.Input("range"); var editor = page.Tree.GetComponent<XsrUiTextInput>(rangeField)!;
        var increase = page.ActionForOption("range", ".Increase");
        Emit(page.Fixture.Intents, "ui.settings.theme.action", increase); page.Render();
        AssertEqual("4", editor.ReadDraft());
        Emit(page.Fixture.Intents, "ui.settings.theme.action", page.ActionForOption("enabled", ".Toggle")); page.Render();
        Emit(page.Fixture.Intents, "ui.settings.theme.action", increase); page.Render();
        AssertEqual("6", editor.ReadDraft()); AssertEqual(rangeField, page.Input("range"));
        page.Renderer.SetTextInputValue(rangeField, "-"); page.Renderer.Focus(rangeField); page.Render();
        AssertEqual("-", editor.ReadDraft());
        page.Session.Touch(); page.Render(); AssertEqual(rangeField, page.Input("range")); AssertEqual("-", editor.ReadDraft());
    }

    private static void ThemeOptionSaveAndResetUseCurrentSourceRevision()
    {
        using var page = new ThemeSettingsFixture([ThemeTestOption("draft", ThemeOptionKind.Text, "saved")]);
        var field = page.Input("draft"); page.Renderer.SetTextInputValue(field, "changed"); page.Renderer.Focus(field); page.Render();
        Emit(page.Fixture.Intents, "ui.settings.theme.action", page.Find("Theme.OptionsSave"));
        page.Pump(() => page.Session.SaveCalls == 1 && page.Session.Snapshot.Current.Options.Single().Values["draft"].Value == "changed");
        AssertEqual("theme-a", page.Session.LastSave!.SourceIdentity); AssertEqual(1L, page.Session.LastSave.ExpectedRevision);
        AssertEqual(ThemeOptionKind.Text, page.Session.LastSave.Values["draft"].Kind);
        AssertEqual(field, page.Input("draft"));
        Emit(page.Fixture.Intents, "ui.settings.theme.action", page.Find("Theme.OptionsReset"));
        page.Pump(() => page.Session.ResetCalls == 1 && page.Tree.GetComponent<XsrUiTextInput>(field)!.ReadDraft() == "saved");
        AssertEqual(field, page.Input("draft"));
    }

    private static void ThemeRangeSliderRoutesPointerAndKeyboardThroughOwnedDraft()
    {
        var range = ThemeTestOption("range", ThemeOptionKind.Range, "4") with { Minimum = 0, Maximum = 10, Step = 2 };
        using var page = new ThemeSettingsFixture([range]);
        var field = page.Input("range"); var slider = page.ActionForOption("range", ".Slider");
        var state = page.Tree.GetComponent<XsrUiRangeInput>(slider)!;
        var scene = page.Fixture.Shell.Render(new(1000, 700));
        var node = ShowSettingsFixtureControl(page.Fixture, page.Settings, ref scene, page.Tree.Name(slider), new(1000, 700));
        AssertEqual(XsrUiSemanticRole.Slider, node.Role); AssertTrue(node.Range is not null);
        var track = node.Range!.Value.Track(node.Rect);
        var pointer = new XsrUiPoint(track.X + track.Width * .8, node.Rect.Y + node.Rect.Height / 2);
        AssertTrue(page.Renderer.PointerPressed(pointer)); page.Render();
        AssertTrue(page.Renderer.PointerReleased(pointer)); page.Render();
        AssertEqual("8", page.Tree.GetComponent<XsrUiTextInput>(field)!.ReadDraft());
        page.Pump(() => page.Session.SaveCalls == 1
            && page.Session.Snapshot.Current.Options.Single().Values["range"].Value == "8");
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Range, "8"), page.Session.LastSave!.Values["range"]);
        AssertEqual(1L, page.Session.LastSave.ExpectedRevision);
        AssertTrue(page.Renderer.HandleKey(XsrUiKey.Home)); page.Render();
        AssertEqual("0", page.Tree.GetComponent<XsrUiTextInput>(field)!.ReadDraft());
        AssertTrue(page.Renderer.HandleKey(XsrUiKey.End)); page.Render();
        AssertEqual("10", page.Tree.GetComponent<XsrUiTextInput>(field)!.ReadDraft());
        AssertTrue(page.Renderer.HandleKey(XsrUiKey.Left)); page.Render();
        AssertEqual("8", page.Tree.GetComponent<XsrUiTextInput>(field)!.ReadDraft());
        AssertEqual(slider, page.ActionForOption("range", ".Slider"));
        AssertTrue(ReferenceEquals(state, page.Tree.GetComponent<XsrUiRangeInput>(slider)));
        Emit(page.Fixture.Intents, "ui.settings.theme.action", page.Find("Theme.OptionsSave"));
        page.Pump(() => page.Session.SaveCalls == 2);
        AssertEqual("8", page.Session.LastSave!.Values["range"].Value);
        AssertEqual(2L, page.Session.LastSave.ExpectedRevision);
        AssertEqual("8", page.Session.Snapshot.Current.Options.Single().Values["range"].Value);
    }

    private static ThemeOptionDefinition ThemeTestOption(string id, ThemeOptionKind kind, string value, ThemeResourceKind? resource = null) =>
        new(id, kind, new(kind, value), new Dictionary<string, string> { ["en"] = id }, new Dictionary<string, string> { ["en"] = "literal <Text/> {data label}" },
            null, null, null, null, null, [], resource);

    private sealed class ThemeSettingsFixture : IDisposable
    {
        internal LaunchPageFixture Fixture { get; } = new(new ImmediateInstanceSource([]));
        internal SettingsPageController Settings { get; }
        internal FakeThemeSettingsSession Session { get; }
        internal XsrUiTree Tree => Fixture.Shell.Tree;
        internal XsrUiRenderer Renderer => Fixture.Shell.Renderer;
        internal ThemeSettingsFixture(IReadOnlyList<ThemeOptionDefinition> options, bool twoThemes = false)
        {
            Session = new(options, twoThemes);
            Settings = new(Fixture.Shell, Fixture.Intents, Fixture.Foundation.Queries, Fixture.Foundation.Commands, Fixture.Store, Fixture.Feedback)
            { ThemeSession = Session };
            Settings.WaitForThemeOptionReload = token => Task.Delay(Timeout.InfiniteTimeSpan, token);
            Renderer.ReducedMotion = true; Fixture.Shell.Stage.Navigation.Replace(Settings.Page); Render();
            Pump(() => Find("SettingsNav.appearance", false).IsAssigned);
            Emit(Fixture.Intents, "ui.settings.section", Find("SettingsNav.appearance"));
            Pump(() => Find("Theme.OptionsSave", false).IsAssigned && Session.RefreshCalls == 1); Render();
        }
        internal void Render() => Fixture.Shell.Render(new(1000, 700));
        internal void Pump(Func<bool> condition)
        {
            bool ready = SpinWait.SpinUntil(() => { Render(); return condition(); }, TimeSpan.FromSeconds(5));
            AssertTrue(ready);
            Render();
        }
        internal XsrUiEntityId Find(string key, bool required = true)
        {
            XsrUiEntityId result = default;
            Tree.Walk(Settings.Page, entity => { if (Tree.Name(entity) == key) result = entity; return true; });
            if (required) AssertTrue(result.IsAssigned); return result;
        }
        internal XsrUiEntityId Input(string label)
        {
            XsrUiEntityId result = default;
            Tree.Walk(Settings.Page, entity => { if (Tree.GetComponent<XsrUiTextInput>(entity) is not null && Tree.GetComponent<XsrUiSemantic>(entity)?.Label == label) result = entity; return true; });
            AssertTrue(result.IsAssigned); return result;
        }
        internal XsrUiEntityId ActionForOption(string label, string suffix)
        {
            XsrUiEntityId option = default;
            Tree.Walk(Settings.Page, entity =>
            {
                if (Tree.Name(entity).StartsWith("Theme.Option.", StringComparison.Ordinal))
                {
                    bool hasLabel = false;
                    Tree.Walk(entity, child => { hasLabel |= Tree.GetComponent<XsrUiText>(child)?.Content == label; return true; });
                    if (hasLabel) option = entity;
                }
                return true;
            });
            AssertTrue(option.IsAssigned); XsrUiEntityId action = default;
            Tree.Walk(option, entity => { if (Tree.Name(entity).EndsWith(suffix, StringComparison.Ordinal)) action = entity; return true; });
            AssertTrue(action.IsAssigned); return action;
        }
        internal bool TextExists(string value)
        {
            bool found = false;
            Tree.Walk(Settings.Page, entity => { found |= Tree.GetComponent<XsrUiText>(entity)?.Content == value; return true; }); return found;
        }
        public void Dispose() { Settings.Dispose(); Fixture.Dispose(); }
    }

    private sealed class FakeThemeSettingsSession : IDesktopThemeSettingsSession
    {
        private readonly IReadOnlyList<ThemeOptionDefinition> _options;
        public DesktopThemeSettingsSnapshot Snapshot { get; private set; }
        internal int RefreshCalls, SaveCalls, ResetCalls, RevokeCalls, EndPreviewCalls;
        internal bool FailSave;
        internal ThemeCandidate? ImportCandidate;
        internal bool? DowngradeDecision;
        internal Func<CancellationToken, Task>? BeforeSave;
        internal Func<CancellationToken, Task>? AfterSave;
        internal ThemeOptionsSaveCommand? LastSave;
        internal Guid? LastRevoked;
        internal List<ThemeResourceGrant> Grants { get; } = [];
        internal FakeThemeSettingsSession(IReadOnlyList<ThemeOptionDefinition> options, bool twoThemes)
        {
            _options = options;
            var manifest = new ThemeManifest("NTP/1", "fixture", "1.0.0", new Dictionary<string, string> { ["en"] = "literal <Theme /> {data name}" },
                1, "NSS/1", [], [], [], options, new(new Dictionary<string, ThemeAppearanceResource>()));
            List<ThemeCatalogueEntry> entries = [new("theme-a", "a1", ThemeSourceKind.Package, manifest, null, true)];
            if (twoThemes) entries.Add(new("theme-b", "b1", ThemeSourceKind.Package, manifest with { Id = "fixture-b" }, null, true));
            var states = entries.Select(entry => new ThemeOptionState(entry.SourceIdentity, 1, 4,
                options.ToDictionary(option => option.Id, option => option.Default), new Dictionary<string, string>())).ToArray();
            var selection = new ThemeSelection("theme-a", "a1", "fixture", "1.0.0", 1);
            Snapshot = new(1, new(1, entries), new(1, selection, selection, states, [], false), "theme-a", false, false, null);
        }
        internal void Touch(string? error = null) => Snapshot = Snapshot with { Revision = Snapshot.Revision + 1, Error = error };
        internal void Select(string source, bool preview = false) { Snapshot = Snapshot with { PresentedSourceIdentity = source, IsPreview = preview }; Touch(); }
        public Task<XsrResult> RefreshAsync(CancellationToken token) { RefreshCalls++; Touch(); return Task.FromResult(XsrResult.Success()); }
        public async Task<XsrResult> ImportAsync(string path, ThemeSourceKind kind, bool preview, CancellationToken token,
            Func<ThemeCandidate, CancellationToken, Task<bool>>? confirmDowngrade = null)
        {
            if (ImportCandidate is { } candidate && confirmDowngrade is not null)
                DowngradeDecision = await confirmDowngrade(candidate, token);
            return XsrResult.Success();
        }
        public Task<XsrResult> ActivateAsync(string sourceIdentity, bool preview, CancellationToken token) { Select(sourceIdentity, preview); return Task.FromResult(XsrResult.Success()); }
        public Task<XsrResult> EndPreviewAsync(CancellationToken token) { EndPreviewCalls++; Snapshot = Snapshot with { IsPreview = false, PresentedSourceIdentity = Snapshot.Current.Selection?.SourceIdentity }; Touch(); return Task.FromResult(XsrResult.Success()); }
        public Task<XsrResult> SaveOptionsAsync(ThemeOptionsSaveCommand command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); SaveCalls++; LastSave = command;
            if (FailSave) return Task.FromResult(XsrResult.Failure(new(XsrErrorKind.Rejected,
                XsrSemanticId.Parse("themes.fixture_rejected"), "fixture theme preparation rejected")));
            return CompleteSaveAsync();
            async Task<XsrResult> CompleteSaveAsync()
            {
                if (BeforeSave is { } wait) await wait(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                ReplaceOptions(command.SourceIdentity, command.Values);
                if (AfterSave is { } after) await after(token).ConfigureAwait(false);
                return XsrResult.Success();
            }
        }
        public Task<XsrResult> ResetOptionsAsync(ThemeOptionsResetCommand command, CancellationToken token)
        { ResetCalls++; ReplaceOptions(command.SourceIdentity, _options.ToDictionary(option => option.Id, option => option.Default)); return Task.FromResult(XsrResult.Success()); }
        private void ReplaceOptions(string source, IReadOnlyDictionary<string, ThemeOptionValue> values)
        {
            var states = Snapshot.Current.Options.Select(state =>
            {
                if (state.SourceIdentity != source) return state;
                var merged = state.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                foreach (var pair in values) merged[pair.Key] = pair.Value;
                return state with { Revision = state.Revision + 1, Values = merged };
            }).ToArray();
            Snapshot = Snapshot with { Current = Snapshot.Current with { Revision = Snapshot.Current.Revision + 1, Options = states } }; Touch();
        }
        public Task<XsrResult<ThemeOptionsExport>> ExportOptionsAsync(ThemeOptionsExportQuery query, CancellationToken token) => Task.FromResult(XsrResult.Success(new ThemeOptionsExport("{}")));
        public Task<XsrResult> ImportOptionsAsync(ThemeOptionsImportCommand command, CancellationToken token) => Task.FromResult(XsrResult.Success());
        public Task<XsrResult> GrantAsync(ThemeResourceGrantCommand command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Grants.Add(new(Guid.NewGuid(), command.SourceIdentity, command.Kind, command.Scope, command.SelectedPath, 1));
            Snapshot = Snapshot with { Current = Snapshot.Current with { Grants = Grants.ToArray() } }; Touch(); return Task.FromResult(XsrResult.Success());
        }
        public Task<XsrResult> RevokeAsync(ThemeResourceRevokeCommand command, CancellationToken token)
        {
            RevokeCalls++; LastRevoked = command.GrantId; Grants.RemoveAll(item => item.GrantId == command.GrantId);
            Snapshot = Snapshot with { Current = Snapshot.Current with { Grants = Grants.ToArray() } }; Touch(); return Task.FromResult(XsrResult.Success());
        }
        public Task<XsrResult> RemoveAsync(ThemeRemoveCommand command, CancellationToken token) => Task.FromResult(XsrResult.Success());
        public Task<XsrResult> WatchAsync(ThemeWatchCommand command, CancellationToken token) => Task.FromResult(XsrResult.Success());
        public Task<XsrResult> OpenFolderAsync(string sourceIdentity, CancellationToken token) => Task.FromResult(XsrResult.Success());
    }
}
