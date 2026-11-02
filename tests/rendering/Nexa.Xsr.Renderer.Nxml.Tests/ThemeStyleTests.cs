using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static XsrUiThemeStyleContext ThemeContext(string kind = "Button", string? key = "action", string[]? classes = null,
        XsrUiThemeInteractionState state = default, double width = 1000, bool dark = false,
        IReadOnlyList<XsrUiStyleIdentity>? parents = null) => new(default,
            new(kind, key, Array.AsReadOnly(classes ?? ["primary"]), kind), parents ?? [], state, new(width, 800),
            new("Linux", dark, Capabilities: new HashSet<string>(StringComparer.Ordinal) { "grid" }), new XsrUiVisualStyle().Snapshot());

    private static void ThemeRulesMatchCascadeConditionsAndParentState()
    {
        var program = NssThemeCompiler.Compile("""
            @tokens { accent: #123456; gap: 8; }
            Button /* tokenizer ignores braces { } inside comments */ { FontSize: 12; Foreground: var(accent); }
            .primary { FontSize: 15; }
            #action { FontSize: 18; }
            @when mode = dark and platform = linux { #action { Background: #08090A; } }
            @supports grid { @viewport width >= 900 { Panel > Button.primary:hover { Padding: var(gap); } } }
            #action { FontSize: 20; }
            """);
        var context = ThemeContext(state: XsrUiThemeInteractionState.Hover, dark: true,
            parents: [new("Panel", "parent", [], "Group")]);
        var computed = program.Compute(context);
        AssertEqual(20d, computed.FontSize!.Value);
        AssertEqual(new XsrUiColor(0x12, 0x34, 0x56), computed.Foreground!.Value);
        AssertEqual(new XsrUiColor(8, 9, 10), computed.Background!.Value);
        AssertEqual(8d, computed.Padding!.Value.Left);
        AssertFalse(program.Compute(context with { Viewport = new(700, 800), Environment = context.Environment with { IsDark = false } }).Padding.HasValue);
        AssertFalse(program.Compute(context with { Parents = [new("Intermediate", null, [], null), new("Panel", null, [], null)] }).Padding.HasValue);
    }

    private static void ThemeCompilerRejectsUnmatchedProtectedAndInvalidDeclarations()
    {
        foreach (string property in new[] { "Command", "Recipe", "Key", "Role", "IsVisible", "Enabled", "Selected", "Password", "Anchor", "ModalLayer", "DismissCommand", "Bindings", "Children" })
            AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile(".never { " + property + ": true; }"));
        foreach (string declaration in new[] { "Width: NaN", "Padding: -1", "FontSize: Infinity", "Opacity: 1.1", "GridColumns: 0fr", "Position: 1", "GridRowSpan: 0", "Transform:scale(0)", "Background: #xyz", "Spacing: 1e20", "MinWidth: 40; MaxWidth: 20", "GridColumnSpan: 65", "GridColumn: 64", "GridRow: 4096" })
            AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("@when mode = dark { .never { " + declaration + "; } }"));
        AssertThrows<NxmlCompileException>(() => NssStyleSheet.Parse(".never { Width: NaN; }"));
        AssertThrows<NxmlCompileException>(() => NssStyleSheet.Parse(".never { Orientation: Sideways; }"));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("@import \"remote\";"));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("{ Command: true; }"));
    }

    private static void ThemeTokensOptionsAndVisibilityAreBoundedAndTyped()
    {
        var inputs = new NssThemeCompileInputs { Options = new Dictionary<string, string> { ["size"] = "19", ["advanced"] = "true", ["name"] = "\"literal\"" } };
        var program = NssThemeCompiler.Compile("@tokens { size: option(size); } Button { FontSize: var(size); }", inputs);
        AssertEqual(19d, program.Compute(ThemeContext()).FontSize!.Value);
        var textInputs = inputs with
        {
            Options = new Dictionary<string, string>(inputs.Options, StringComparer.Ordinal)
            { ["font"] = "\"\\u601D\\u6E90\\u9ED1\\u4F53\"", ["punctuated"] = "\"\\\"( || )\\\"\"" }
        };
        AssertEqual("思源黑体", NssThemeCompiler.Compile("Button{FontFamily:option(font);}", textInputs).Compute(ThemeContext()).FontFamily);
        AssertTrue(NssThemeCompiler.EvaluateCondition("option(font) == \"思源黑体\"", textInputs));
        var typedInputs = inputs with { Options = new Dictionary<string, string> { ["size"] = "19.0", ["text"] = "\"true\"", ["flag"] = "true" } };
        AssertTrue(NssThemeCompiler.EvaluateCondition("option(size) == 19", typedInputs));
        AssertFalse(NssThemeCompiler.EvaluateCondition("option(text) == true", typedInputs));
        AssertFalse(NssThemeCompiler.EvaluateCondition("option(flag) == \"true\"", typedInputs));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.EvaluateCondition("option(text) >= 1", typedInputs));
        AssertTrue(NssThemeCompiler.EvaluateCondition("""(option(punctuated) == "\"( || )\"") && option(advanced)""", textInputs));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("""Button{FontFamily:"\u000A";}"""));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("""Button{FontFamily:"\u12GG";}"""));
        ((Dictionary<string, string>)inputs.Options)["size"] = "400";
        AssertEqual(19d, program.Compute(ThemeContext()).FontSize!.Value);
        ((Dictionary<string, string>)inputs.Options)["size"] = "19";
        AssertTrue(NssThemeCompiler.EvaluateCondition("option(advanced) && (option(size) >= 18 || option(name) == \"other\")", inputs));
        AssertFalse(NssThemeCompiler.EvaluateCondition("!option(advanced) || option(size) < 10", inputs));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.EvaluateCondition("option(missing)", inputs));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("@tokens { a: var(b); b: var(a); } Button{FontSize:12;}"));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("@tokens { unused: NaN; } Button{FontSize:12;}"));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("Button{FontSize:option(size);}", inputs with { Options = new Dictionary<string, string> { ["size"] = "var(hidden)" } }));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile(new string(' ', 1024 * 1024 + 1)));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile(".primary:unknown{Width:2;}"));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile(string.Join(" ", Enumerable.Repeat(".a", 9)) + " {Width:2;}"));
        string tooManyRules = string.Concat(Enumerable.Repeat("Button{Width:2;}", 4097));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile(tooManyRules));
        string tooDeepConditions = string.Concat(Enumerable.Repeat("@when mode = dark {", 18)) + "Button{Width:2;}" + new string('}', 18);
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile(tooDeepConditions));
    }

    private static void ThemeResourcesArePreparedFromEveryConditionalBranch()
    {
        var program = NssThemeCompiler.Compile("""
            @when mode = dark { .never { FontFamily: asset("fonts/body.ttf"); } }
            Button { BackgroundImage: file("selected-picture"); }
            """, new NssThemeCompileInputs { ResourceBindings = new Dictionary<string, string> { ["file(\"selected-picture\")"] = "prepared-42" } });
        AssertEqual(2, program.Resources.Count);
        var fromOption = NssThemeCompiler.Compile("Button{BackgroundImage:option(picture);}",
            new NssThemeCompileInputs { Options = new Dictionary<string, string> { ["picture"] = "asset(\"images/bounded.png\")" } });
        AssertEqual("images/bounded.png", fromOption.Resources.Single().Value);
        var windowsPath = NssThemeCompiler.Compile("""Button{BackgroundImage:file("C:\Users\Nexa\背景.png");}""");
        AssertEqual("""C:\Users\Nexa\背景.png""", windowsPath.Resources.Single().Value);
        AssertTrue(program.Resources.Any(resource => resource.Kind == NssThemeResourceKind.Asset && resource.Value == "fonts/body.ttf"));
        AssertEqual("prepared-42", program.Compute(ThemeContext()).BackgroundImage!.ResourceId);
        var image = NssThemeCompiler.Compile("Button{IconSource:asset(\"icons/page.svg\");BackgroundImage:asset(\"images/background.png\");BackgroundImageFit:contain;BackgroundImageOpacity:.25;}").Compute(ThemeContext());
        AssertEqual("asset(\"icons/page.svg\")", image.ImageSource);
        AssertEqual("contain", image.BackgroundImage!.Fit); AssertEqual(.25d, image.BackgroundImage.Opacity);
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("Button{BackgroundImage:asset(\"../outside.png\");}"));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("Button{FontFamily:url(\"https://example.invalid/font\");}"));
    }

    private static void ThemeAdvancedValuesAndFiniteKeyframesAreCompiled()
    {
        var program = NssThemeCompiler.Compile("""
            @keyframes pulse { 0% { Opacity: .6; TranslateX: -2; } 50% { Opacity: .9; } 100% { Opacity: 1; TranslateX: 2; } }
            .primary { GridColumns: 1fr 2fr auto 80; GridRows: auto 50; ColumnGap: 7; RowGap: 4;
              Order: -1; Position: Absolute; Left: 3; GridColumn: 1; GridRowSpan: 2;
              Transform: translate(2,3) scale(1.2) rotate(15); Shadow: #00000080 0 2 6;
              BackgroundGradient: linear-gradient(45deg,#112233 0%,#334455 100%);
              ItemHeight: 80; ItemWidth: 120; ItemSpacing: 9; ItemColumns: 3; Animation: pulse 1s infinite alternate; }
            """);
        var style = program.Compute(ThemeContext());
        AssertEqual(4, style.Grid!.Columns.Count); AssertEqual(XsrUiGridUnit.Fraction, style.Grid.Columns[0].Unit);
        AssertEqual(7d, style.Grid.ColumnGap); AssertEqual(4d, style.Grid.RowGap);
        AssertEqual(XsrUiThemePosition.Absolute, style.Position!.Value); AssertEqual(3d, style.Left!.Value);
        AssertEqual(1.2d, style.Transform!.Value.ScaleY); AssertEqual(15d, style.Transform.Value.Rotation);
        AssertEqual(6d, style.Shadow!.Value.BlurRadius); AssertEqual(2, style.BackgroundGradient!.Stops.Count);
        AssertEqual(80d, style.VirtualItemHeight!.Value); AssertEqual(3, style.VirtualItemColumns!.Value);
        AssertEqual(2, style.Animations!.Count); AssertTrue(style.Animations.All(animation => animation.Repeat && animation.Alternate));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("@keyframes bad{0%{Width:2;}100%{Width:3;}}Button{Animation:bad 1s;}"));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("@keyframes unused{0%{Opacity:NaN;}100%{Opacity:1;}}Button{Width:2;}"));
        AssertThrows<NxmlCompileException>(() => NssThemeCompiler.Compile("@keyframes unused{0%{BlurRadius:5000;}100%{BlurRadius:1;}}Button{Width:2;}"));
    }

    private static void AuthoredStyleIdentityAndSourceSurviveDynamicReload()
    {
        var tree = new XsrUiTree(); var store = new XsrStateStoreBuilder().Build();
        var slot = NxmlUiLoader.LoadDetached(Compile("<Dynamic Key=\"themeslot\" />"), tree, store);
        var owner = new NxmlDynamicContent(tree, store);
        var template = new NxmlDynamicTemplate("""<nxml-dynamic><TextInput Key="draft" Label="draft" Class="{data classes}" Width="200" Height="40" Padding="6" /></nxml-dynamic>""");
        var data = new Dictionary<string, string> { ["classes"] = "primary first" };
        var first = template.Compile(data, NxmlComponentLibrary.Default, NssStyleSheet.Parse(".primary{FontSize:14;Padding:2;}"));
        AssertEqual(2, first.Root.StyleIdentity!.Classes.Count);
        AssertTrue(first.Root.StyleSource.Any(source => source.Name == "Padding" && source.Origin == NxmlStyleOrigin.ExplicitMarkup));
        AssertTrue(first.Root.StyleSource.Any(source => source.Name == "FontSize" && source.Origin == NxmlStyleOrigin.HostStyleSheet));
        var entity = owner.Append(slot, first);
        var renderer = new XsrUiRenderer(tree, store); renderer.SetRoot(slot); renderer.Render();
        AssertTrue(renderer.Focus(entity)); AssertTrue(renderer.InsertText("retained draft")); AssertTrue(renderer.EditText(XsrUiTextEdit.Left));
        var input = tree.GetComponent<XsrUiTextInput>(entity)!;
        int caret = input.SelectionEnd; var baseElement = tree.GetComponent<XsrUiElement>(entity)!;
        renderer.ApplyTheme(NssThemeCompiler.Compile(".primary{Padding:12;Foreground:#123456;}"), 1);
        renderer.Render(); AssertEqual(entity, renderer.Focused);
        AssertEqual("retained draft", input.ReadDraft()); AssertEqual(caret, input.SelectionEnd); AssertEqual(6d, baseElement.Padding.Left);
        data["classes"] = "primary second";
        owner.Reload(entity, template.Compile(data, NxmlComponentLibrary.Default, NssStyleSheet.Parse(".primary{FontSize:14;Padding:2;}")));
        AssertTrue(tree.GetComponent<XsrUiStyleIdentity>(entity)!.Classes.Contains("second"));
        AssertTrue(ReferenceEquals(input, tree.GetComponent<XsrUiTextInput>(entity))); AssertEqual(caret, input.SelectionEnd);
        renderer.ClearTheme(2); renderer.Render(); AssertEqual(6d, baseElement.Padding.Left);
        int invalidations = 0; tree.RenderInvalidated += (_, _) => invalidations++;
        owner.Reload(entity, template.Compile(data, NxmlComponentLibrary.Default, NssStyleSheet.Parse(".primary{FontSize:14;Padding:2;}")));
        AssertEqual(0, invalidations);
    }
}
