using Nexa.Services.Themes;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ThemeBooleanEditorsUseAccessibleRetainedSwitchAndCheckbox()
    {
        using var page = new ThemeSettingsFixture([
            ThemeTestOption("switch", ThemeOptionKind.Boolean, "false"),
            ThemeTestOption("check", ThemeOptionKind.Boolean, "true") with { Control = ThemeOptionControl.CheckBox },
            ThemeTestOption("segmented", ThemeOptionKind.Boolean, "false") with { Control = ThemeOptionControl.Segmented },
        ]);
        var toggle = page.ActionForOption("switch", ".Toggle");
        var checkbox = page.ActionForOption("check", ".Toggle");
        AssertEqual(XsrUiSemanticRole.Switch, page.Tree.GetComponent<XsrUiSemantic>(toggle)!.Role);
        AssertEqual(XsrUiSemanticRole.CheckBox, page.Tree.GetComponent<XsrUiSemantic>(checkbox)!.Role);
        AssertFalse(page.Tree.GetComponent<XsrUiToggle>(toggle)!.IsChecked);
        AssertTrue(page.Tree.GetComponent<XsrUiToggle>(checkbox)!.IsChecked);
        var scene = page.Fixture.Shell.Render(new(1000, 700));
        _ = ShowSettingsFixtureControl(page.Fixture, page.Settings, ref scene, page.Tree.Name(toggle), new(1000, 700));
        AssertTrue(page.Renderer.Focus(toggle, showIndicator: true));
        AssertTrue(page.Renderer.HandleKey(XsrUiKey.Space)); page.Render();
        AssertTrue(page.Tree.GetComponent<XsrUiToggle>(toggle)!.IsChecked);
        AssertEqual(toggle, page.ActionForOption("switch", ".Toggle"));
        Emit(page.Fixture.Intents, "ui.settings.theme.action", checkbox); page.Render();
        AssertFalse(page.Tree.GetComponent<XsrUiToggle>(checkbox)!.IsChecked);
        var on = page.ActionForOption("segmented", ".True"); var off = page.ActionForOption("segmented", ".False");
        var track = page.Tree.GetComponent<XsrUiSegmentedTrack>(page.Tree.Parent(on))!;
        AssertEqual(off, track.Selected);
        Emit(page.Fixture.Intents, "ui.settings.theme.action", on); page.Render();
        AssertEqual(on, track.Selected);
        Emit(page.Fixture.Intents, "ui.settings.theme.action", page.Find("Theme.OptionsSave"));
        page.Pump(() => page.Session.SaveCalls == 1);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Boolean, "true"), page.Session.LastSave!.Values["switch"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Boolean, "false"), page.Session.LastSave.Values["check"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Boolean, "true"), page.Session.LastSave.Values["segmented"]);
    }

    private static void ThemeChoiceAndNumberSlidersPersistDeclaredValuesRatherThanIndices()
    {
        ThemeOptionChoice[] choices = [
            new("compact", new Dictionary<string, string> { ["en"] = "Compact" }),
            new("balanced", new Dictionary<string, string> { ["en"] = "Balanced" }),
            new("spacious", new Dictionary<string, string> { ["en"] = "Spacious" }),
        ];
        using var page = new ThemeSettingsFixture([
            ThemeTestOption("choice", ThemeOptionKind.Select, "balanced") with { Control = ThemeOptionControl.Slider, Choices = choices },
            ThemeTestOption("numeric", ThemeOptionKind.Number, "4") with { Control = ThemeOptionControl.Slider, Minimum = 0, Maximum = 10, Step = 2 },
            ThemeTestOption("input", ThemeOptionKind.Range, "4") with { Control = ThemeOptionControl.Input, Minimum = 0, Maximum = 10, Step = 2 },
        ]);
        var choiceSlider = page.ActionForOption("choice", ".Slider");
        var numericSlider = page.ActionForOption("numeric", ".Slider");
        var range = page.Tree.GetComponent<XsrUiRangeInput>(choiceSlider)!;
        AssertEqual(0d, range.Minimum); AssertEqual(2d, range.Maximum); AssertEqual(1d, range.Step); AssertEqual(1d, range.Value);
        var scene = page.Fixture.Shell.Render(new(1000, 700));
        _ = ShowSettingsFixtureControl(page.Fixture, page.Settings, ref scene, page.Tree.Name(choiceSlider), new(1000, 700));
        AssertTrue(page.Renderer.Focus(choiceSlider));
        AssertTrue(page.Renderer.HandleKey(XsrUiKey.End)); page.Render();
        AssertTrue(page.TextExists("Spacious"));
        AssertEqual(choiceSlider, page.ActionForOption("choice", ".Slider"));
        AssertTrue(ReferenceEquals(range, page.Tree.GetComponent<XsrUiRangeInput>(choiceSlider)));
        AssertTrue(page.Renderer.HandleKey(XsrUiKey.Left)); page.Render();
        AssertTrue(page.TextExists("Balanced"));
        _ = ShowSettingsFixtureControl(page.Fixture, page.Settings, ref scene, page.Tree.Name(numericSlider), new(1000, 700));
        AssertTrue(page.Renderer.Focus(numericSlider));
        AssertTrue(page.Renderer.HandleKey(XsrUiKey.Right)); page.Render();
        AssertEqual("6", page.Tree.GetComponent<XsrUiTextInput>(page.Input("numeric"))!.ReadDraft());
        AssertTrue(page.Input("input").IsAssigned);
        Emit(page.Fixture.Intents, "ui.settings.theme.action", page.Find("Theme.OptionsSave"));
        page.Pump(() => page.Session.SaveCalls == 1);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Select, "balanced"), page.Session.LastSave!.Values["choice"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Number, "6"), page.Session.LastSave.Values["numeric"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Range, "4"), page.Session.LastSave.Values["input"]);
    }

    private static void ThemeSegmentedChoicesScrollWithinViewportAndRetainSelection()
    {
        var choices = Enumerable.Range(0, 12).Select(index => new ThemeOptionChoice("value-" + index,
            new Dictionary<string, string> { ["en"] = "Long localized choice label " + index })).ToArray();
        using var page = new ThemeSettingsFixture([
            ThemeTestOption("choices", ThemeOptionKind.Select, "value-0") with { Choices = choices },
        ]);
        var first = page.ActionForOption("choices", ".Choice." + ThemePreviewIdentity("value-0"));
        var last = page.ActionForOption("choices", ".Choice." + ThemePreviewIdentity("value-11"));
        var slot = page.Tree.Parent(first); var track = page.Tree.GetComponent<XsrUiSegmentedTrack>(slot)!;
        AssertEqual(first, track.Selected);
        foreach (double width in new[] { 760d, 1040d })
        {
            var scene = page.Fixture.Shell.Render(new(width, 2400));
            var node = scene.Nodes.Single(item => item.Entity == slot);
            AssertTrue(node.Rect.Width > 0 && node.Rect.X >= 0 && node.Rect.X + node.Rect.Width <= width);
            AssertTrue(node.Scroll is { } scroll && scroll.ContentWidth > scroll.ViewportWidth);
        }
        Emit(page.Fixture.Intents, "ui.settings.theme.action", last); page.Render();
        AssertEqual(last, track.Selected);
        page.Session.Touch(); page.Render();
        AssertEqual(last, track.Selected);
        AssertEqual(last, page.ActionForOption("choices", ".Choice." + ThemePreviewIdentity("value-11")));
        Emit(page.Fixture.Intents, "ui.settings.theme.action", page.Find("Theme.OptionsSave"));
        page.Pump(() => page.Session.SaveCalls == 1);
        AssertEqual("value-11", page.Session.LastSave!.Values["choices"].Value);
    }

    private static void ThemeSlidersEndAtTheLastAdmittedStep()
    {
        ThemeOptionChoice[] choices = [
            new("first", new Dictionary<string, string> { ["en"] = "First" }),
            new("middle", new Dictionary<string, string> { ["en"] = "Middle" }),
            new("last", new Dictionary<string, string> { ["en"] = "Last" }),
        ];
        var rangeDefinition = ThemeTestOption("range", ThemeOptionKind.Range, "3") with { Minimum = 0, Maximum = 10, Step = 3 };
        var numberDefinition = ThemeTestOption("number", ThemeOptionKind.Number, "3") with { Control = ThemeOptionControl.Slider, Minimum = 0, Maximum = 10, Step = 3 };
        using var page = new ThemeSettingsFixture([
            rangeDefinition, numberDefinition,
            ThemeTestOption("automatic-number", ThemeOptionKind.Number, "9") with { Minimum = 0, Maximum = 10, Step = 3 },
            ThemeTestOption("input-number", ThemeOptionKind.Number, "9") with { Control = ThemeOptionControl.Input, Minimum = 0, Maximum = 10, Step = 3 },
            ThemeTestOption("choice", ThemeOptionKind.Select, "first") with { Control = ThemeOptionControl.Slider, Choices = choices },
        ]);
        int releasedSliders = 0;
        foreach (string label in new[] { "range", "number" })
        {
            var slider = page.ActionForOption(label, ".Slider");
            var range = page.Tree.GetComponent<XsrUiRangeInput>(slider)!;
            AssertEqual(9d, range.Maximum);
            var scene = page.Fixture.Shell.Render(new(1000, 700));
            var node = ShowSettingsFixtureControl(page.Fixture, page.Settings, ref scene, page.Tree.Name(slider), new(1000, 700));
            var track = node.Range!.Value.Track(node.Rect);
            var right = new XsrUiPoint(track.X + track.Width, node.Rect.Y + node.Rect.Height / 2);
            AssertTrue(page.Renderer.PointerPressed(right)); page.Render();
            AssertTrue(page.Renderer.PointerReleased(right)); page.Render();
            AssertEqual("9", page.Tree.GetComponent<XsrUiTextInput>(page.Input(label))!.ReadDraft());
            releasedSliders++;
            page.Pump(() => page.Session.SaveCalls == releasedSliders
                && page.Session.Snapshot.Current.Options.Single().Values[label].Value == "9");
            var expectedKind = label == "range" ? ThemeOptionKind.Range : ThemeOptionKind.Number;
            AssertEqual(new ThemeOptionValue(expectedKind, "9"), page.Session.LastSave!.Values[label]);
            AssertEqual(1, page.Session.LastSave.Values.Count);
            AssertEqual((long)releasedSliders, page.Session.LastSave.ExpectedRevision);
            AssertTrue(page.Renderer.HandleKey(XsrUiKey.Home)); page.Render();
            AssertEqual("0", page.Tree.GetComponent<XsrUiTextInput>(page.Input(label))!.ReadDraft());
            AssertTrue(page.Renderer.HandleKey(XsrUiKey.End)); page.Render();
            AssertEqual("9", page.Tree.GetComponent<XsrUiTextInput>(page.Input(label))!.ReadDraft());
            Emit(page.Fixture.Intents, "ui.settings.theme.action", page.ActionForOption(label, ".Increase")); page.Render();
            AssertEqual("9", page.Tree.GetComponent<XsrUiTextInput>(page.Input(label))!.ReadDraft());
        }
        foreach (string label in new[] { "automatic-number", "input-number" })
        {
            var field = page.Input(label);
            var increase = page.ActionForOption(label, ".Increase");
            var decrease = page.ActionForOption(label, ".Decrease");
            AssertEqual("9", page.Tree.GetComponent<XsrUiTextInput>(field)!.ReadDraft());
            Emit(page.Fixture.Intents, "ui.settings.theme.action", increase); page.Render();
            AssertEqual("9", page.Tree.GetComponent<XsrUiTextInput>(field)!.ReadDraft());
            Emit(page.Fixture.Intents, "ui.settings.theme.action", decrease); page.Render();
            AssertEqual("6", page.Tree.GetComponent<XsrUiTextInput>(field)!.ReadDraft());
            Emit(page.Fixture.Intents, "ui.settings.theme.action", increase); page.Render();
            AssertEqual("9", page.Tree.GetComponent<XsrUiTextInput>(field)!.ReadDraft());
            AssertEqual(field, page.Input(label));
        }
        var choiceSlider = page.ActionForOption("choice", ".Slider");
        var choiceScene = page.Fixture.Shell.Render(new(1000, 700));
        _ = ShowSettingsFixtureControl(page.Fixture, page.Settings, ref choiceScene, page.Tree.Name(choiceSlider), new(1000, 700));
        AssertTrue(page.Renderer.Focus(choiceSlider)); AssertTrue(page.Renderer.HandleKey(XsrUiKey.End)); page.Render();
        AssertTrue(page.TextExists("Last"));
        AssertEqual(2, page.Session.SaveCalls);
        Emit(page.Fixture.Intents, "ui.settings.theme.action", page.Find("Theme.OptionsSave"));
        page.Pump(() => page.Session.SaveCalls == 3);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Range, "9"), page.Session.LastSave!.Values["range"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Number, "9"), page.Session.LastSave.Values["number"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Number, "9"), page.Session.LastSave.Values["automatic-number"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Number, "9"), page.Session.LastSave.Values["input-number"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Select, "last"), page.Session.LastSave.Values["choice"]);
        AssertEqual(5, page.Session.LastSave.Values.Count);
        AssertEqual(3L, page.Session.LastSave.ExpectedRevision);
        var saved = page.Session.Snapshot.Current.Options.Single().Values;
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Range, "9"), saved["range"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Number, "9"), saved["number"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Number, "9"), saved["automatic-number"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Number, "9"), saved["input-number"]);
        AssertEqual(new ThemeOptionValue(ThemeOptionKind.Select, "last"), saved["choice"]);
        AssertEqual(10d, rangeDefinition.Maximum!.Value); AssertEqual(10d, numberDefinition.Maximum!.Value);
    }
}
