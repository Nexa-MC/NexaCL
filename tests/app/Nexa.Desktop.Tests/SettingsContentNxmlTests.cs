using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static XsrUiEntityId ContentScrollAncestor(XsrUiTree tree, XsrUiEntityId entity)
    {
        for (var parent = tree.Parent(entity); parent.IsAssigned; parent = tree.Parent(parent))
            if (tree.GetComponent<XsrUiScroll>(parent) is not null) return parent;
        throw new InvalidOperationException("The owned content workspace has no scrolling ancestor.");
    }
    private static string ContentUnsafeTestKey(string name) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)));

    private static void ContentScreenshotOwnedModalRetainsDraftFocusScrollAndMotion()
    {
        var image = GalleryPng(800, 600);
        var item = new InstanceContentEntry("retained.png", false, 33) { Icon = image, ModifiedUtcTicks = 12 };
        int crops = 0;
        var commands = new XsrCommandRouterBuilder();
        commands.Register<InstanceScreenshotCropCommand>(InstanceScreenshotContract.Crop, (_, _) => { crops++; return ValueTask.FromResult(XsrResult.Success()); });
        using var page = new ScreenshotPageFixture([item], (query, _) => ValueTask.FromResult(XsrResult.Success(new InstanceScreenshot(query.Name, image))), commands.Build(new NoopDispatchObserver()));
        page.Render(1000, 450);
        page.Emit("ui.settings.management.action", "ManagementScreenshot.retained.png");
        page.Emit("ui.settings.screenshot-preview.action", "ScreenshotPreview.Crop");
        var tree = page.Fixture.Shell.Tree; var renderer = page.Fixture.Shell.Renderer;
        var panel = page.Find("ScreenshotPreview").Entity;
        XsrUiEntityId FindOwned(string key)
        {
            XsrUiEntityId result = default;
            tree.Walk(panel, entity => { if (tree.Name(entity) == key) result = entity; return true; });
            AssertTrue(result.IsAssigned); return result;
        }
        var width = FindOwned("ScreenshotCropWidth"); var crop = FindOwned("ScreenshotPreview.SaveCrop");
        var cropFields = FindOwned("ScreenshotCrop"); AssertTrue(tree.GetComponent<XsrUiElement>(cropFields)!.IsVisible);
        var editor = tree.GetComponent<XsrUiTextInput>(width)!;
        renderer.SetTextInputValue(width, "321"); renderer.Focus(width, showIndicator: true);
        AssertTrue(renderer.EditText(XsrUiTextEdit.Left));
        int caret = editor.SelectionEnd;
        var scrolling = tree.GetComponent<XsrUiScroll>(panel)!; scrolling.OffsetY = 30;
        var motion = tree.GetComponent<XsrUiOverlayMotion>(panel)!;
        page.Render(760, 450);
        AssertEqual(panel, page.Find("ScreenshotPreview").Entity); AssertEqual(width, FindOwned("ScreenshotCropWidth"));
        AssertTrue(ReferenceEquals(editor, tree.GetComponent<XsrUiTextInput>(width)));
        AssertTrue(ReferenceEquals(scrolling, tree.GetComponent<XsrUiScroll>(panel)));
        AssertTrue(ReferenceEquals(motion, tree.GetComponent<XsrUiOverlayMotion>(panel)));
        AssertEqual("321", editor.ReadDraft()); AssertEqual(caret, editor.SelectionEnd); AssertEqual(width, renderer.Focused); AssertClose(30, scrolling.OffsetY);
        page.Emit("ui.settings.screenshot-preview.action", "ScreenshotPreview.Crop");
        AssertFalse(tree.GetComponent<XsrUiElement>(cropFields)!.IsVisible);
        AssertFalse(page.Scene.Nodes.Any(node => node.Entity == width));
        page.Emit("ui.settings.screenshot-preview.action", crop); AssertEqual(0, crops);
        page.Emit("ui.settings.screenshot-preview.action", "ScreenshotPreview.Crop");
        AssertTrue(tree.GetComponent<XsrUiElement>(cropFields)!.IsVisible);
        AssertEqual(width, FindOwned("ScreenshotCropWidth")); AssertEqual("321", editor.ReadDraft()); AssertEqual(caret, editor.SelectionEnd);
        page.Emit("ui.settings.screenshot-preview.action", "ScreenshotPreview.Close");
        page.Emit("ui.settings.screenshot-preview.action", crop); AssertEqual(0, crops);
    }

    private static void ContentUnsafeScreenshotNamesStayLiteralAndKeepTypedFileIdentity()
    {
        var image = GalleryPng(500, 300);
        string name = "long file {data count} <Button> " + new string('x', 180) + ".png";
        var item = new InstanceContentEntry(name, false, 33) { Icon = image, ModifiedUtcTicks = 55 };
        var reads = new List<InstanceScreenshotQuery>(); InstanceScreenshotCropCommand? cropped = null;
        var commands = new XsrCommandRouterBuilder();
        commands.Register<InstanceScreenshotCropCommand>(InstanceScreenshotContract.Crop, (command, _) => { cropped = command; return ValueTask.FromResult(XsrResult.Success()); });
        using var page = new ScreenshotPageFixture([item], (query, _) => { reads.Add(query); return ValueTask.FromResult(XsrResult.Success(new InstanceScreenshot(query.Name, image))); }, commands.Build(new NoopDispatchObserver()));
        string key = "ManagementScreenshot." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)));
        var card = page.Find(key).Entity; AssertTrue(page.Scene.Nodes.Any(node => node.Text == name));
        AssertTrue(page.Fixture.Shell.Tree.Name(card).Length <= 128);
        page.Emit("ui.settings.management.action", card);
        AssertEqual(name, page.Find("ScreenshotPreviewTitle").Text); AssertTrue(reads.Count > 0);
        AssertTrue(reads.All(query => query.Name == name && query.ExpectedSize == 33 && query.ExpectedModifiedUtcTicks == 55));
        page.Emit("ui.settings.screenshot-preview.action", "ScreenshotPreview.Crop");
        page.Emit("ui.settings.screenshot-preview.action", "ScreenshotPreview.SaveCrop");
        page.Pump(() => cropped is not null);
        AssertEqual(name, cropped!.File.Name); AssertEqual(33L, cropped.File.ExpectedSize); AssertEqual(55L, cropped.File.ExpectedModifiedUtcTicks);
    }

    private static void ContentProjectionFailureLeavesRetainedWorkspaceAndNextReloadUsable()
    {
        var item = new InstanceContentEntry("literal.png", false, 33) { Icon = GalleryPng(400, 200), ModifiedUtcTicks = 1 };
        using var page = new ScreenshotPageFixture([item]);
        var controller = page.Settings; var type = controller.GetType();
        var root = (XsrUiEntityId)type.GetField("_contentWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        var reload = type.GetMethod("ReloadContent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var search = page.Find("ManagementContentSearch").Entity; var editor = page.Fixture.Shell.Tree.GetComponent<XsrUiTextInput>(search)!;
        page.Fixture.Shell.Renderer.SetTextInputValue(search, "literal"); page.Fixture.Shell.Renderer.Focus(search, showIndicator: true);
        page.Render();
        bool rejected = false;
        try { reload.Invoke(controller, [root, new (string Key, string Value)[] { ("screenshots", "invalid-boolean") }]); }
        catch (TargetInvocationException error) when (error.InnerException is NxmlCompileException) { rejected = true; }
        AssertTrue(rejected); AssertEqual(search, page.Find("ManagementContentSearch").Entity); AssertEqual("literal", editor.ReadDraft());
        reload.Invoke(controller, [root, new (string Key, string Value)[] { ("count", "retained") }]);
        page.Render(); AssertTrue(page.Scene.Nodes.Any(node => node.Text == "retained"));
        page.Render(820, 650); AssertEqual(search, page.Find("ManagementContentSearch").Entity);
        AssertTrue(ReferenceEquals(editor, page.Fixture.Shell.Tree.GetComponent<XsrUiTextInput>(search))); AssertEqual(search, page.Fixture.Shell.Renderer.Focused);
        AssertEqual("literal", editor.ReadDraft());
    }
}
