using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexa.Core.Media;
using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop;

internal static partial class Program
{
    private static async Task ValidateDynamicSettingsContentAsync(XsrUiShell shell, XsrStateStore store, DesktopUiIntentSink intents)
    {
        // The shipped executable loads its actual external consumers. Every service result and
        // mutation below is a closed fixture; only a unique temporary PNG directory is written.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string directory = Path.Combine(Path.GetTempPath(), "nexa-settings-content-validation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousPage = shell.Stage.Navigation.Current;
        var previousFocus = shell.Renderer.Focused;
        var previousLocalizer = shell.Renderer.TextLocalizer;
        var previousScheme = shell.Renderer.ColorScheme;
        bool previousMotion = shell.Renderer.ReducedMotion;
        var languageState = store.Resolve(XsrSemanticId.Parse("UiLanguage"));
        string? previousLanguage = store.Read<string>(languageState).Value;
        var revisionState = store.Resolve(SettingsPolicyContract.RevisionKey);
        var language = new UiLocalizationCatalog();
        SettingsPageController? controller = null;
        var fixture = new SettingsContentValidationFixture(directory);
        try
        {
            language.SetLanguage("zh-Hans"); store.Publish(languageState, "zh-Hans");
            shell.Renderer.TextLocalizer = language.Translate; shell.Renderer.ReducedMotion = true;
            var queries = new XsrQueryRouterBuilder(); var commands = new XsrCommandRouterBuilder();
            queries.Register<SettingsCatalogQuery, SettingsCatalogSnapshot>(SettingsPolicyContract.CatalogQuery,
                (query, _) => new(XsrResult.Success(SettingsCatalog.Read(query))));
            queries.Register<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(SettingsPolicyContract.EffectiveQuery, (query, readToken) =>
            {
                fixture.RequireInstance(query.InstanceId);
                return new(XsrResult.Success(new SettingsEffectiveSnapshot(store.Read<long>(revisionState, readToken).Value,
                    SettingsPolicySchema.Definitions.Select(definition => new SettingsEffectiveValue(definition.Key,
                        new(SettingsOverrideMode.Custom, definition.DefaultValue), SettingsLayer.Builtin, definition.Timing, null)).ToArray())));
            });
            queries.Register<InstanceManagementQuery, InstanceManagementSnapshot>(InstanceManagementContract.Query,
                (query, _) => new(XsrResult.Success(fixture.Snapshot(query.InstanceDirectory))));
            queries.Register<InstanceScreenshotQuery, InstanceScreenshot>(InstanceScreenshotContract.Read, fixture.ReadScreenshot);
            commands.Register<InstanceScreenshotCropCommand>(InstanceScreenshotContract.Crop, (command, _) =>
            {
                fixture.RequireFile(command.File); fixture.Crops++; return new(XsrResult.Success());
            });
            using var feedback = new DesktopFeedbackService();
            var observer = new SettingsContentValidationObserver();
            controller = new(shell, intents, queries.Build(observer), commands.Build(observer), store, feedback, () => fixture.Instance)
            {
                CopyScreenshotAsync = bytes =>
                {
                    Require(bytes.Span.SequenceEqual(fixture.Image.Bytes.Span), "Screenshot copy changed its controlled encoded carrier.");
                    fixture.Copies++; return Task.CompletedTask;
                }
            };
            await controller.PrepareStartupAsync(timeout.Token).ConfigureAwait(false);
            shell.Stage.Navigation.Replace(controller.Page);
            var size = new XsrUiSize(810, 500);
            XsrUiScene scene = shell.Render(size);
            await Pump(() => Optional("SettingsNav.mods").IsAssigned).ConfigureAwait(false);
            Select("mods"); await Pump(() => Optional("ManagementContentList").IsAssigned).ConfigureAwait(false);
            var list = Find("ManagementContentList"); var sections = ScrollAncestor(list);
            var scroll = shell.Tree.GetComponent<XsrUiScroll>(sections)!;
            var search = Find("ManagementContentSearch"); var draft = shell.Tree.GetComponent<XsrUiTextInput>(search)!;
            var first = Find("ManagementContentDetails.fixture-0000.jar"); var firstRow = shell.Tree.Parent(shell.Tree.Parent(shell.Tree.Parent(first)));
            Require(shell.Tree.GetComponent<NxmlDynamicSlot>(list) is not null
                && shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(firstRow)) is not null,
                "Virtual file cards did not enter owned Dynamic slots.");
            Require(shell.Renderer.Focus(first, showIndicator: true), "The controlled virtual row could not receive focus.");
            scroll.OffsetY = 1200; shell.Tree.MarkDirty(sections, XsrUiDirtyKinds.Layout); Render(); Render();
            Require(shell.Tree.IsAlive(first) && shell.Tree.Parent(firstRow) == list && shell.Renderer.Focused == first,
                "The virtual file window retired its focused owned card.");
            var fileMetric = DesktopMarkup.Load("SettingsContentMetrics.nxml").Root.Children.Single(node => node.Key == "files");
            double extent = shell.Tree.Children(list).Sum(entity =>
            {
                var box = shell.Tree.GetComponent<XsrUiElement>(entity)!; return (box.Height ?? 0) + box.Margin.Vertical;
            });
            Require(Math.Abs(extent - SettingsContentValidationFixture.ModCount * fileMetric.Height!.Value) < .01 && shell.Tree.Children(list).Count < 40,
                "The virtual file window changed its exact extent or attached bound.");
            scroll.OffsetY = 0; shell.Tree.MarkDirty(sections, XsrUiDirtyKinds.Layout); Render();
            shell.Renderer.SetTextInputValue(search, "Fixture"); Require(shell.Renderer.Focus(search, showIndicator: true), "The content search was not focusable.");
            Require(shell.Renderer.EditText(XsrUiTextEdit.Left), "The controlled content search rejected a caret edit."); Render(); int caret = draft.SelectionEnd;
            foreach (bool dark in new[] { false, true })
            {
                shell.Renderer.ColorScheme = new(dark); Render();
                Require(Find("ManagementContentSearch") == search && ReferenceEquals(draft, shell.Tree.GetComponent<XsrUiTextInput>(search))
                    && draft.ReadDraft() == "Fixture" && draft.SelectionEnd == caret && shell.Renderer.Focused == search,
                    "The 810x500 content theme projection replaced its search draft, caret or focus.");
            }
            language.SetLanguage("en"); store.Publish(languageState, "en"); shell.Renderer.TextLocalizer = language.Translate; Render(); Render();
            Require(Find("ManagementContentSearch") == search && draft.ReadDraft() == "Fixture" && draft.SelectionEnd == caret
                && ReferenceEquals(scroll, shell.Tree.GetComponent<XsrUiScroll>(sections)) && scroll.OffsetY == 0,
                "Content locale projection replaced draft or scroll ownership.");

            Select("screenshots"); await Pump(() => Optional("ScreenshotWaterfall").IsAssigned).ConfigureAwait(false);
            var card = Find("ManagementScreenshot." + SettingsContentValidationFixture.ScreenshotName); var image = FindWithin(card, "ManagementContentIcon");
            Require(shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(card)) is not null
                && shell.Tree.GetComponent<XsrUiImage>(image)?.Raster?.Image == fixture.Image,
                "Screenshot gallery lost its owned column or immutable carrier.");
            Require(shell.Renderer.Focus(card, showIndicator: true), "The controlled screenshot source could not receive focus.");
            Activate(card, "ui.settings.management.action"); await Pump(() => shell.Stage.Overlays.Any(IsScreenshotSlot)).ConfigureAwait(false);
            var modalSlot = ScreenshotModalSlot(); var panel = FindWithin(modalSlot, "ScreenshotPreview");
            Activate(FindWithin(panel, "ScreenshotPreview.Crop"), "ui.settings.screenshot-preview.action");
            var width = FindWithin(panel, "ScreenshotCropWidth"); var crop = FindWithin(panel, "ScreenshotPreview.SaveCrop");
            var input = shell.Tree.GetComponent<XsrUiTextInput>(width)!; var modalScroll = shell.Tree.GetComponent<XsrUiScroll>(panel)!;
            var motion = shell.Tree.GetComponent<XsrUiOverlayMotion>(panel)!;
            shell.Renderer.SetTextInputValue(width, "1"); shell.Renderer.Focus(width, showIndicator: true);
            Require(shell.Renderer.EditText(XsrUiTextEdit.Left), "The controlled crop input rejected a caret edit.");
            int cropCaret = input.SelectionEnd; double modalOffset = modalScroll.OffsetY;
            size = new(1000, 500); Render(); size = new(810, 500); Render();
            Require(ScreenshotModalSlot() == modalSlot && FindWithin(modalSlot, "ScreenshotPreview") == panel && FindWithin(panel, "ScreenshotCropWidth") == width && input.ReadDraft() == "1"
                && input.SelectionEnd == cropCaret && shell.Renderer.Focused == width
                && ReferenceEquals(modalScroll, shell.Tree.GetComponent<XsrUiScroll>(panel))
                && ReferenceEquals(motion, shell.Tree.GetComponent<XsrUiOverlayMotion>(panel)) && modalScroll.OffsetY == modalOffset,
                "Screenshot resize lost modal identity, crop draft, caret, focus, scroll or motion.");
            Activate(FindWithin(panel, "ScreenshotPreview.Crop"), "ui.settings.screenshot-preview.action");
            intents.Emit(XsrSemanticId.Parse("ui.settings.screenshot-preview.action"), crop, XsrCorrelationId.Create()); Render();
            Require(fixture.Crops == 0, "A hidden crop control gained mutation authority.");
            Activate(FindWithin(panel, "ScreenshotPreview.Crop"), "ui.settings.screenshot-preview.action");
            Require(FindWithin(panel, "ScreenshotCropWidth") == width && input.ReadDraft() == "1", "Crop reveal replaced its retained editor.");
            Activate(FindWithin(panel, "ScreenshotPreview.Copy"), "ui.settings.screenshot-preview.action");
            await Pump(() => fixture.Copies == 1).ConfigureAwait(false);
            Require(fixture.Reads.All(file => file.InstanceDirectory == fixture.InitialInstance && file.Name == SettingsContentValidationFixture.ScreenshotName
                && file.ExpectedSize == fixture.Image.Bytes.Length && file.ExpectedModifiedUtcTicks == fixture.ModifiedTicks),
                "Screenshot reads lost their exact typed file identity.");
            Activate(FindWithin(panel, "ScreenshotPreview.Close"), "ui.settings.screenshot-preview.action");
            Require(!shell.Tree.IsAlive(modalSlot) && !shell.Tree.IsAlive(panel) && shell.Renderer.Focused == card, "Modal retirement did not restore its retained gallery source.");

