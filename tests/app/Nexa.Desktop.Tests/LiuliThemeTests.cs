using System.Text.Json;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static readonly string[] LiuliCardKeys = ["CardAccount", "CardVersion", "CardAbout"];
    private static void LiuliThemePreservesReadableStatesAndHostGeometry()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Themes", "Liuli");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "theme.json")));
        AssertEqual("nexa.liuli", manifest.RootElement.GetProperty("id").GetString()!);
        AssertEqual("1.0.1", manifest.RootElement.GetProperty("version").GetString()!);
        AssertFalse(manifest.RootElement.GetProperty("appearance").GetProperty("resources").TryGetProperty("background", out _));
        var options = manifest.RootElement.GetProperty("options").EnumerateArray().ToDictionary(
            option => option.GetProperty("id").GetString()!, option =>
            {
                var value = option.GetProperty("default");
                return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            });
        string source = string.Join("\n", manifest.RootElement.GetProperty("styles").EnumerateArray()
            .Select(path => File.ReadAllText(Path.Combine(root, path.GetString()!))));
        var program = NssThemeCompiler.Compile(source, new NssThemeCompileInputs { Options = options });
        foreach (bool dark in new[] { false, true })
        {
            XsrUiComputedStyle Style(string kind, string key, string[]? classes = null, XsrUiThemeInteractionState state = default) =>
                program.Compute(new(default, new(kind, key, classes ?? [], kind), [], state, new(1040, 740),
                    new("Windows", dark), new XsrUiVisualStyle().Snapshot()));
            var card = Style("StackPanel", "CardAccount");
            AssertEqual(20d, card.CornerRadius!.Value);
            AssertEqual(XsrUiSurfaceKind.Glass, card.Surface!.Value);
            AssertTrue(card.Shadow is not null);
            AssertFalse(card.Width.HasValue); AssertFalse(card.Height.HasValue); AssertFalse(card.Padding.HasValue);
            AssertFalse(card.BlurRadius.HasValue);
            AssertEqual((byte)0, Style("ContentHost", "Host", ["layout-Shell-14"]).Background!.Value.Alpha);
            // The package itself targets the window root. Alpha 24's generated
            // appearance-background rule must never be needed to place its wallpaper.
            var wallpaper = Style("Shell", "Root").BackgroundImage!;
            AssertEqual("asset(\"assets/liuli.svg\")", wallpaper.ResourceId);
            AssertEqual(.38d, wallpaper.Opacity);
            AssertTrue(Style("ContentHost", "Host", ["layout-Shell-14"]).BackgroundImage is null);
            var text = Style("Text", "AccountName");
            var primary = Style("Button", "LaunchButton");
            AssertTrue(Contrast(text.Foreground!.Value, dark ? new(25, 45, 59) : new(247, 251, 252)) >= 7);
            AssertTrue(Contrast(primary.Foreground!.Value, primary.Background!.Value) >= 4.5);
            AssertEqual(2d, Style("Button", "AccountSwitch", state: XsrUiThemeInteractionState.FocusVisible).BorderWidth!.Value);
            foreach (var state in new[] { default, XsrUiThemeInteractionState.Hover, XsrUiThemeInteractionState.Pressed, XsrUiThemeInteractionState.FocusVisible })
            {
                var add = Style("Button", "AccountAdd", state: state);
                var picker = Style("Button", "InstanceListButton", state: state);
                AssertTrue(add.Background == picker.Background);
                AssertTrue(add.Foreground == picker.Foreground);
                AssertTrue(add.Border == picker.Border);
                AssertTrue(add.BorderWidth == picker.BorderWidth);
                AssertTrue(add.Hover == picker.Hover);
            }
            // Runtime error/status text is not globally recolored by the theme.
            AssertFalse(Style("Text", "AnyFutureError").Foreground.HasValue);
            AssertFalse(Style("Text", "TaskError", ["layout-TaskCard-TaskCardError"]).Foreground.HasValue);
            AssertFalse(Style("Icon", "Release", ["resource-version-release"]).Foreground.HasValue);
            foreach (string pageCard in new[] { "version-row", "resource-project", "settings-content-card", "wardrobe-card", "launch-install-InstallJavaChoice" })
            {
                var pageStyle = Style("StackPanel", "PageCard", [pageCard]);
                AssertEqual(XsrUiSurfaceKind.Glass, pageStyle.Surface!.Value);
                AssertEqual(20d, pageStyle.CornerRadius!.Value);
                AssertFalse(pageStyle.Padding.HasValue);
                AssertFalse(pageStyle.BlurRadius.HasValue);
            }
            // Host owns interruptible control/overlay motion. Theme does not restart it on state changes.
            AssertTrue(Style("Button", "SettingsTab", ["settings-navigation-option"], XsrUiThemeInteractionState.Pressed).Animations is not { Count: > 0 });
            AssertTrue(Style("Button", "ListRow", ["settings-content-card"]).Animations is not { Count: > 0 });
            AssertTrue(Style("StackPanel", "Dialog", ["feedback-dialog-card"]).Animations is not { Count: > 0 });
        }
        LiuliMotionSettlesAndRespectsReducedMotion(program);
        foreach (int percent in new[] { 0, 38, 70 })
        {
            options["wallpaper-opacity"] = percent.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var opacityProgram = NssThemeCompiler.Compile(source, new NssThemeCompileInputs { Options = options });
            AssertEqual(percent / 100d, opacityProgram.Compute(new(default, new("Shell", "Root", [], "Shell"), [], default,
                new(800, 740), new("Windows", true), new XsrUiVisualStyle().Snapshot())).BackgroundImage!.Opacity);
        }
        options["wallpaper-opacity"] = "38";
        options["motion"] = "false";
        var quiet = NssThemeCompiler.Compile(source, new NssThemeCompileInputs { Options = options });
        AssertTrue(quiet.Compute(new(default, new("StackPanel", "CardAccount", [], "Group"), [], default,
            new(800, 740), new("Linux", false), new XsrUiVisualStyle().Snapshot())).Animations is not { Count: > 0 });
        options["radius"] = "26";
        var custom = NssThemeCompiler.Compile(source, new NssThemeCompileInputs { Options = options });
        AssertEqual(26d, custom.Compute(new(default, new("StackPanel", "CardAccount", [], "Group"), [], default,
            new(800, 740), new("Linux", false), new XsrUiVisualStyle().Snapshot())).CornerRadius!.Value);
        static double Contrast(XsrUiColor a, XsrUiColor b)
        {
            static double Channel(byte value) { double c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
            static double Luminance(XsrUiColor c) => .2126 * Channel(c.Red) + .7152 * Channel(c.Green) + .0722 * Channel(c.Blue);
            double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
        }
    }

    private static void LiuliMotionSettlesAndRespectsReducedMotion(IXsrUiThemeStyleProgram program)
    {
        XsrUiTree tree = new(); var root = tree.Create("root");
        tree.SetComponent(root, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        var cards = LiuliCardKeys.Select(key =>
        {
            var entity = tree.Create(key);
            tree.SetComponent(entity, new XsrUiElement { Width = 100, Height = 40 });
            tree.SetComponent(entity, new XsrUiStyleIdentity("StackPanel", key, [], "Group"));
            tree.Attach(entity, root); return entity;
        }).ToArray();
        LiuliClock clock = new();
        var renderer = new XsrUiRenderer(tree, new XsrStateStoreBuilder().Build(), timeProvider: clock);
        renderer.SetRoot(root); renderer.ApplyTheme(program, 1); renderer.Render();
        AssertTrue(renderer.HasThemeAnimations);
        clock.Advance(45); AssertTrue(renderer.AdvanceThemeAnimations());
        var entering = renderer.Render();
        double Opacity(int index) => entering.Nodes.Single(node => node.Entity == cards[index]).VisualStyle.Opacity;
        AssertTrue(Opacity(0) > Opacity(1)); AssertTrue(Opacity(1) > Opacity(2));
        renderer.ReducedMotion = true;
        var reduced = renderer.Render();
        foreach (var card in cards) AssertEqual(1d, reduced.Nodes.Single(node => node.Entity == card).VisualStyle.Opacity);
        AssertFalse(renderer.HasThemeAnimations);
        renderer.ReducedMotion = false; clock.Advance(400); renderer.AdvanceThemeAnimations();
        renderer.Render(); AssertFalse(renderer.HasThemeAnimations); AssertFalse(renderer.AdvanceThemeAnimations());
    }

    private sealed class LiuliClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int milliseconds) => _now += TimeSpan.FromMilliseconds(milliseconds);
    }
}
