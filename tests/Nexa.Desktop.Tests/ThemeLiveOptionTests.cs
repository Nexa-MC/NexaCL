using System.Text.Json.Nodes;
using Nexa.Desktop.Ui;
using Nexa.Services.Themes;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ThemeOptionLiveReloadRecomputesActualStyleAndPersistsPreviewValues()
    {
        using var fixture = new ThemeLifecycleFixture();
        string path = fixture.MakeTheme("live-options", "Text#LiveThemeSample{FontSize:option(size);}");
        string manifest = Path.Combine(path, "theme.json");
        var document = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
        document["options"] = JsonNode.Parse("""
            [{"id":"size","kind":"Number","default":20,"min":12,"max":40,"step":1,"label":{"en":"size"}}]
            """);
        File.WriteAllText(manifest, document.ToJsonString());
        // Persisted option history belongs to an installed source. A never-installed
        // import preview deliberately retires all provisional state when canceled.
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(path, ThemeSourceKind.Directory, false, CancellationToken.None)).IsSuccess);
        string source = fixture.Session.Snapshot.PresentedSourceIdentity!;
        AssertTrue(fixture.Session.Snapshot.Catalogue.Themes.Single(theme => theme.SourceIdentity == source).IsInstalled);
        AssertTrue(fixture.Run(fixture.Session.ActivateAsync("builtin", false, CancellationToken.None)).IsSuccess);
        var f = fixture.Fixture;
        var sample = f.Shell.Tree.Create("LiveThemeSample");
        f.Shell.Tree.SetComponent(sample, new XsrUiElement { Width = 200, Height = 60 });
        f.Shell.Tree.SetComponent(sample, new XsrUiText("Theme sample"));
        f.Shell.Tree.SetComponent(sample, new XsrUiStyleIdentity("Text", "LiveThemeSample", []));
        // The page fills ContentHost. Use a non-modal stage overlay so the actual
        // painted probe remains inside the viewport without blocking the editor.
        f.Shell.Stage.Show(sample);
        using var settings = new SettingsPageController(f.Shell, f.Intents, f.Foundation.Queries, f.Foundation.Commands, f.Store, f.Feedback)
        { ThemeSession = fixture.Session, WaitForThemeOptionReload = _ => Task.CompletedTask };
        f.Shell.Stage.Navigation.Replace(settings.Page);
        fixture.Pump(() => Find("SettingsNav.appearance").IsAssigned);
        Emit(f.Intents, "ui.settings.section", Find("SettingsNav.appearance"));
        fixture.Pump(() => Find("Theme.OptionsSave").IsAssigned && !fixture.Session.Snapshot.Busy);
        // Section changes retire previews, so start the actual preview only after
        // entering appearance, just as the host's preview action does.
        AssertTrue(fixture.Run(fixture.Session.ActivateAsync(source, true, CancellationToken.None)).IsSuccess);
        AssertEqual("size", fixture.Session.Snapshot.Catalogue.Themes.Single(theme => theme.SourceIdentity == source).Manifest.Options.Single().Id);
        XsrUiEntityId field = default;
        f.Shell.Tree.Walk(settings.Page, entity =>
        {
            if (f.Shell.Tree.GetComponent<XsrUiTextInput>(entity) is not null && f.Shell.Tree.GetComponent<XsrUiSemantic>(entity)?.Label == "size") field = entity;
            return true;
        });
        AssertTrue(field.IsAssigned);
        AssertEqual(20d, f.Shell.Render(new(1000, 700)).Nodes.Single(node => node.Entity == sample).VisualStyle.FontSize);
        long generation = fixture.Appearance.Current!.Generation;
        f.Shell.Renderer.SetTextInputValue(field, "28"); f.Shell.Renderer.Focus(field);
        fixture.Pump(() => fixture.Current().Options.Any(state => state.SourceIdentity == source && state.Values["size"].Value == "28")
            && fixture.Appearance.Current!.Generation > generation && !fixture.Session.Snapshot.Busy);
        AssertEqual(28d, f.Shell.Render(new(1000, 700)).Nodes.Single(node => node.Entity == sample).VisualStyle.FontSize);
        AssertEqual(fixture.Appearance.Current!.Generation, f.Shell.Renderer.ThemeActivationGeneration);
        AssertTrue(fixture.Session.Snapshot.IsPreview); AssertEqual("builtin", fixture.Current().Selection!.SourceIdentity);
        AssertEqual(field, f.Shell.Renderer.Focused);
        AssertTrue(fixture.Run(fixture.Session.EndPreviewAsync(CancellationToken.None)).IsSuccess);
        AssertEqual("builtin", fixture.Session.Snapshot.PresentedSourceIdentity);
        AssertEqual("28", fixture.Current().Options.Single(state => state.SourceIdentity == source).Values["size"].Value);
        AssertTrue(f.Shell.Stage.Dismiss(sample));
        f.Shell.Tree.Destroy(sample);

        XsrUiEntityId Find(string key)
        {
            XsrUiEntityId found = default;
            f.Shell.Tree.Walk(settings.Page, entity => { if (f.Shell.Tree.Name(entity) == key) found = entity; return true; });
            return found;
        }
    }

    private static void ThemeOptionEditsReloadWithoutSaveAndRetainInputOwnership()
    {
        var number = ThemeTestOption("number", ThemeOptionKind.Number, "4") with { Minimum = 0, Maximum = 10, Step = 2 };
        using var page = new ThemeSettingsFixture([ThemeTestOption("draft", ThemeOptionKind.Text, "saved"), number]);
        page.Settings.WaitForThemeOptionReload = _ => Task.CompletedTask;
        var field = page.Input("draft");
        var input = page.Tree.GetComponent<XsrUiTextInput>(field)!;
        page.Renderer.SetTextInputValue(field, "applied automatically"); page.Renderer.Focus(field);
        page.Renderer.EditText(XsrUiTextEdit.Left);
        int start = input.SelectionStart, end = input.SelectionEnd;
        page.Pump(() => page.Session.SaveCalls == 1);
        AssertEqual("applied automatically", page.Session.Snapshot.Current.Options.Single().Values["draft"].Value);
        AssertEqual(field, page.Input("draft")); AssertEqual(field, page.Renderer.Focused);
        AssertTrue(ReferenceEquals(input, page.Tree.GetComponent<XsrUiTextInput>(field)));
        AssertEqual(start, input.SelectionStart); AssertEqual(end, input.SelectionEnd);
        // An unfinished numeric draft does not submit, and does not block a complete text edit.
        var numeric = page.Input("number");
        page.Renderer.SetTextInputValue(numeric, "-"); page.Renderer.Focus(numeric); page.Render();
        AssertEqual(1, page.Session.SaveCalls);
        page.Renderer.SetTextInputValue(field, "second value"); page.Renderer.Focus(field);
        page.Pump(() => page.Session.SaveCalls == 2);
        AssertFalse(page.Session.LastSave!.Values.ContainsKey("number"));
        AssertEqual("-", page.Tree.GetComponent<XsrUiTextInput>(numeric)!.ReadDraft());
        AssertEqual("4", page.Session.Snapshot.Current.Options.Single().Values["number"].Value);
        page.Renderer.SetTextInputValue(numeric, "6"); page.Renderer.Focus(numeric);
        page.Pump(() => page.Session.SaveCalls == 3);
        AssertEqual("6", page.Session.Snapshot.Current.Options.Single().Values["number"].Value);
        page.Render(); page.Render(); AssertEqual(3, page.Session.SaveCalls);
    }

    private static void ThemeOptionReloadCoalescesChangesAndRejectsRetiredScope()
    {
        using var page = new ThemeSettingsFixture([ThemeTestOption("draft", ThemeOptionKind.Text, "saved")], twoThemes: true);
        List<(TaskCompletionSource Ready, CancellationToken Token)> waits = [];
        page.Settings.WaitForThemeOptionReload = token =>
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waits.Add((ready, token)); return ready.Task;
        };
        var field = page.Input("draft"); page.Renderer.Focus(field);
        page.Renderer.SetTextInputValue(field, "first"); page.Render();
        page.Renderer.SetTextInputValue(field, "latest"); page.Render();
        AssertEqual(2, waits.Count); AssertTrue(waits[0].Token.IsCancellationRequested);
        waits[0].Ready.SetResult(); page.Render(); AssertEqual(0, page.Session.SaveCalls);
        waits[1].Ready.SetResult(); page.Pump(() => page.Session.SaveCalls == 1);
        AssertEqual("latest", page.Session.LastSave!.Values["draft"].Value);
        page.Renderer.SetTextInputValue(field, "do not apply to another theme"); page.Render();
        AssertEqual(3, waits.Count);
        page.Session.Select("theme-b"); page.Render();
        AssertTrue(waits[2].Token.IsCancellationRequested);
        waits[2].Ready.SetResult(); page.Render(); AssertEqual(1, page.Session.SaveCalls);
        AssertEqual("saved", page.Session.Snapshot.Current.Options.Single(state => state.SourceIdentity == "theme-b").Values["draft"].Value);
    }

    private static void ThemeOptionFailedReloadKeepsDraftAndRetriesOnlyAfterEdit()
    {
        var number = ThemeTestOption("number", ThemeOptionKind.Number, "4") with { Minimum = 0, Maximum = 10, Step = 2 };
        using var page = new ThemeSettingsFixture([number]);
        page.Settings.WaitForThemeOptionReload = _ => Task.CompletedTask;
        page.Session.FailSave = true;
        var field = page.Input("number"); page.Renderer.Focus(field); page.Renderer.SetTextInputValue(field, "6");
        page.Pump(() => page.Session.SaveCalls == 1 && page.TextExists("fixture theme preparation rejected"));
        AssertEqual("6", page.Tree.GetComponent<XsrUiTextInput>(field)!.ReadDraft());
        AssertEqual("4", page.Session.Snapshot.Current.Options.Single().Values["number"].Value);
        page.Render(); page.Render(); page.Render(); AssertEqual(1, page.Session.SaveCalls);
        page.Session.FailSave = false;
        page.Renderer.SetTextInputValue(field, "-"); page.Render();
        page.Renderer.SetTextInputValue(field, "6");
        page.Pump(() => page.Session.SaveCalls == 2);
        AssertEqual("6", page.Session.Snapshot.Current.Options.Single().Values["number"].Value);
        page.Render(); AssertEqual(2, page.Session.SaveCalls);
    }

    private static void ThemeOptionLiveReloadWaitsForSliderReleaseAndKeepsDragging()
    {
        var range = ThemeTestOption("range", ThemeOptionKind.Range, "4") with { Minimum = 0, Maximum = 10, Step = 2 };
        using var page = new ThemeSettingsFixture([range]);
        page.Settings.WaitForThemeOptionReload = _ => Task.CompletedTask;
        var slider = page.ActionForOption("range", ".Slider");
        var scene = page.Fixture.Shell.Render(new(1000, 700));
        var node = ShowSettingsFixtureControl(page.Fixture, page.Settings, ref scene, page.Tree.Name(slider), new(1000, 700));
        var track = node.Range!.Value.Track(node.Rect);
        var center = node.Rect.Y + node.Rect.Height / 2;
        AssertTrue(page.Renderer.PointerPressed(new(track.X + track.Width * .6, center)));
        page.Render(); page.Render();
        AssertTrue(page.Tree.GetComponent<XsrUiInput>(slider)!.IsPressed);
        AssertTrue(page.Tree.GetComponent<XsrUiInput>(slider)!.Enabled);
        AssertEqual(0, page.Session.SaveCalls);
        AssertTrue(page.Renderer.PointerMoved(new(track.X + track.Width * .8, center)));
        page.Render(); page.Render();
        AssertEqual("8", page.Tree.GetComponent<XsrUiTextInput>(page.Input("range"))!.ReadDraft());
        AssertTrue(page.Tree.GetComponent<XsrUiInput>(slider)!.IsPressed);
        AssertEqual(0, page.Session.SaveCalls);
        var end = new XsrUiPoint(track.X + track.Width, center);
        AssertTrue(page.Renderer.PointerMoved(end)); page.Render();
        AssertTrue(page.Renderer.PointerReleased(end));
        page.Pump(() => page.Session.SaveCalls == 1);
        AssertEqual("10", page.Session.LastSave!.Values["range"].Value);
        AssertEqual(slider, page.ActionForOption("range", ".Slider"));
        AssertFalse(page.Tree.GetComponent<XsrUiInput>(slider)!.IsPressed);
        page.Render(); AssertEqual(1, page.Session.SaveCalls);
    }

    private static void ThemeOptionPendingReloadKeepsTypingCompositionAndNewerDrafts()
    {
        using var page = new ThemeSettingsFixture([ThemeTestOption("draft", ThemeOptionKind.Text, "saved"),
            ThemeTestOption("enabled", ThemeOptionKind.Boolean, "false")]);
        page.Settings.WaitForThemeOptionReload = _ => Task.CompletedTask;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Session.BeforeSave = token => gate.Task.WaitAsync(token);
        var field = page.Input("draft");
        var input = page.Tree.GetComponent<XsrUiTextInput>(field)!;
        var scene = page.Fixture.Shell.Render(new(1000, 700));
        ShowSettingsFixtureControl(page.Fixture, page.Settings, ref scene, page.Tree.Name(field), new(1000, 700));
        page.Renderer.SetTextInputValue(field, "first"); page.Renderer.Focus(field);
        page.Pump(() => page.Session.SaveCalls == 1);
        page.Render(); page.Render();
        AssertTrue(page.Tree.GetComponent<XsrUiInput>(field)!.Enabled); AssertEqual(field, page.Renderer.Focused);
        AssertTrue(page.Renderer.EditText(XsrUiTextEdit.End));
        AssertTrue(page.Renderer.InsertText("追加"));
        AssertTrue(page.Renderer.SetTextPreedit(field, "拼"));
        Emit(page.Fixture.Intents, "ui.settings.theme.action", page.ActionForOption("enabled", ".Toggle"));
        Emit(page.Fixture.Intents, "ui.settings.theme.action", page.Find("Theme.OptionsReset"));
        page.Render();
        AssertEqual("first追加", input.ReadDraft()); AssertEqual(field, page.Renderer.Focused);
        AssertTrue(page.Tree.GetComponent<XsrUiToggle>(page.ActionForOption("enabled", ".Toggle"))!.IsChecked);
        AssertEqual(1, page.Session.SaveCalls); AssertEqual(0, page.Session.ResetCalls);
        AssertEqual("拼", page.Fixture.Shell.Render(new(1000, 700)).Nodes.Single(node => node.Entity == field).TextInput!.Value.Preedit);
        page.Session.BeforeSave = null; gate.SetResult();
        page.Pump(() => page.Session.SaveCalls == 2 && page.Session.Snapshot.Current.Options.Single().Values["draft"].Value == "first追加");
        page.Render();
        AssertEqual("true", page.Session.Snapshot.Current.Options.Single().Values["enabled"].Value);
        AssertEqual(field, page.Renderer.Focused); AssertEqual(field, page.Input("draft"));
        AssertTrue(ReferenceEquals(input, page.Tree.GetComponent<XsrUiTextInput>(field)));
        AssertEqual("拼", page.Fixture.Shell.Render(new(1000, 700)).Nodes.Single(node => node.Entity == field).TextInput!.Value.Preedit);
        AssertTrue(page.Renderer.InsertText("音"));
        page.Pump(() => page.Session.SaveCalls == 3);
        AssertEqual("first追加音", page.Session.LastSave!.Values["draft"].Value);
    }

    private static void ThemeOptionPublishedSnapshotCannotOverwriteReturnToPreviousValue()
    {
        var number = ThemeTestOption("number", ThemeOptionKind.Number, "4") with { Minimum = 0, Maximum = 10, Step = 2 };
        using var page = new ThemeSettingsFixture([number]);
        page.Settings.WaitForThemeOptionReload = _ => Task.CompletedTask;
        var publish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Session.BeforeSave = token => publish.Task.WaitAsync(token);
        page.Session.AfterSave = token => { published.TrySetResult(); return complete.Task.WaitAsync(token); };
        var field = page.Input("number");
        var input = page.Tree.GetComponent<XsrUiTextInput>(field)!;
        page.Renderer.SetTextInputValue(field, "6"); page.Renderer.Focus(field);
        page.Pump(() => page.Session.SaveCalls == 1);
        AssertEqual("6", page.Session.LastSave!.Values["number"].Value);
        AssertTrue(page.Renderer.EditText(XsrUiTextEdit.SelectAll));
        AssertTrue(page.Renderer.InsertText("4"));
        page.Render(); page.Render();
        AssertEqual("4", input.ReadDraft()); AssertEqual(1, page.Session.SaveCalls);
        publish.SetResult();
        page.Pump(() => published.Task.IsCompleted);
        AssertEqual("6", page.Session.Snapshot.Current.Options.Single().Values["number"].Value);
        page.Render(); page.Render();
        AssertEqual("4", input.ReadDraft()); AssertEqual(field, page.Renderer.Focused);
        AssertEqual(1, page.Session.SaveCalls);
        page.Session.BeforeSave = null; page.Session.AfterSave = null; complete.SetResult();
        page.Pump(() => page.Session.SaveCalls == 2 && page.Session.Snapshot.Current.Options.Single().Values["number"].Value == "4");
        AssertEqual("4", page.Session.LastSave!.Values["number"].Value);
        AssertEqual(2L, page.Session.LastSave.ExpectedRevision);
        AssertEqual(field, page.Input("number")); AssertEqual(field, page.Renderer.Focused);
        AssertTrue(ReferenceEquals(input, page.Tree.GetComponent<XsrUiTextInput>(field)));
        page.Render(); AssertEqual(2, page.Session.SaveCalls);
    }
}
