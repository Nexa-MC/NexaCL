using Avalonia;
using Avalonia.Media.Imaging;
using Nexa.Desktop.Ui;
using Nexa.Services.Accounts;
using Nexa.Services.Composition;
using Nexa.Services.Setup;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static readonly bool[] WardrobePreviewThemes = [true, false];
    private static async Task RunWardrobeSetupPreview(string output)
    {
        Directory.CreateDirectory(output);
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], true,
            new AvaloniaUiStartupAppearance(XsrUiThemeMode.Dark, ReducedMotion: true));
        await startup.InvokeAsync(async () =>
        {
            using var fixture = LibraryFixture(LaunchProfileKind.Microsoft);
            File.WriteAllText(Path.Combine(output,"preview-data.txt"),"Actual Avalonia rendering of product controllers/templates. Skin/cape/profile identities and pending setup settings are read-only fixtures. No sign-in, account persistence, setup completion or game launch is performed.");
            foreach(bool dark in WardrobePreviewThemes)
            {
                fixture.Shell.Renderer.ColorScheme=new(dark);string theme=dark?"dark":"light";
                Emit(fixture.Intents,"ui.account.add");fixture.Shell.Render(new(1000,640));
                await Capture(fixture.Shell,"account-providers-"+theme);
                Emit(fixture.Intents,"ui.account.third-party");fixture.Shell.Render(new(1000,640));
                fixture.Shell.Renderer.SetTextInputValue(FindEntity(fixture.Shell,"AuthUsername"),"literal user");
                fixture.Shell.Renderer.SetTextInputValue(FindEntity(fixture.Shell,"AuthServer"),"https://example.invalid/auth");
                await Capture(fixture.Shell,"account-third-party-"+theme);
                await Capture(fixture.Shell,"account-third-party-compact-"+theme,800,600);
                Emit(fixture.Intents,"ui.account.close");fixture.Shell.Render(new(1000,640));
            }
            var queries = new XsrQueryRouterBuilder();
            var current = new AccountWardrobeResolvedTextures(null, null, true, null, null);
            queries.Register<AccountWardrobeQuery, AccountWardrobeSnapshot>(AccountWardrobeContract.Read, (_, _) =>
                ValueTask.FromResult(XsrResult.Success(WardrobeDesktopSnapshot(fixture, "原版披风") with
                {
                    Current = current,
                    Skins = Enumerable.Range(0, 6).Select(i => new AccountWardrobeCard("skin" + i,
                        "MUXUE1230", "其他档案", current with { IsSlim = i % 2 == 0 }, true)).ToArray()
                })));
            using var wardrobe = new WardrobePageController(fixture.Shell, fixture.Intents,
                queries.Build(new NoopDispatchObserver()), new XsrCommandRouterBuilder().Build(new NoopDispatchObserver()),
                fixture.Store, fixture.Feedback);
            wardrobe.ConfigureFilePicker(_ => Task.FromResult<string?>(null)); wardrobe.ConfigureLibrary(() => { });
            fixture.Shell.Stage.Navigation.Replace(wardrobe.Page);
            foreach (bool dark in WardrobePreviewThemes)
            {
                fixture.Shell.Renderer.ColorScheme = new(dark);
                await Capture(fixture.Shell, "wardrobe-" + (dark ? "dark" : "light"));
                await Capture(fixture.Shell, "wardrobe-compact-" + (dark ? "dark" : "light"), 800, 600);
            }
            fixture.Shell.Stage.Navigation.Replace(wardrobe.Page);
            WardrobeEmit(fixture,"ui.wardrobe.browse","WardrobeBrowse");fixture.Shell.Render(new(1000,640));
            foreach(bool dark in WardrobePreviewThemes)
            {
                fixture.Shell.Renderer.ColorScheme=new(dark);
                await Capture(fixture.Shell,"wardrobe-local-"+(dark?"dark":"light"),800,600);
            }
            WardrobeEmit(fixture,"ui.wardrobe.local.close","WardrobeLocalClose");fixture.Shell.Render(new(1000,640));
            var publicQueries=LibraryQueries(fixture);
            publicQueries.Register<WardrobeCatalogQuery,WardrobeCatalogPage>(WardrobeCatalogContract.Read,(query,_)=>
                ValueTask.FromResult(XsrResult.Success(LibraryPage(query,Enumerable.Range(1,12).Select(i=>LibraryItem(i,WardrobeCatalogKind.Skin)).ToArray()))));
            using var publicLibrary=new WardrobeLibraryPageController(fixture.Shell,fixture.Intents,publicQueries.Build(new NoopDispatchObserver()),
                new XsrCommandRouterBuilder().Build(new NoopDispatchObserver()),fixture.Store,fixture.Feedback);
            fixture.Shell.Stage.Navigation.Replace(publicLibrary.Page);
            foreach(bool dark in WardrobePreviewThemes)
            {
                fixture.Shell.Renderer.ColorScheme=new(dark);string theme=dark?"dark":"light";
                await Capture(fixture.Shell,"wardrobe-library-"+theme);
                await Capture(fixture.Shell,"wardrobe-library-compact-"+theme,800,600);
            }
            fixture.Shell.Stage.Navigation.Replace(wardrobe.Page);fixture.Shell.Render(new(1000,640));
            publicLibrary.Dispose();
            wardrobe.Dispose();
            foreach (bool dark in WardrobePreviewThemes)
            {
                fixture.Shell.Renderer.ColorScheme = new(dark);
                string theme = dark ? "dark" : "light";
                Emit(fixture.Intents, "ui.navigation.launch");
                await Capture(fixture.Shell, "launch-" + theme, 800, 600);
                Emit(fixture.Intents, "ui.navigation.download");
                await Capture(fixture.Shell, "install-" + theme, 800, 600);
                Emit(fixture.Intents, "ui.install.java");
                await Capture(fixture.Shell, "java-install-" + theme, 800, 600);
            }
            var service = new FirstRunService(Path.Combine(output, "setup-data"), Path.Combine(output, "locator.json"));
            using var setup = new FirstRunController(fixture.Shell, fixture.Intents, fixture.Store,
                FirstRunRuntimeComposer.Compose(service), service.Read(), () => Task.FromResult<string?>(null), () => { });
            for (int step = 0; step < 4; step++)
            {
                foreach (bool dark in WardrobePreviewThemes)
                {
                    fixture.Shell.Renderer.ColorScheme = new(dark);
                    await Capture(fixture.Shell, "setup-" + step + "-" + (dark ? "dark" : "light"));
                    await Capture(fixture.Shell, "setup-compact-" + step + "-" + (dark ? "dark" : "light"), 800, 600);
                }
                if (step < 3) { Emit(fixture.Intents, "ui.setup.next"); fixture.Shell.Render(new(1000, 640)); }
            }
            setup.Dispose();
            using var required=new FirstRunController(fixture.Shell,fixture.Intents,fixture.Store,
                FirstRunRuntimeComposer.Compose(service),service.Read() with {TelemetryRequired=true},()=>Task.FromResult<string?>(null),()=>{});
            Emit(fixture.Intents,"ui.setup.next");fixture.Shell.Render(new(1000,640));
            Emit(fixture.Intents,"ui.setup.next");fixture.Shell.Render(new(1000,640));
            foreach(bool dark in WardrobePreviewThemes)
            {
                fixture.Shell.Renderer.ColorScheme=new(dark);
                await Capture(fixture.Shell,"setup-required-"+(dark?"dark":"light"));
                await Capture(fixture.Shell,"setup-required-compact-"+(dark?"dark":"light"),800,600);
            }
            async Task Capture(XsrUiShell shell, string name, int width = 1000, int height = 640)
            {
                shell.Renderer.ReducedMotion = true;
                using var surface = new AvaloniaUiSceneSurface(shell);
                var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = surface, ShowInTaskbar = false };
                window.Show();
                await Task.Delay(100);
                surface.Width = width; surface.Height = height;
                surface.Measure(new Size(width, height)); surface.Arrange(new Rect(0, 0, width, height));
                for (int frame = 0; frame < 12; frame++) surface.CommitScene();
                window.UpdateLayout(); surface.UpdateLayout();
                await Task.Delay(100);
                using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
                bitmap.Render(surface); bitmap.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
                window.Close();
            }
        });
    }
}
