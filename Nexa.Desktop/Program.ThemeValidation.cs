using System.Diagnostics;
using System.Text.Json;
using Nexa.Desktop.Ui;
using Nexa.Services.Themes;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Desktop;

internal static partial class Program
{
    private static async Task ValidateThemesAsync(XsrUiShell shell, DesktopThemeSession themes,
        SettingsPageController settings, DesktopUiIntentSink intents, XsrStateStore state)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        CancellationToken token = timeout.Token;
        var size = new XsrUiSize(1040, 740);
        var previousPage = shell.Stage.Navigation.Current;
        var previousViewport = shell.Renderer.Viewport;
        var previousFocus = shell.Renderer.Focused;
        bool previousMotion = shell.Renderer.ReducedMotion;
        var previousSession = settings.ThemeSession;
        string previousSection = settings.SelectedSection;
        string? previousIdentity = themes.Snapshot.Current.Selection?.SourceIdentity ?? themes.Snapshot.PresentedSourceIdentity;
        var originalSources = themes.Snapshot.Catalogue.Themes.Select(source => source.SourceIdentity).ToHashSet(StringComparer.Ordinal);
        string directory = Path.Combine(Path.GetTempPath(), "nexa-ntp-publication-" + Guid.NewGuid().ToString("N"));
        string minimalDirectory = Path.Combine(directory, "Minimal"), advancedDirectory = Path.Combine(directory, "Advanced");
        string? watchedIdentity = null;
        XsrUiEntityId argument = default;
        string? originalArgument = null;
        XsrUiTextInput? retainedInput = null;
        XsrUiScroll? retainedScroll = null;
        XsrUiCommandBinding? retainedBinding = null;
        XsrSemanticId retainedCommand = default;
        XsrUiSemanticRole retainedRole = default;
        int retainedCaret = 0;
        double retainedOffset = 0;
        bool hasMode = state.TryResolve(XsrSemanticId.Parse("UiDarkMode"), out var modeId);
        int modeValue = hasMode ? state.Read<int>(modeId).Value : 0;
        try
        {
            Directory.CreateDirectory(directory);
            CopySource("Minimal", minimalDirectory);
            CopySource("Advanced", advancedDirectory);
            string external = Path.Combine(directory, "explicit-user-selection.svg");
            File.Copy(Path.Combine(advancedDirectory, "assets", "background.svg"), external);
            shell.Renderer.ReducedMotion = true;
            if (!ReferenceEquals(settings.ThemeSession, themes)) settings.ThemeSession = themes;
            await Pump(settings.PrepareStartupAsync(token)).ConfigureAwait(false);
            shell.Stage.Navigation.Replace(settings.Page);
            shell.Render(size);
            await Select("game").ConfigureAwait(false);
            argument = Owned("SettingsArgument.game.arguments.0");
            var input = shell.Tree.GetComponent<XsrUiTextInput>(argument)!;
            retainedInput = input;
            originalArgument = input.ReadDraft();
            const string draft = "--ntp literal <argument> {state private}";
            shell.Renderer.SetTextInputValue(argument, draft);
            await Show(argument).ConfigureAwait(false);
            Check(shell.Renderer.Focus(argument, showIndicator: false) && shell.Renderer.EditText(XsrUiTextEdit.Left), "Cannot focus the actual business editor.");
            retainedCaret = input.SelectionEnd;
            var sections = Owned("SettingsSections");
            var scroll = shell.Tree.GetComponent<XsrUiScroll>(sections)!;
            retainedScroll = scroll; retainedOffset = scroll.OffsetY;
            var apply = Owned("SettingsEdit.game.arguments");
            retainedBinding = shell.Tree.GetComponent<XsrUiCommandBinding>(apply)!;
            retainedCommand = retainedBinding.Command;
            retainedRole = shell.Tree.GetComponent<XsrUiSemantic>(apply)!.Role;

            await Success(themes.ImportAsync(minimalDirectory, ThemeSourceKind.Directory, true, token)).ConfigureAwait(false);
            string minimal = SourceFor(minimalDirectory);
            Check(themes.Snapshot.IsPreview && themes.Snapshot.PresentedSourceIdentity == minimal
                && themes.Snapshot.Current.Selection?.SourceIdentity == previousIdentity, "Minimal preview rewrote the committed selection.");
            AssertEditor();
            long minimalGeneration = shell.Renderer.ThemeActivationGeneration;
            await Success(themes.ImportAsync(advancedDirectory, ThemeSourceKind.Directory, true, token)).ConfigureAwait(false);
            string advanced = SourceFor(advancedDirectory);
            Check(themes.Snapshot.IsPreview && themes.Snapshot.PresentedSourceIdentity == advanced
                && themes.Snapshot.Current.Selection?.SourceIdentity == previousIdentity
                && shell.Renderer.ThemeActivationGeneration > minimalGeneration, "Advanced preview did not retain a distinct activation generation.");
            AssertEditor();
            Check(shell.Renderer.ThemeStyleCacheCount > 0 && shell.Renderer.ThemeStyleComputeCount > 0,
                "The actual renderer did not compute theme presentation.");
            await Success(themes.EndPreviewAsync(token)).ConfigureAwait(false);
            Check(!themes.Snapshot.IsPreview && themes.Snapshot.PresentedSourceIdentity == previousIdentity,
                "Ending preview did not restore the committed theme.");
            Check(!themes.Snapshot.Catalogue.Themes.Any(source => source.SourceIdentity == minimal || source.SourceIdentity == advanced),
                "Cancelling newly imported previews retained a provisional installation.");
            AssertEditor();

            string previewMinimal = minimal, previewAdvanced = advanced;
            await Success(themes.ImportAsync(minimalDirectory, ThemeSourceKind.Directory, false, token)).ConfigureAwait(false);
            minimal = SourceFor(minimalDirectory);
            Check(minimal != previewMinimal && !themes.Snapshot.Current.Grants.Any(grant => grant.SourceIdentity == minimal),
                "A fresh committed import reused retired preview authority.");
            Check(themes.Snapshot.Current.Selection?.SourceIdentity == minimal
                && themes.Snapshot.Current.LastKnownGood?.SourceIdentity == minimal, "Effective activation was not durably committed.");
            AssertEditor();
            long optionRevision = themes.Snapshot.Current.Revision;
            var minimalValues = Values(minimal);
            minimalValues["accent"] = new(ThemeOptionKind.Color, "#73b8ff");
            minimalValues["title-size"] = new(ThemeOptionKind.Range, "27");
            await Success(themes.SaveOptionsAsync(new(minimal, minimalValues, optionRevision), token)).ConfigureAwait(false);
            Check(Options(minimal)!.Values["title-size"].Value == "27" && Options(minimal)!.Values["accent"].Value == "#73b8ff",
                "Valid parameters were not saved through the actual runtime.");
            AssertEditor();
            long savedRevision = SourceOptionsRevision(minimal), savedGeneration = shell.Renderer.ThemeActivationGeneration;
            await Failure(themes.SaveOptionsAsync(new(minimal, minimalValues, optionRevision), token), staleRevision: true).ConfigureAwait(false);
            Check(SourceOptionsRevision(minimal) == savedRevision && shell.Renderer.ThemeActivationGeneration == savedGeneration,
                "A stale option revision changed the current parameter/style snapshot.");
            var invalidNumber = new Dictionary<string, ThemeOptionValue>(minimalValues, StringComparer.Ordinal)
            { ["title-size"] = new(ThemeOptionKind.Range, "NaN") };
            await Failure(themes.SaveOptionsAsync(new(minimal, invalidNumber, themes.Snapshot.Current.Revision), token)).ConfigureAwait(false);
            Check(SourceOptionsRevision(minimal) == savedRevision && shell.Renderer.ThemeActivationGeneration == savedGeneration,
                "An invalid numeric option changed the last valid snapshot.");
            AssertEditor();

            string protectedDirectory = Path.Combine(directory, "Protected");
            CopySource("Minimal", protectedDirectory);
            File.WriteAllText(Path.Combine(protectedDirectory, "styles", "main.nss"), "Button { Command: ui.launch.primary; }");
            await Failure(themes.ImportAsync(protectedDirectory, ThemeSourceKind.Directory, false, token)).ConfigureAwait(false);
            Check(themes.Snapshot.Current.Selection?.SourceIdentity == minimal && shell.Renderer.ThemeActivationGeneration == savedGeneration,
                "A protected declaration changed the active business/style snapshot.");
            AssertEditor();
            string missingDirectory = Path.Combine(directory, "MissingAsset");
            CopySource("Minimal", missingDirectory);
            File.AppendAllText(Path.Combine(missingDirectory, "styles", "main.nss"), "\nContentHost { BackgroundImage: asset(assets/missing.svg); }\n");
            await Failure(themes.ImportAsync(missingDirectory, ThemeSourceKind.Directory, false, token)).ConfigureAwait(false);
            Check(themes.Snapshot.Current.Selection?.SourceIdentity == minimal && shell.Renderer.ThemeActivationGeneration == savedGeneration,
                "A missing required style resource replaced the last valid theme.");
            AssertEditor();

            await Success(themes.ImportAsync(advancedDirectory, ThemeSourceKind.Directory, false, token)).ConfigureAwait(false);
            advanced = SourceFor(advancedDirectory);
            Check(advanced != previewAdvanced && !themes.Snapshot.Current.Grants.Any(grant => grant.SourceIdentity == advanced),
                "The Advanced import inherited a cancelled preview identity/grant.");
            var advancedValues = Values(advanced);
            advancedValues["background-path"] = new(ThemeOptionKind.File, external);
            long advancedRevision = SourceOptionsRevision(advanced), advancedGeneration = shell.Renderer.ThemeActivationGeneration;
            await Failure(themes.SaveOptionsAsync(new(advanced, advancedValues, themes.Snapshot.Current.Revision), token)).ConfigureAwait(false);
            Check(SourceOptionsRevision(advanced) == advancedRevision && shell.Renderer.ThemeActivationGeneration == advancedGeneration
                && !themes.Snapshot.Current.Grants.Any(grant => grant.SourceIdentity == advanced), "An ungranted path was read or saved.");
            await Success(themes.GrantAsync(new(advanced, ThemeResourceKind.Image, ThemeGrantScope.File, external), token)).ConfigureAwait(false);
            var grant = themes.Snapshot.Current.Grants.Single(value => value.SourceIdentity == advanced && value.CanonicalPath == external);
            await Success(themes.SaveOptionsAsync(new(advanced, advancedValues, themes.Snapshot.Current.Revision), token)).ConfigureAwait(false);
            Check(Options(advanced)!.Values["background-path"].Value == external && themes.Snapshot.Current.Selection?.SourceIdentity == advanced,
                "The authorized resource was not committed with its option snapshot.");
            AssertEditor();
            var exportedTask = themes.ExportOptionsAsync(new(advanced), token);
            await Pump(exportedTask).ConfigureAwait(false);
            var exported = await exportedTask.ConfigureAwait(false);
            Check(exported.IsSuccess, "Parameter export failed.");
            using (var document = JsonDocument.Parse(exported.Value!.Json))
                Check(!document.RootElement.TryGetProperty("grants", out _), "Parameter export conveyed external-read grants.");
            await Success(themes.ResetOptionsAsync(new(advanced, themes.Snapshot.Current.Revision), token)).ConfigureAwait(false);
            Check(Options(advanced)!.Values["background-path"].Value.Length == 0, "Reset did not restore the declared default.");
            await Success(themes.ImportOptionsAsync(new(advanced, exported.Value!.Json, themes.Snapshot.Current.Revision), token)).ConfigureAwait(false);
            Check(Options(advanced)!.Values["background-path"].Value == external, "The same-source parameter round trip lost its saved value.");
            await Success(themes.RevokeAsync(new(grant.GrantId), token)).ConfigureAwait(false);
            string builtin = themes.Snapshot.Catalogue.Themes.Single(source => source.SourceKind == ThemeSourceKind.BuiltIn).SourceIdentity;
            Check(!themes.Snapshot.Current.Grants.Any(value => value.GrantId == grant.GrantId)
                && themes.Snapshot.PresentedSourceIdentity == builtin, "Revoking active resources did not retire their use and recover the built-in theme.");
            long recoveredGeneration = shell.Renderer.ThemeActivationGeneration;
            await Failure(themes.ActivateAsync(advanced, false, token)).ConfigureAwait(false);
            Check(themes.Snapshot.PresentedSourceIdentity == builtin && shell.Renderer.ThemeActivationGeneration == recoveredGeneration,
                "A revoked path reattached during activation.");
            AssertEditor();

            await Success(themes.ActivateAsync(minimal, false, token)).ConfigureAwait(false);
            await Success(themes.WatchAsync(new(minimal, true), token)).ConfigureAwait(false);
            watchedIdentity = minimal;
            string stylePath = Path.Combine(minimalDirectory, "styles", "main.nss");
            string validStyle = File.ReadAllText(stylePath) + "\nButton.capsule { BorderWidth: 2; }\n";
            long beforeReload = shell.Renderer.ThemeActivationGeneration;
            File.WriteAllText(stylePath, validStyle);
            await Until(() => shell.Renderer.ThemeActivationGeneration > beforeReload && !themes.Snapshot.Busy).ConfigureAwait(false);
            AssertEditor();
            long beforeInvalidReload = shell.Renderer.ThemeActivationGeneration;
            File.WriteAllText(stylePath, "Button { Recipe: CommandInput; }");
            await Until(() => !string.IsNullOrEmpty(themes.Snapshot.Error) && !themes.Snapshot.Busy).ConfigureAwait(false);
            Check(shell.Renderer.ThemeActivationGeneration == beforeInvalidReload && themes.Snapshot.PresentedSourceIdentity == minimal,
                "An invalid development edit discarded the last valid style program.");
            File.WriteAllText(stylePath, validStyle + "\n/* repaired development edit */\n");
            await Until(() => shell.Renderer.ThemeActivationGeneration > beforeInvalidReload && themes.Snapshot.Error is null && !themes.Snapshot.Busy).ConfigureAwait(false);
            AssertEditor();
            await Success(themes.WatchAsync(new(minimal, false), token)).ConfigureAwait(false);
            watchedIdentity = null;
            await Success(themes.ActivateAsync(builtin, false, token)).ConfigureAwait(false);
            Check(themes.Snapshot.PresentedSourceIdentity == builtin && !themes.Snapshot.IsPreview, "Built-in recovery did not complete.");
            AssertEditor();
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            token = cleanupTimeout.Token;
            // This CLI may be pointed at an existing profile. Restore its selected
            // source and remove only installations created by this isolated probe.
            if (watchedIdentity is not null) await Success(themes.WatchAsync(new(watchedIdentity, false), token)).ConfigureAwait(false);
            await Success(themes.EndPreviewAsync(token)).ConfigureAwait(false);
            string restore = previousIdentity ?? themes.Snapshot.Catalogue.Themes.First(source => source.SourceKind == ThemeSourceKind.BuiltIn).SourceIdentity;
            await Success(themes.ActivateAsync(restore, false, token)).ConfigureAwait(false);
            await Success(themes.RefreshAsync(token)).ConfigureAwait(false);
            foreach (var source in themes.Snapshot.Catalogue.Themes.Where(source => !originalSources.Contains(source.SourceIdentity)).ToArray())
                await Success(themes.RemoveAsync(new(source.SourceIdentity), token)).ConfigureAwait(false);
            Check(themes.Snapshot.Current.Selection?.SourceIdentity == restore
                && themes.Snapshot.Catalogue.Themes.All(source => originalSources.Contains(source.SourceIdentity)),
                "The theme publication probe did not restore its original selection/catalogue.");
            if (argument.IsAssigned && shell.Tree.IsAlive(argument) && originalArgument is not null)
                shell.Renderer.SetTextInputValue(argument, originalArgument);
            if (!ReferenceEquals(settings.ThemeSession, previousSession)) settings.ThemeSession = previousSession;
            shell.Renderer.ReducedMotion = previousMotion;
            if (previousPage.IsAssigned && shell.Tree.IsAlive(previousPage)) shell.Stage.Navigation.Replace(previousPage);
            shell.Render(previousViewport.Width > 0 && previousViewport.Height > 0 ? previousViewport : size);
            if (previousPage == settings.Page && previousSection != settings.SelectedSection)
            {
                var navigation = OptionalOwned("SettingsNav." + previousSection);
                if (navigation.IsAssigned) { intents.Emit(XsrSemanticId.Parse("ui.settings.section"), navigation, XsrCorrelationId.Create()); shell.Render(size); }
            }
            if (previousFocus.IsAssigned && shell.Tree.IsAlive(previousFocus)) shell.Renderer.Focus(previousFocus, showIndicator: false);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine("NTP/1 themes validation passed: actual service/session/controller routes, shipped Minimal/Advanced sources, preview/commit/last-known-good, typed options and stale revisions, protected-property/resource failure rollback, source-bound grants and revoke, option round trip, valid/invalid/repaired directory reload, retained entity/command/role/draft/caret/focus/scroll, built-in recovery and original selection/catalogue restoration. Native SVG geometry/font shaping remain deferred in this headless smoke; the native preview gate executes those decoders.");

        async Task Pump(Task operation)
        {
            while (!operation.IsCompleted)
            {
                token.ThrowIfCancellationRequested(); shell.Render(size);
                await Task.Delay(5, token).ConfigureAwait(false);
            }
            await operation.WaitAsync(token).ConfigureAwait(false); shell.Render(size);
        }
        async Task Success(Task<XsrResult> operation)
        {
            await Pump(operation).ConfigureAwait(false);
            var result = await operation.ConfigureAwait(false);
            Check(result.IsSuccess, result.Error?.Message ?? "Theme operation failed.");
            await Settled().ConfigureAwait(false);
        }
        async Task Failure(Task<XsrResult> operation, bool staleRevision = false)
        {
            await Pump(operation).ConfigureAwait(false);
            var result = await operation.ConfigureAwait(false);
            Check(!result.IsSuccess, "An invalid theme operation succeeded.");
            bool rejectedAsStale = result.Error?.Message is
                "Theme options changed before candidate preparation." or
                "Theme options changed during candidate preparation." or
                "Theme options changed after candidate preparation." or
                "Theme candidate or persistent revision is stale." or
                "Theme option source or revision is stale.";
            Check(rejectedAsStale == staleRevision, staleRevision
                ? "The stale revision fixture did not reach the service CAS rejection."
                : "A fresh-CAS invalid-value/resource fixture was rejected as stale before its actual validation path.");
            await Settled().ConfigureAwait(false);
        }
        async Task Settled()
        {
            // A durable commit mirrors the selected source through real settings.
            // Serialize a refresh after it, then let queued policy work finish.
            // Do not retry a failed CAS request or accept a stale successful save.
            var barrier = themes.RefreshAsync(token);
            await Pump(barrier).ConfigureAwait(false);
            Check((await barrier.ConfigureAwait(false)).IsSuccess, "Theme snapshot barrier failed.");
            long revision = -1, generation = -1;
            long stableSince = Stopwatch.GetTimestamp();
            while (true)
            {
                token.ThrowIfCancellationRequested(); shell.Render(size);
                var snapshot = themes.Snapshot;
                long currentGeneration = shell.Renderer.ThemeActivationGeneration;
                if (snapshot.Busy || revision != snapshot.Current.Revision || generation != currentGeneration)
                {
                    revision = snapshot.Current.Revision; generation = currentGeneration;
                    stableSince = Stopwatch.GetTimestamp();
                }
                else if (Stopwatch.GetElapsedTime(stableSince) >= TimeSpan.FromMilliseconds(100)) return;
                await Task.Delay(5, token).ConfigureAwait(false);
            }
        }
        async Task Until(Func<bool> condition)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested(); shell.Render(size); if (condition()) return;
                await Task.Delay(10, token).ConfigureAwait(false);
            }
        }
        async Task Select(string section)
        {
            await Until(() => OptionalOwned("SettingsNav." + section).IsAssigned).ConfigureAwait(false);
            intents.Emit(XsrSemanticId.Parse("ui.settings.section"), Owned("SettingsNav." + section), XsrCorrelationId.Create());
            await Until(() => settings.SelectedSection == section && OptionalOwned("SettingsArgument.game.arguments.0").IsAssigned).ConfigureAwait(false);
        }
        async Task Show(XsrUiEntityId entity)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                var scene = shell.Render(size);
                var target = scene.Nodes.Where(node => node.Entity == entity).Select(node => (XsrUiSceneNode?)node).SingleOrDefault();
                if (target is { } node && node.Rect.Width > 0 && node.Rect.Height > 0 && node.Rect.Y >= 0 && node.Rect.Y < size.Height
                    && (node.ClipRect is null || node.ClipRect.Value.Contains(new(node.Rect.X + 5, node.Rect.Y + Math.Min(5, node.Rect.Height / 2))))) return;
                var owner = scene.Nodes.Single(node => node.Entity == Owned("SettingsSections"));
                double delta = target is { } above && above.Rect.Y < owner.Rect.Y ? -140 : 140;
                shell.Renderer.PointerScroll(new(owner.Rect.X + 18, owner.Rect.Y + 24), delta);
                await Task.Delay(5, token).ConfigureAwait(false);
            }
            throw new InvalidOperationException("The actual business editor did not enter its viewport.");
        }
        XsrUiEntityId OptionalOwned(string key)
        {
            XsrUiEntityId result = default;
            shell.Tree.Walk(settings.Page, entity =>
            {
                // Settings keeps one SettingsSections root per pager page alive.
                // A control lookup belongs to the selected page and its shared navigation.
                var parent = shell.Tree.Parent(entity);
                if (parent.IsAssigned && shell.Tree.Name(parent) == "SettingsPager"
                    && shell.Tree.GetComponent<XsrUiPager>(parent) is { } pager)
                {
                    var pages = shell.Tree.Children(parent);
                    if (pager.PageIndex < 0 || pager.PageIndex >= pages.Count || pages[pager.PageIndex] != entity) return false;
                }
                if (shell.Tree.Name(entity) == key)
                { Check(!result.IsAssigned, "Duplicate current-page managed validation key: " + key); result = entity; }
                return true;
            });
            return result;
        }
        XsrUiEntityId Owned(string key)
        { var result = OptionalOwned(key); Check(result.IsAssigned, "Missing owned managed validation control: " + key); return result; }
        string SourceFor(string path) => themes.Snapshot.Catalogue.Themes.Single(source => source.DevelopmentDirectory == path).SourceIdentity;
        ThemeOptionState? Options(string identity) => themes.Snapshot.Current.Options.FirstOrDefault(options => options.SourceIdentity == identity);
        long SourceOptionsRevision(string identity) => Options(identity)?.Revision ?? 0;
        Dictionary<string, ThemeOptionValue> Values(string identity)
        {
            var source = themes.Snapshot.Catalogue.Themes.Single(source => source.SourceIdentity == identity);
            return source.Manifest.Options.ToDictionary(option => option.Id,
                option => Options(identity)?.Values.GetValueOrDefault(option.Id) ?? option.Default, StringComparer.Ordinal);
        }
        void CopySource(string name, string destination)
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Themes", name);
            Check(File.Exists(Path.Combine(source, "theme.json")), "Published NTP author template is missing: " + name);
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
        }
        void AssertEditor()
        {
            shell.Render(size);
            Check(shell.Tree.IsAlive(argument) && Owned("SettingsArgument.game.arguments.0") == argument
                && shell.Tree.GetComponent<XsrUiTextInput>(argument)!.ReadDraft() == "--ntp literal <argument> {state private}"
                && shell.Renderer.Focused == argument, "A theme operation replaced or changed the retained business editor.");
            var currentInput = shell.Tree.GetComponent<XsrUiTextInput>(argument)!;
            Check(ReferenceEquals(currentInput, retainedInput) && currentInput.SelectionEnd == retainedCaret,
                "A theme operation moved the retained input caret.");
            var currentScroll = shell.Tree.GetComponent<XsrUiScroll>(Owned("SettingsSections"))!;
            Check(ReferenceEquals(currentScroll, retainedScroll) && Math.Abs(currentScroll.OffsetY - retainedOffset) < .001,
                "A theme operation changed the retained scroll owner/offset.");
            // Semantic/command identity is read from the actual host owner, never
            // inferred from an authored theme selector or reconstructed UI factory.
            var currentBinding = shell.Tree.GetComponent<XsrUiCommandBinding>(Owned("SettingsEdit.game.arguments"));
            Check(ReferenceEquals(currentBinding, retainedBinding) && currentBinding!.Command == retainedCommand
                && shell.Tree.GetComponent<XsrUiSemantic>(Owned("SettingsEdit.game.arguments"))!.Role == retainedRole,
                "A theme operation changed its host command or role.");
            Check(!hasMode || state.Read<int>(modeId).Value == modeValue, "A theme operation changed the host light/dark policy.");
        }
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
