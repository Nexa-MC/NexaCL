using Nexa.Desktop.Ui;
using Nexa.Services.Composition;
using Nexa.Services.Settings;
using Nexa.Services.Themes;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ThemeLifecycleInvalidPreparationReleasesCandidateAndPreservesLastKnownGood()
    {
        using var fixture = new ThemeLifecycleFixture();
        string invalid = fixture.MakeTheme("invalid", ".never { Command: ui.untrusted; }");
        var prior = fixture.Session.Snapshot.Current.Selection!;
        var result = fixture.Run(fixture.Session.ImportAsync(invalid, ThemeSourceKind.Directory, false, CancellationToken.None));
        AssertFalse(result.IsSuccess); AssertFalse(fixture.Session.Snapshot.Busy);
        AssertEqual(prior.SourceIdentity, fixture.Session.Snapshot.PresentedSourceIdentity);
        AssertEqual(prior.Generation, fixture.Current().Selection!.Generation);
        AssertFalse(fixture.Session.Snapshot.Catalogue.Themes.Any(theme => theme.Manifest.Id == "fixture.invalid"));
        string fallback = fixture.MakeTheme("fallback", ".never{BackgroundImage:asset(\"primary.svg\");}");
        SetFallbackAssets(fallback, validFallback: true);
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(fallback, ThemeSourceKind.Directory, false, CancellationToken.None)).IsSuccess);
        AssertEqual("asset(\"fallback.svg\")", fixture.Appearance.Current!.BackgroundResourceId);
        AssertEqual(1, fixture.LeaseCount);
        string source = fixture.Current().LastKnownGood!.SourceIdentity;
        string broken = fixture.MakeTheme("broken-fallback", "");
        SetFallbackAssets(broken, validFallback: false);
        AssertFalse(fixture.Run(fixture.Session.ImportAsync(broken, ThemeSourceKind.Directory, false, CancellationToken.None)).IsSuccess);
        AssertEqual(source, fixture.Current().LastKnownGood!.SourceIdentity);
        AssertEqual(source, fixture.Session.Snapshot.PresentedSourceIdentity);
        AssertEqual("asset(\"fallback.svg\")", fixture.Appearance.Current!.BackgroundResourceId);
        AssertEqual(1, fixture.LeaseCount);
        fixture.Session.WaitForAppearanceConsumers = (_, _) => throw new ObjectDisposedException("media consumer");
        var disposal = fixture.Session.DisposeAsync().AsTask();
        fixture.Pump(() => disposal.IsCompleted); disposal.GetAwaiter().GetResult();
        AssertEqual(0, fixture.LeaseCount);

        static void SetFallbackAssets(string path, bool validFallback)
        {
            string manifest = Path.Combine(path, "theme.json");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"appearance\":{\"resources\":{}}", """
                "appearance":{"resources":{"background":{"expression":"asset(\"primary.svg\")","kind":"Image","required":false,"fallback":"asset(\"fallback.svg\")"}}}
                """, StringComparison.Ordinal));
            const string unsupported = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><text>Unsupported vector text</text></svg>""";
            const string admitted = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><rect width="16" height="16" fill="#123456"/></svg>""";
            File.WriteAllText(Path.Combine(path, "primary.svg"), unsupported);
            File.WriteAllText(Path.Combine(path, "fallback.svg"), validFallback ? admitted : unsupported);
        }
    }

    private static void ThemeLifecycleSupersededPreviewCannotWritePersistentSelection()
    {
        using var fixture = new ThemeLifecycleFixture();
        string first = fixture.MakeTheme("first", "Text#TitleMain { Foreground: #123456; }");
        string second = fixture.MakeTheme("second", "Text#TitleMain { Foreground: #654321; }");
        long blockedGeneration = fixture.Appearance.Current!.Generation + 1;
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Session.WaitForAppearanceConsumers = (generation, token) =>
        {
            if (generation != blockedGeneration) return Task.CompletedTask;
            admitted.TrySetResult(); return Task.Delay(Timeout.Infinite, token);
        };
        var pending = fixture.Session.ImportAsync(first, ThemeSourceKind.Directory, false, CancellationToken.None);
        fixture.Pump(() => admitted.Task.IsCompleted);
        AssertEqual("builtin", fixture.Current().Selection!.SourceIdentity);
        var latest = fixture.Session.ImportAsync(second, ThemeSourceKind.Directory, false, CancellationToken.None);
        AssertFalse(fixture.Run(pending).IsSuccess); AssertTrue(fixture.Run(latest).IsSuccess);
        AssertFalse(fixture.Session.Snapshot.Busy);
        AssertEqual("fixture.second", fixture.Current().Selection!.ThemeId);
        AssertEqual(fixture.Current().Selection!.SourceIdentity, fixture.Session.Snapshot.PresentedSourceIdentity);
    }

    private static void ThemeLifecyclePreviewRestoresCommittedSnapshotAndRefreshesDarkMode()
    {
        using var fixture = new ThemeLifecycleFixture();
        string theme = fixture.MakeTheme("preview", "@when mode = dark { Text#TitleMain { Foreground: #010203; } }");
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(theme, ThemeSourceKind.Directory, true, CancellationToken.None)).IsSuccess);
        AssertTrue(fixture.Session.Snapshot.IsPreview); AssertEqual("builtin", fixture.Current().Selection!.SourceIdentity);
        long before = fixture.Appearance.Current!.Generation;
        fixture.Fixture.Store.Publish(fixture.Fixture.Store.Resolve(XsrSemanticId.Parse("UiDarkMode")), 1);
        fixture.Pump(() => fixture.Appearance.Current!.Generation > before && fixture.Session.Snapshot.IsDark);
        AssertTrue(fixture.Session.Snapshot.IsPreview); AssertEqual("builtin", fixture.Current().Selection!.SourceIdentity);
        AssertTrue(fixture.Run(fixture.Session.EndPreviewAsync(CancellationToken.None)).IsSuccess);
        AssertFalse(fixture.Session.Snapshot.IsPreview); AssertEqual("builtin", fixture.Session.Snapshot.PresentedSourceIdentity);
        AssertEqual(fixture.Current().Selection!.SourceIdentity, fixture.Session.Snapshot.PresentedSourceIdentity);
    }

    private static void ThemeLifecycleMirrorFailureCannotRollbackDurableCommit()
    {
        using var fixture = new ThemeLifecycleFixture(failMirror: true);
        string theme = fixture.MakeTheme("durable", "Text#TitleMain { FontSize: 24; }");
        var result = fixture.Run(fixture.Session.ImportAsync(theme, ThemeSourceKind.Directory, false, CancellationToken.None));
        AssertTrue(result.IsSuccess); AssertFalse(fixture.Session.Snapshot.Busy);
        AssertEqual("fixture.durable", fixture.Current().LastKnownGood!.ThemeId);
        AssertEqual(fixture.Current().Selection!.SourceIdentity, fixture.Session.Snapshot.PresentedSourceIdentity);
        AssertTrue(fixture.Log.Any(value => value.Contains("mirror", StringComparison.OrdinalIgnoreCase)));
        long generation = fixture.Appearance.Current!.Generation;
        fixture.Fixture.Store.Publish(fixture.Fixture.Store.Resolve(XsrSemanticId.Parse("UiDarkMode")), 1);
        fixture.Pump(() => fixture.Appearance.Current!.Generation > generation && !fixture.Session.Snapshot.Busy && fixture.Session.Snapshot.IsDark);
        AssertEqual("fixture.durable", fixture.Current().LastKnownGood!.ThemeId);
        AssertEqual(fixture.Current().Selection!.SourceIdentity, fixture.Session.Snapshot.PresentedSourceIdentity);
        generation = fixture.Appearance.Current!.Generation;
        AssertTrue(fixture.Run(fixture.Session.SetMediaVolumeAsync(.37, CancellationToken.None)).IsSuccess);
        fixture.Pump(() => fixture.Appearance.Current!.Generation > generation && !fixture.Session.Snapshot.Busy);
        AssertEqual("37", fixture.Appearance.Current!.Settings.Values.Single(value => value.Key == "music.volume").Value.Value);
        AssertEqual("fixture.durable", fixture.Current().LastKnownGood!.ThemeId);
    }

    private static void ThemeLifecycleSafeModeIsTemporaryAndBypassesLegacyResources()
    {
        using var fixture = new ThemeLifecycleFixture();
        string theme = fixture.MakeTheme("selected", "Text#TitleMain{Foreground:#102030;}");
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(theme, ThemeSourceKind.Directory, false, CancellationToken.None)).IsSuccess);
        var selection = fixture.Current().Selection!;
        string untrusted = Path.Combine(fixture.Fixture.TemporaryDirectory, "invalid-legacy-image.png");
        File.WriteAllText(untrusted, "not an image; safe mode must never prepare it");
        AssertTrue(fixture.Fixture.Foundation.Host.SettingsPolicy.Set(new("appearance.background-path", SettingsLayer.Global,
            new(SettingsOverrideMode.Custom, untrusted))).IsSuccess);
        fixture.Restart(safeMode: true);
        AssertEqual("builtin", fixture.Session.Snapshot.PresentedSourceIdentity);
        AssertEqual(selection.SourceIdentity, fixture.Current().Selection!.SourceIdentity);
        AssertEqual(selection.Generation, fixture.Current().Selection!.Generation);
        AssertFalse(fixture.Run(fixture.Session.SaveOptionsAsync(new(selection.SourceIdentity,
            new Dictionary<string, ThemeOptionValue>(), fixture.Current().Revision), CancellationToken.None)).IsSuccess);
        AssertEqual("builtin", fixture.Session.Snapshot.PresentedSourceIdentity);
        AssertEqual(selection.Generation, fixture.Current().Selection!.Generation);
        AssertEqual(0, fixture.Appearance.Current!.AudioPlaylist.Count);
        AssertNull(fixture.Appearance.Current!.Logo); AssertNull(fixture.Appearance.Current!.Background);
        AssertFalse(fixture.Log.Any(value => value.Contains("Legacy built-in resource", StringComparison.Ordinal)));
        fixture.Restart(safeMode: false);
        AssertEqual(selection.SourceIdentity, fixture.Session.Snapshot.PresentedSourceIdentity);
        AssertEqual(selection.SourceIdentity, fixture.Current().Selection!.SourceIdentity);
    }

    private static void ThemeLifecycleFutureChildStyleFailureRestoresBuiltInWithoutLosingDraft()
    {
        using var fixture = new ThemeLifecycleFixture();
        string theme = fixture.MakeTheme("future-grid", ".future-grid{GridColumns:1fr;}.future-cell{GridColumn:2;}");
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(theme, ThemeSourceKind.Directory, false, CancellationToken.None)).IsSuccess);
        long generation = fixture.Appearance.Current!.Generation;
        var tree = fixture.Fixture.Shell.Tree;
        var parent = tree.Create("future-grid");
        tree.SetComponent(parent, new XsrUiElement { Width = 240, Height = 80 });
        tree.SetComponent(parent, new XsrUiStackPanel(XsrUiOrientation.Vertical));
        tree.SetComponent(parent, new XsrUiStyleIdentity("Node", "future-grid", ["future-grid"]));
        var child = tree.Create("future-cell");
        var source = new XsrUiElement { Width = 120, Height = 36 };
        tree.SetComponent(child, source);
        tree.SetComponent(child, new XsrUiTextInput());
        tree.SetComponent(child, new XsrUiInput { Focusable = true, Clickable = true });
        tree.SetComponent(child, new XsrUiStyleIdentity("TextInput", "future-cell", ["future-cell"]));
        tree.Attach(child, parent); tree.Attach(parent, fixture.Fixture.Shell.Content);
        fixture.Fixture.Shell.Renderer.SetTextInputValue(child, "host draft survives theme fault");
        fixture.Fixture.Shell.Render(new(1000, 700));
        AssertTrue(fixture.Appearance.Current!.Generation > generation);
        AssertEqual("builtin", fixture.Session.Snapshot.PresentedSourceIdentity);
        AssertEqual("host draft survives theme fault", tree.GetComponent<XsrUiTextInput>(child)!.ReadDraft());
        AssertTrue(ReferenceEquals(source, tree.GetComponent<XsrUiElement>(child)));
        AssertEqual(120d, source.Width!.Value);
        fixture.Pump(() => fixture.Current().Selection!.SourceIdentity == "builtin" && !fixture.Session.Snapshot.Busy);
        AssertEqual("builtin", fixture.Current().LastKnownGood!.SourceIdentity);
        AssertEqual(0, fixture.LeaseCount);
        AssertTrue(fixture.Log.Any(value => value.Contains("product appearance restored", StringComparison.Ordinal)));
    }

    private sealed class ThemeLifecycleFixture : IDisposable
    {
        internal LaunchPageFixture Fixture { get; } = new(new ImmediateInstanceSource([]));
        internal DesktopEffectiveAppearanceSource Appearance { get; } = new();
        internal AvaloniaUiPlatformActions Platform { get; } = new();
        internal DesktopThemeSession Session { get; private set; }
        private readonly XsrQueryRouter _queries;
        private readonly XsrCommandRouter _commands;
        internal ThemePackageService Service { get; }
        internal List<string> Log { get; } = [];
        internal ThemeLifecycleFixture(bool failMirror = false)
        {
            var queries = WorldFixtureQueries(Fixture);
            var commands = new XsrCommandRouterBuilder();
            AssertTrue(Fixture.Foundation.Commands.TryResolve(SettingsPolicyContract.SetCommand, out var set));
            commands.Register<SettingsMutation>(SettingsPolicyContract.SetCommand, (mutation, token) =>
                failMirror && mutation.Key == "appearance.theme-pack-id" && mutation.Value.Value != "builtin"
                    ? ValueTask.FromException<XsrResult>(new IOException("fixture mirror persistence failure"))
                    : new(Fixture.Foundation.Commands.Dispatch(set, mutation, cancellationToken: token).Completion));
            Service = ThemesRuntime.Register(queries, commands, Fixture.TemporaryDirectory,
                Path.Combine(AppContext.BaseDirectory, "Themes", "BuiltIn"), DesktopThemeSession.SupportedCapabilities());
            _queries = queries.Build(new NoopDispatchObserver()); _commands = commands.Build(new NoopDispatchObserver());
            Session = new(_queries, _commands, Fixture.Store, Fixture.Shell, Platform, Appearance, value => { lock (Log) Log.Add(value); });
            Pump(() => Session.InitialReady.IsCompleted); Session.InitialReady.GetAwaiter().GetResult();
        }
        internal void Restart(bool safeMode)
        {
            Session.WaitForAppearanceConsumers = null;
            var stop = Session.DisposeAsync().AsTask(); Pump(() => stop.IsCompleted); stop.GetAwaiter().GetResult();
            Session = new(_queries, _commands, Fixture.Store, Fixture.Shell, Platform, Appearance,
                value => { lock (Log) Log.Add(value); }, safeMode);
            Pump(() => Session.InitialReady.IsCompleted); Session.InitialReady.GetAwaiter().GetResult();
        }
        internal string MakeTheme(string name, string styles)
        {
            string path = Path.Combine(Fixture.TemporaryDirectory, "author-" + name); Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "mimetype"), "application/x-nexa-theme");
            File.WriteAllText(Path.Combine(path, "theme.json"), """
                {"format":"NTP/1","id":"fixture.NAME","version":"1.0.0","name":{"en-US":"Fixture"},
                "styleApi":1,"nss":"NSS/1","styles":["main.nss"],"requiredCapabilities":[],"optionalCapabilities":[],"options":[],"appearance":{"resources":{}}}
                """.Replace("NAME", name, StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(path, "main.nss"), styles); return path;
        }
        internal ThemeCurrentSnapshot Current() => Service.CurrentAsync(new()).AsTask().GetAwaiter().GetResult().Value!;
        internal int LeaseCount => Directory.EnumerateDirectories(Path.Combine(Fixture.TemporaryDirectory, "themes", "leases")).Count();
        internal void Pump(Func<bool> ready)
        {
            AssertTrue(SpinWait.SpinUntil(() => { Fixture.Shell.Render(new(1000, 700)); return ready(); }, TimeSpan.FromSeconds(10)));
            Fixture.Shell.Render(new(1000, 700));
        }
        internal T Run<T>(Task<T> task) { Pump(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
        public void Dispose()
        {
            Session.WaitForAppearanceConsumers = null;
            var dispose = Session.DisposeAsync().AsTask(); Pump(() => dispose.IsCompleted); dispose.GetAwaiter().GetResult();
            Service.Dispose(); Fixture.Dispose();
        }
    }
}