            fixture.DeferReads = true;
            string unsafeKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SettingsContentValidationFixture.UnsafeScreenshotName)));
            var unsafeCard = Find("ManagementScreenshot." + unsafeKey);
            Activate(unsafeCard, "ui.settings.management.action"); await Pump(() => fixture.Pending is not null).ConfigureAwait(false);
            var unsafeModalSlot = ScreenshotModalSlot(); var unsafePanel = FindWithin(unsafeModalSlot, "ScreenshotPreview");
            Require(shell.Tree.GetComponent<XsrUiText>(FindWithin(unsafePanel, "ScreenshotPreviewTitle"))?.Content == SettingsContentValidationFixture.UnsafeScreenshotName,
                "Unsafe screenshot metadata was reinterpreted as markup or an action key.");
            var retiredCopy = FindWithin(unsafePanel, "ScreenshotPreview.Copy"); var retiredImage = FindWithin(unsafePanel, "ScreenshotPreviewImage");
            fixture.Instance = fixture.SecondInstance; Render(); Render();
            Require(fixture.PendingToken.IsCancellationRequested && !shell.Tree.IsAlive(unsafeModalSlot) && !shell.Tree.IsAlive(retiredImage), "Instance scope change failed to retire its original read and modal.");
            fixture.Pending!.SetResult(XsrResult.Success(new InstanceScreenshot(fixture.UnsafePath, fixture.Image)));
            await Task.Delay(1, timeout.Token).ConfigureAwait(false); Render();
            intents.Emit(XsrSemanticId.Parse("ui.settings.screenshot-preview.action"), retiredCopy, XsrCorrelationId.Create()); Render();
            Require(fixture.Copies == 1 && fixture.Crops == 0 && !shell.Tree.IsAlive(unsafeModalSlot) && !shell.Stage.Overlays.Any(IsScreenshotSlot),
                "A stale result or retired control acquired authority in the new instance.");
            Require(controller.ScreenshotThumbnailOwnedBytes + controller.ScreenshotThumbnailReservedBytes <= 64L * 1024 * 1024,
                "Controlled screenshot ownership exceeded the existing carrier and reservation quota.");
            Console.WriteLine("Dynamic settings content validation passed: controlled typed fixtures, bounded owned virtual rows and exact extent, retained locale/theme/search/modal drafts and motion, literal filenames, exact screenshot identity and stale scope retirement; no real instance mutations.");

            void Render() => scene = shell.Render(size);
            async Task Pump(Func<bool> ready)
            {
                while (true) { timeout.Token.ThrowIfCancellationRequested(); Render(); if (ready()) return; await Task.Delay(1, timeout.Token).ConfigureAwait(false); }
            }
            XsrUiEntityId Section()
            {
                var pager = FindWithin(controller.Page, "SettingsPager");
                var position = shell.Tree.GetComponent<XsrUiPager>(pager)!.PageIndex;
                return shell.Tree.Children(pager)[position];
            }
            XsrUiEntityId Optional(string key)
            {
                XsrUiEntityId result = default;
                shell.Tree.Walk(shell.Stage.Root, entity => { if (shell.Tree.Name(entity) == key && scene.Nodes.Any(node => node.Entity == entity && node.IsAccessible)) result = entity; return true; });
                return result;
            }
            XsrUiEntityId Find(string key) => Optional(key) is { IsAssigned: true } found ? found : FindWithin(Section(), key);
            bool IsScreenshotSlot(XsrUiEntityId entity) => shell.Tree.IsAlive(entity) && shell.Tree.Name(entity) == "ScreenshotPreviewSlot";
            XsrUiEntityId ScreenshotModalSlot()
            {
                var slots = shell.Stage.Overlays.Where(IsScreenshotSlot).ToArray();
                Require(slots.Length == 1, "The controlled screenshot preview must have exactly one stage-owned slot.");
                var slot = slots[0];
                Require(shell.Tree.Parent(slot) == shell.Stage.Root && shell.Tree.GetComponent<NxmlDynamicSlot>(slot) is not null
                    && shell.Tree.GetComponent<XsrUiOverlayLayer>(slot)?.IsModal == true,
                    "The controlled screenshot preview escaped its owned modal slot.");
                return slot;
            }
            XsrUiEntityId FindWithin(XsrUiEntityId root, string key)
            {
                Require(shell.Tree.IsAlive(root), "The shipped settings content scope was retired: " + key);
                XsrUiEntityId result = default; int matches = 0;
                shell.Tree.Walk(root, entity => { if (shell.Tree.Name(entity) == key) { result = entity; matches++; } return true; });
                Require(matches == 1, "The shipped settings content scope must contain exactly one element: " + key); return result;
            }
            XsrUiEntityId ScrollAncestor(XsrUiEntityId entity)
            {
                for (var parent = shell.Tree.Parent(entity); parent.IsAssigned; parent = shell.Tree.Parent(parent)) if (shell.Tree.GetComponent<XsrUiScroll>(parent) is not null) return parent;
                throw new InvalidOperationException("The controlled content slot has no scroll ancestor.");
            }
            void Activate(XsrUiEntityId entity, string command) { intents.Emit(XsrSemanticId.Parse(command), entity, XsrCorrelationId.Create()); Render(); }
            void Select(string page) { Activate(FindWithin(controller.Page, "SettingsNav." + page), "ui.settings.section"); Render(); Require(controller.SelectedSection == page, "The controlled settings page did not navigate."); }
        }
        finally
        {
            fixture.Pending?.TrySetResult(XsrResult.Success(new InstanceScreenshot(fixture.UnsafePath, fixture.Image)));
            shell.Stage.Navigation.Replace(previousPage); controller?.Dispose();
            if (controller is not null && shell.Tree.IsAlive(controller.Page)) shell.Tree.Destroy(controller.Page);
            store.Publish(languageState, previousLanguage ?? "auto"); shell.Renderer.TextLocalizer = previousLocalizer;
            shell.Renderer.ColorScheme = previousScheme; shell.Renderer.ReducedMotion = previousMotion;
            if (shell.Tree.IsAlive(previousFocus)) shell.Renderer.Focus(previousFocus, showIndicator: false);
            Directory.Delete(directory, recursive: true);
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }

    private sealed class SettingsContentValidationObserver : IXsrDispatchObserver { public void OnCompleted(XsrDispatchObservation observation) { } }
    private sealed class SettingsContentValidationFixture
    {
        internal const int ModCount = 1000;
        internal string InitialInstance { get; }
        internal string SecondInstance { get; }
        internal string Instance { get; set; }
        internal const string ScreenshotName = "preview.png";
        internal const string UnsafeScreenshotName = "literal {data caption} screenshot.png";
        internal string UnsafePath { get; }
        internal PngImage Image { get; }
        internal long ModifiedTicks { get; }
        internal List<InstanceScreenshotQuery> Reads { get; } = [];
        internal bool DeferReads { get; set; }
        internal TaskCompletionSource<XsrResult<InstanceScreenshot>>? Pending { get; private set; }
        internal CancellationToken PendingToken { get; private set; }
        internal int Copies { get; set; }
        internal int Crops { get; set; }
        private string ImagePath { get; }
        private IReadOnlyList<InstanceContentEntry> Mods { get; }
        private IReadOnlyList<InstanceContentEntry> Screenshots { get; }
        internal SettingsContentValidationFixture(string directory)
        {
            InitialInstance = Path.Combine(directory, "instance-a"); SecondInstance = Path.Combine(directory, "instance-b"); Instance = InitialInstance;
            string screenshots = Path.Combine(InitialInstance, "screenshots"); Directory.CreateDirectory(screenshots);
            ImagePath = Path.Combine(screenshots, ScreenshotName); UnsafePath = Path.Combine(screenshots, UnsafeScreenshotName);
            byte[] bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6xN8AAAAASUVORK5CYII=");
            File.WriteAllBytes(ImagePath, bytes); File.WriteAllBytes(UnsafePath, bytes); ModifiedTicks = File.GetLastWriteTimeUtc(ImagePath).Ticks;
            File.SetLastWriteTimeUtc(UnsafePath, new DateTime(ModifiedTicks, DateTimeKind.Utc));
            Image = PngImage.TryCreatePreview(bytes) ?? throw new InvalidOperationException("The bounded PNG validation fixture was invalid.");
            Mods = Enumerable.Range(0, ModCount).Select(index => new InstanceContentEntry("fixture-" + index.ToString("D4", CultureInfo.InvariantCulture) + ".jar", false, 1024 + index)
            { ModifiedUtcTicks = ModifiedTicks + index, DisplayName = "Fixture mod " + index.ToString("D4", CultureInfo.InvariantCulture) + " {state private}", Enabled = true, Version = "1", Icon = Image }).ToArray();
            Screenshots = new[] { ScreenshotName, UnsafeScreenshotName }.Select(name => new InstanceContentEntry(name, false, bytes.Length)
            { ModifiedUtcTicks = ModifiedTicks, Icon = Image, ImageWidth = Image.Width, ImageHeight = Image.Height }).ToArray();
        }
        internal void RequireInstance(string? instance)
        { if (instance != InitialInstance && instance != SecondInstance) throw new InvalidOperationException("The controlled settings query escaped its exact instance scope."); }
        internal InstanceManagementSnapshot Snapshot(string instance)
        {
            RequireInstance(instance);
            return new(instance, instance, "1.21.1", [], [new("overview", "Overview"), new("game", "Game settings"), new("mods", "Mods", Path.Combine(instance, "mods")), new("screenshots", "Screenshots", Path.Combine(instance, "screenshots"))], true, "")
            { Contents = [new("mods", Mods, true, null), new("screenshots", Screenshots, true, null)] };
        }
        internal void RequireFile(InstanceScreenshotQuery query)
        {
            RequireInstance(query.InstanceDirectory);
            if (query.InstanceDirectory != InitialInstance || query.Name != ScreenshotName && query.Name != UnsafeScreenshotName
                || query.ExpectedSize != Image.Bytes.Length || query.ExpectedModifiedUtcTicks != ModifiedTicks)
                throw new InvalidOperationException("The screenshot fixture received an obsolete or altered typed file identity.");
        }
        internal ValueTask<XsrResult<InstanceScreenshot>> ReadScreenshot(InstanceScreenshotQuery query, CancellationToken token)
        {
            RequireFile(query); Reads.Add(query);
            if (!DeferReads) return new(XsrResult.Success(new InstanceScreenshot(query.Name == ScreenshotName ? ImagePath : UnsafePath, Image)));
            PendingToken = token; Pending ??= new(TaskCreationOptions.RunContinuationsAsynchronously); return new(Pending.Task);
        }
    }
}
