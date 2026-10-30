using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static readonly int[] SidecarPreviewWidths = [1040, 800];
    private static async Task SidecarUiPreview(string source, string output)
    {
        static byte[][] Read(string path)
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            List<byte[]> result = [];
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                int length = reader.ReadInt32();
                if (length is < 1 or > 1048576) throw new InvalidDataException();
                result.Add(reader.ReadBytes(length));
            }
            return result.ToArray();
        }
        var declarations = Read(Path.Combine(source, "declarations.bin")).Select(bytes => SidecarRegistration.DecodeItem(bytes)).ToArray();
        var states = Read(Path.Combine(source, "states.bin")).Select(bytes => SidecarStateSnapshot.DecodeItem(bytes)).ToArray();
        Directory.CreateDirectory(output);
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
            new AvaloniaUiStartupAppearance(XsrUiThemeMode.Light, ReducedMotion: true));
        await startup.InvokeAsync(async () =>
        {
            using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
            var shell = fixture.Shell;
            shell.Renderer.ReducedMotion = true;
            var adapter = new DesktopSidecarNxml(fixture.Store);
            List<string> warnings = []; string? copied = null; Uri? opened = null;
            using var edge = adapter.Attach(shell, fixture.Intents, fixture.Store, warnings.Add, shell.Stage.Navigation.Current,
                openLink: uri => opened = uri, copyText: text => { copied = text; return Task.CompletedTask; });
            var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: declarations,
                features: SidecarFeatures.All, initialState: states, nxml: adapter);
            using (session) using (peer)
            {
                var window = new Window { Width = 1040, Height = 760, ShowInTaskbar = false };
                window.Show();
                try
                {
                    await Capture("onboarding");
                    Set("nsc.device.code", "ABCD-EFGH"); Set("nsc.device.waiting", "true"); Set("nsc.device.idle", "false");
                    await Capture("onboarding-device-code");
                    Set("nsc.device.code", ""); Set("nsc.device.waiting", "false"); Set("nsc.device.idle", "true");
                    await Task.Delay(350);
                    Activate("ui.sidecar.onboarding.continue");
                    Activate("ui.sidecar.page.open");
                    await Capture("account-signed-out");
                    Set("nsc.device.code", "ABCD-EFGH"); Set("nsc.device.waiting", "true"); Set("nsc.device.idle", "false");
                    Set("nsc.auth.message", ""); Set("nsc.auth.message.visible", "false");
                    await Capture("account-device-code");
                    Set("nsc.account.signed_in", "true"); Set("nsc.account.name", "Nexa 玩家"); Set("nsc.account.email", "player@example.test");
                    Set("nsc.account.section.ready", "true");
                    Set("nsc.account.level.title", "Lv3 · 账户 Lv3"); Set("nsc.account.level.experience", "6200 Exp");
                    Set("nsc.account.level.next", "距离 Lv4 还需 3800 Exp · 累计目标 10000 Exp");
                    Set("nsc.account.level.activity", "今日经验 42 Exp · 连续启动 7 天 · 最长 12 天\nMinecraft 36.5 小时 · 启动器 48.0 小时");
                    for (int i = 0; i < 7; i++)
                    { Set($"nsc.account.level.badge.{i}.visible", "true"); Set($"nsc.account.level.badge.{i}.text", $"社区铭牌 {i + 1} · 未获得\n参与社区活动以解锁"); }
                    await Capture("account-overview");
                    Activate("ui.sidecar.page.open", "#NexaMcMarketPage");
                    Set("nsc.market.ready", "true"); Set("nsc.market.home", "true");
                    await Capture("market-home");
                    Set("nsc.market.home", "false"); Set("nsc.market.list", "true"); Set("nsc.market.title", "主题");
                    Set("nsc.market.home.selected", "false"); Set("nsc.market.theme.selected", "true");
                    for (int i = 0; i < 10; i++)
                    { Set($"nsc.market.item.{i}.visible", "true"); Set($"nsc.market.item.{i}.name", $"社区主题 {i + 1}"); Set($"nsc.market.item.{i}.summary", "简洁的配色与舒适的阅读层级，让启动器更像你。"); }
                    await Capture("market-list");
                    Set("nsc.market.list", "false"); Set("nsc.market.detail.visible", "true");
                    Set("nsc.market.title", "真正的主题标题"); Set("nsc.market.subtitle", "这里是摘要，不是标题");
                    Set("nsc.market.description", "主题正文");
                    await Capture("market-detail");
                    if (warnings.Count > 0) throw new InvalidOperationException(string.Join("\n", warnings));
                    Console.WriteLine("PASS: offscreen Sidecar UI; raw touch copies device codes, explicitly opens the unfilled device page, and drags account/market lists at both widths and themes.");
                }
                finally { window.Close(); }
                void Set(string name, string value)
                { var store = session.Mirror!.Store; store.Publish(store.Resolve(XsrSemanticId.Parse(name)), value); }
                void Activate(string command, string? argument = null)
                {
                    var scene = shell.Render(new(1040, 760));
                    var node = scene.Nodes.First(n => shell.Tree.GetComponent<XsrUiCommandBinding>(n.Entity)?.Command.Value == command
                        && (argument is null || shell.Renderer.ReadIntentArgument(n.Entity) == argument));
                    AssertTrue(shell.Renderer.Activate(node.Entity));
                }
                async Task Capture(string name)
                {
                    string? expected = name switch { "account-device-code" or "onboarding-device-code" => "ABCD-EFGH", "account-overview" => "6200 Exp", "market-list" => "社区主题 1", "market-detail" => "真正的主题标题", _ => null };
                    long until = Environment.TickCount64 + 10000;
                    while (expected is not null && !shell.Render(new(1040, 700)).Nodes.Any(n => n.Text == expected))
                    {
                        if (Environment.TickCount64 >= until) throw new InvalidOperationException("Preview state was not presented: " + name);
                        await Task.Delay(25);
                    }
                    await Task.Delay(250);
                    foreach (int width in SidecarPreviewWidths)
                        foreach (bool dark in WardrobePreviewThemes)
                        {
                            shell.Renderer.ColorScheme = new(dark);
                            using var surface = new AvaloniaUiSceneSurface(shell) { Width = width, Height = 700 };
                            window.Content = surface;
                            surface.Measure(new Size(width, 700)); surface.Arrange(new Rect(0, 0, width, 700));
                            for (int frame = 0; frame < 4; frame++) surface.CommitScene();
                            window.UpdateLayout(); surface.UpdateLayout();
                            await Task.Delay(100); // Allow the native compositor hit-test tree to observe the attached surface.
                            using var bitmap = new RenderTargetBitmap(new PixelSize(width, 700), new Vector(96, 96));
                            bitmap.Render(surface);
                            string stem = $"{name}-{width}-{(dark ? "dark" : "light")}";
                            bitmap.Save(Path.Combine(output, stem + ".png"), PngBitmapEncoderOptions.Default);
                            File.WriteAllLines(Path.Combine(output, stem + ".txt"), surface.Scene!.Nodes.Select(n => $"{shell.Tree.Name(n.Entity)} | {n.Rect} | {n.Text}"));
                            if (name == "account-overview")
                            {
                                var button = surface.Scene!.Nodes.Single(n => shell.Tree.GetComponent<XsrUiCommandBinding>(n.Entity)?.Command.Value == "nsc.account.section"
                                    && shell.Renderer.ReadIntentArgument(n.Entity) == "overview");
                                var caption = surface.Scene.Nodes.Single(n => n.Text == "账户概览" && shell.Tree.Parent(n.Entity) == button.Entity);
                                AssertTrue(caption.Rect.X >= button.Rect.X + 12);
                                AssertTrue(caption.Rect.X + caption.Rect.Width <= button.Rect.X + button.Rect.Width - 12);
                                AssertTrue(button.IsEnabled);
                            }
                            if (name is "market-list" or "market-detail")
                            {
                                var category = surface.Scene!.Nodes.Single(n => shell.Tree.GetComponent<XsrUiCommandBinding>(n.Entity)?.Command.Value == "nsc.market.navigate"
                                    && shell.Renderer.ReadIntentArgument(n.Entity) == "theme");
                                var track = shell.Tree.GetComponent<XsrUiSegmentedTrack>(shell.Tree.Parent(category.Entity))!;
                                AssertEqual(category.Entity, track.Selected);
                                AssertTrue(category.IsSelected);
                            }
                            if (name == "market-detail")
                            {
                                var heading = surface.Scene!.Nodes.Single(n => shell.Tree.Name(n.Entity) == "NscMarketDetailTitle");
                                AssertEqual("真正的主题标题", heading.Text!);
                                AssertTrue(surface.Scene.Nodes.Any(n => n.Text == "这里是摘要，不是标题"));
                            }
                            if (name is "account-device-code" or "onboarding-device-code")
                            {
                                var code = surface.Scene!.Nodes.Single(n => shell.Tree.Name(n.Entity) == "NexaMcDeviceCode");
                                AssertEqual("ABCD-EFGH", code.Text!);
                                AssertTrue(shell.Tree.GetComponent<XsrUiVisualStyle>(code.Entity)!.Foreground.Alpha > 0);
                                using var touch = new SidecarPreviewTouch(window, surface);
                                var button = surface.Scene.Nodes.Single(n => shell.Tree.Name(n.Entity) == "NexaMcCopyDeviceCode");
                                AssertTrue(copied is null);
                                touch.Tap(new(button.Rect.X + button.Rect.Width / 2, button.Rect.Y + button.Rect.Height / 2));
                                AssertEqual("ABCD-EFGH", copied!); copied = null;
                                AssertTrue(opened is null); // Obtaining/copying a code never launches a browser.
                                var link = surface.Scene.Nodes.Single(n => shell.Tree.GetComponent<XsrUiCommandBinding>(n.Entity)?.Command.Value == "ui.sidecar.link.open");
                                touch.Tap(new(link.Rect.X + link.Rect.Width / 2, link.Rect.Y + link.Rect.Height / 2));
                                AssertEqual("https://pcln.top/device", opened!.AbsoluteUri); opened = null;
                            }
                            if (name is "account-overview" or "market-list")
                            {
                                string key = name == "account-overview" ? "NexaMcAccountSectionScroll" : "NscMarketList";
                                var viewport = surface.Scene!.Nodes.Single(n => shell.Tree.Name(n.Entity) == key);
                                var scroll = shell.Tree.GetComponent<XsrUiScroll>(viewport.Entity)!;
                                AssertTrue(shell.Tree.GetComponent<XsrUiScrollGesture>(viewport.Entity) is not null);
                                double before = scroll.OffsetY;
                                using var touch = new SidecarPreviewTouch(window, surface);
                                touch.Drag(new(viewport.Rect.X + viewport.Rect.Width / 2, viewport.Rect.Y + viewport.Rect.Height * .8));
                                AssertTrue(scroll.OffsetY > before + 30);
                                scroll.OffsetY = 0;
                                shell.Tree.MarkDirty(viewport.Entity, XsrUiDirtyKinds.Layout);
                            }
                            window.Content = null;
                        }
                }
            }
        });
    }
    private sealed class SidecarPreviewTouch : IDisposable
    {
        private readonly Window _window;
        private readonly AvaloniaUiSceneSurface _surface;
        private readonly TouchDevice _device = NewDevice();
        private readonly IInputRoot _root;
        private ulong _time = 1000;
        private int _presses;
        internal SidecarPreviewTouch(Window window, AvaloniaUiSceneSurface surface)
        {
            _window = window; _surface = surface;
            IInputRoot? root = null;
            var probe = new Control { IsHitTestVisible = false };
            probe.AttachedToVisualTree += (_, args) => root = args.PresentationSource as IInputRoot;
            surface.Children.Add(probe); surface.Children.Remove(probe);
            _root = root ?? throw new InvalidOperationException("Missing native input root.");
            window.AddHandler(InputElement.PointerPressedEvent, Pressed, handledEventsToo: true);
        }
        internal void Tap(XsrUiPoint point)
        { int before = _presses; Send(RawPointerEventType.TouchBegin, point); AssertEqual(before + 1, _presses); Send(RawPointerEventType.TouchEnd, point); }
        internal void Drag(XsrUiPoint point)
        {
            Send(RawPointerEventType.TouchBegin, point);
            Send(RawPointerEventType.TouchUpdate, point with { Y = point.Y - 80 });
            Send(RawPointerEventType.TouchEnd, point with { Y = point.Y - 80 });
        }
        private void Send(RawPointerEventType type, XsrUiPoint point)
        {
            var native = _surface.TranslatePoint(new Point(point.X, point.Y), _window)!.Value;
            Input(_window.PlatformImpl!)!(NewEvent(_device, _time += 20, _root, type, native, RawInputModifiers.None, 1));
            Dispatcher.UIThread.RunJobs(); _surface.CommitScene();
        }
        private void Pressed(object? sender, PointerPressedEventArgs args)
        {
            if (args.Pointer.Type == PointerType.Touch) _presses++;
        }
        public void Dispose()
        { _window.RemoveHandler(InputElement.PointerPressedEvent, Pressed); CloseDevice(_device); }
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] private static extern TouchDevice NewDevice();
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)] private static extern RawTouchEventArgs NewEvent(IInputDevice device, ulong timestamp, IInputRoot root, RawPointerEventType type, Point position, RawInputModifiers modifiers, long id);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Input")] private static extern Action<RawInputEventArgs>? Input(ITopLevelImpl impl);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Dispose")] private static extern void CloseDevice(TouchDevice device);
    }

}
