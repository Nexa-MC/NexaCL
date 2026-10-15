using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private sealed class VirtualMetricsTheme(long revision, string key, XsrUiComputedStyle style) : IXsrUiThemeStyleProgram
    {
        public long Revision => revision;
        public XsrUiComputedStyle Compute(XsrUiThemeStyleContext context) => context.Identity.Key == key ? style : XsrUiComputedStyle.Empty;
    }

    private static void ThemeVirtualCatalogsUseSameGenerationGeometryWithoutRequeryOrDraftLoss()
    {
        using (var f = new ContinuousResourceHarness())
        {
            f.Complete(0, ContinuousProjects("Metric", 40), hasMore: false); f.Pump(() => f.Rows.Length > 0);
            var renderer = f.Fixture.Shell.Renderer; var list = f.Page.Find("ResourceList"); var row = f.Row("Metric0");
            var search = f.Page.Find("ResourceSearch"); renderer.SetTextInputValue(search, "unsubmitted"); renderer.Focus(search);
            renderer.EditText(XsrUiTextEdit.Left); var editor = f.Fixture.Shell.Tree.GetComponent<XsrUiTextInput>(search)!;
            int caret = editor.SelectionEnd;
            renderer.ApplyTheme(new VirtualMetricsTheme(1, "ResourceList", new() { VirtualItemHeight = 100, VirtualItemSpacing = 10, VirtualItemWidth = 500 }), 1);
            f.RenderFrames(1);
            AssertEqual(row, f.Row("Metric0")); AssertClose(100, f.Scene.Nodes.Single(node => node.Entity == row).Rect.Height);
            AssertClose(500, f.Scene.Nodes.Single(node => node.Entity == row).Rect.Width);
            AssertClose(40 * 100 + 39 * 10, f.Snapshot.ContentHeight); AssertEqual(1, f.Reads.Count);
            AssertEqual(search, renderer.Focused); AssertEqual("unsubmitted", editor.ReadDraft()); AssertEqual(caret, editor.SelectionEnd);
            AssertTrue(renderer.TryGetVirtualLayoutMetrics(list, out var metrics)); AssertEqual(1L, metrics.ActivationGeneration);
            var scrolling = f.Fixture.Shell.Tree.GetComponent<XsrUiScroll>(list)!; scrolling.OffsetY = 260;
            f.Fixture.Shell.Tree.MarkDirty(list, XsrUiDirtyKinds.Layout); f.RenderFrames(1);
            renderer.ClearTheme(2); f.RenderFrames(1);
            AssertTrue(renderer.TryGetVirtualLayoutMetrics(list, out var restoredMetrics));
            int columns = f.Scene.Nodes.Where(node => f.Rows.Contains(node.Entity)).GroupBy(node => node.Rect.Y).First().Count();
            double originalLine = 260d / 110;
            AssertClose((Math.Floor(Math.Floor(originalLine) / columns) + originalLine % 1)
                * (restoredMetrics.ItemHeight + restoredMetrics.Spacing), scrolling.OffsetY);
            AssertTrue(f.Rows.Length <= metrics.RealizationLimit);
            AssertEqual(1, f.Reads.Count); AssertEqual("unsubmitted", editor.ReadDraft());
            double viewportHeight = f.Scene.Nodes.Single(node => node.Entity == list).Rect.Height;
            double? sourceHeight = f.Fixture.Shell.Tree.GetComponent<XsrUiElement>(list)!.Height;
            XsrUiColor foreground = new(46, 75, 111);
            renderer.ApplyTheme(new VirtualMetricsTheme(2, "ResourceList", new() { Height = 500, Foreground = foreground }), 3);
            f.RenderFrames(1);
            AssertClose(viewportHeight, f.Scene.Nodes.Single(node => node.Entity == list).Rect.Height);
            AssertEqual(sourceHeight, f.Fixture.Shell.Tree.GetComponent<XsrUiElement>(list)!.Height);
            AssertEqual(foreground, f.Scene.Nodes.Single(node => node.Entity == list).VisualStyle.Foreground);
            AssertEqual(1, f.Reads.Count); AssertEqual("unsubmitted", editor.ReadDraft());
        }
        var source = new LargeInstallSource(); source.Loader.SetResult([]);
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), installSource: source);
        fixture.Shell.Renderer.ReducedMotion = true;
        Emit(fixture.Intents, "ui.navigation.download"); Emit(fixture.Intents, "ui.install.java"); fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        var size = new XsrUiSize(1024, 600); var scene = fixture.Shell.Render(size);
        var host = FindEntity(fixture.Shell, "JavaMinecraftVersions"); var rowBefore = FindByKey(fixture.Shell, scene, "CatalogRow:game:1.20.1").Entity;
        fixture.Shell.Renderer.Focus(rowBefore);
        fixture.Shell.Renderer.ApplyTheme(new VirtualMetricsTheme(3, "JavaMinecraftVersions", new() { VirtualItemHeight = 60, VirtualItemSpacing = 7, VirtualItemWidth = 600 }), 4);
        scene = fixture.Shell.Render(size);
        AssertEqual(rowBefore, FindByKey(fixture.Shell, scene, "CatalogRow:game:1.20.1").Entity);
        AssertClose(60, FindByKey(fixture.Shell, scene, "CatalogRow:game:1.20.1").Rect.Height);
        AssertClose(7, fixture.Shell.Tree.GetComponent<XsrUiElement>(rowBefore)!.Margin.Bottom);
        AssertEqual(rowBefore, fixture.Shell.Renderer.Focused); AssertEqual(0, source.LoaderReads);
        AssertTrue(fixture.Shell.Renderer.TryGetVirtualLayoutMetrics(host, out var installMetrics));
        AssertEqual(4L, installMetrics.ActivationGeneration); AssertTrue(fixture.Shell.Tree.Children(host).Count <= installMetrics.RealizationLimit + 2);
    }

    private static void ThemeScreenshotMetricsReflowOwnedCardsAndRetainPreviewRoutes()
    {
        var entries = Enumerable.Range(0, 20).Select(index => new InstanceContentEntry($"theme-{index}.png", false, 33)
        { ImageWidth = 1200, ImageHeight = 300 }).ToArray();
        var geometry = DesktopScreenshotGalleryLayout.Create(entries, 700, false, minimumCardWidth: 160, gap: 20,
            minimumImageHeight: 140, columns: 2);
        AssertEqual(2, geometry.ColumnCount); AssertClose(340, geometry.CardWidth); AssertClose(20, geometry.Spacing);
        AssertTrue(geometry.Columns.SelectMany(column => column).All(slot => slot.ImageHeight >= 140));
        foreach (var column in geometry.Columns)
            for (int index = 1; index < column.Count; index++) AssertClose(column[index - 1].Top + column[index - 1].Height + 20, column[index].Top);
        AssertTrue(geometry.SelectWindow(0, 500).Sum(column => column.Count) <= 1024);
        AssertThrows<ArgumentException>(() => DesktopScreenshotGalleryLayout.Create(entries, 700, false, columns: 13));
        using var page = new ScreenshotPageFixture(entries);
        var renderer = page.Fixture.Shell.Renderer; var first = page.Find("ManagementScreenshot.theme-0.png").Entity;
        var list = page.Find("ManagementContentList").Entity;
        renderer.Focus(first);
        renderer.ApplyTheme(new VirtualMetricsTheme(1, "ManagementContentList", new()
        { VirtualItemWidth = 160, VirtualItemHeight = 140, VirtualItemSpacing = 20, VirtualItemColumns = 2 }), 1);
        page.Render();
        AssertEqual(first, page.Find("ManagementScreenshot.theme-0.png").Entity); AssertEqual(first, renderer.Focused);
        var waterfall = page.Find("ScreenshotWaterfall").Entity;
        AssertEqual(2, page.Fixture.Shell.Tree.Children(waterfall).Count);
        AssertClose(20, page.Fixture.Shell.Tree.GetComponent<XsrUiStackPanel>(waterfall)!.Spacing);
        AssertTrue(renderer.TryGetVirtualLayoutMetrics(list, out var metrics)); AssertEqual(1L, metrics.ActivationGeneration);
        AssertEqual(XsrUiVirtualLayoutKind.Waterfall, metrics.Kind); AssertTrue(metrics.HasColumnsOverride);
        AssertEqual("ui.settings.management.action", page.Fixture.Shell.Tree.GetComponent<XsrUiCommandBinding>(first)!.Command.ToString());
        renderer.ClearTheme(2); page.Render(); AssertEqual(first, page.Find("ManagementScreenshot.theme-0.png").Entity);
        AssertEqual(first, renderer.Focused);
    }
}
