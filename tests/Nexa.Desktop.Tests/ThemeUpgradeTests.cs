using Nexa.Services.Themes;
using Nexa.Xsr;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ThemeSettingsDowngradeDialogDisplaysBothVersions()
    {
        foreach (bool accept in new[] { false, true })
        {
            using var page = new ThemeSettingsFixture([]);
            var entry = page.Session.Snapshot.Catalogue.Themes[0];
            page.Session.ImportCandidate = new(Guid.NewGuid(), 1, entry.SourceIdentity, entry.SourceRevision,
                entry.SourceKind, entry.Manifest, [], new Dictionary<string, ThemeOptionValue>(), [])
            { PreviousVersion = "2.0.0", IsDowngrade = true };
            page.Settings.ConfigureThemePickers((_, _) => Task.FromResult<IReadOnlyList<string>>(["theme.ntp"]),
                _ => Task.FromResult<string?>(null), _ => Task.FromResult<string?>(null));
            page.Render();
            Emit(page.Fixture.Intents, "ui.settings.theme.action", page.Find("Theme.ImportPackage"));
            page.Pump(() => page.Fixture.Feedback.Snapshot().Dialog is not null);
            var dialog = page.Fixture.Feedback.Snapshot().Dialog!;
            AssertTrue(dialog.Message.Contains("2.0.0", StringComparison.Ordinal));
            AssertTrue(dialog.Message.Contains("1.0.0", StringComparison.Ordinal));
            AssertFalse(dialog.Message.Contains("{data", StringComparison.Ordinal));
            AssertTrue(page.Fixture.Feedback.ResolveDialog(dialog.Id, accept));
            page.Pump(() => page.Session.DowngradeDecision == accept);
        }
    }

    private static (string Newer, string Older) UpgradeSources(ThemeLifecycleFixture fixture)
    {
        string newer = fixture.MakeTheme("upgrade", ""), older = fixture.MakeTheme("older", "");
        string manifest = Path.Combine(newer, "theme.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        manifest = Path.Combine(older, "theme.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("fixture.older", "fixture.upgrade", StringComparison.Ordinal));
        return (newer, older);
    }

    private static void ThemeLifecycleSameIdImportConfirmsDowngradeBeforeActivation()
    {
        using var fixture = new ThemeLifecycleFixture(); var paths = UpgradeSources(fixture);
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(paths.Newer, ThemeSourceKind.Directory, false, CancellationToken.None)).IsSuccess);
        string identity = fixture.Current().Selection!.SourceIdentity;
        int confirmations = 0;
        Task<bool> Reject(ThemeCandidate candidate, CancellationToken token)
        {
            confirmations++;
            AssertEqual("2.0.0", fixture.Current().Selection!.Version);
            AssertEqual("2.0.0", candidate.PreviousVersion!);
            AssertEqual("1.0.0", candidate.Manifest.Version);
            return Task.FromResult(false);
        }
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(paths.Older, ThemeSourceKind.Directory, false, CancellationToken.None, Reject)).IsSuccess);
        AssertEqual(1, confirmations);
        AssertEqual("2.0.0", fixture.Current().Selection!.Version);
        AssertFalse(fixture.Run(fixture.Session.ImportAsync(paths.Older, ThemeSourceKind.Directory, false, CancellationToken.None)).IsSuccess);
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(paths.Older, ThemeSourceKind.Directory, false, CancellationToken.None,
            (_, _) => Task.FromResult(true))).IsSuccess);
        AssertEqual("1.0.0", fixture.Current().Selection!.Version);
        AssertEqual(identity, fixture.Current().Selection!.SourceIdentity);
        AssertEqual(1, fixture.Session.Snapshot.Catalogue.Themes.Count(t => t.Manifest.Id == "fixture.upgrade"));
        fixture.Restart(safeMode: false);
        AssertEqual("1.0.0", fixture.Current().Selection!.Version);
    }

    private static void ThemeLifecycleCancelledDowngradeRetainsInstalledVersion()
    {
        using var fixture = new ThemeLifecycleFixture(); var paths = UpgradeSources(fixture);
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(paths.Newer, ThemeSourceKind.Directory, false, CancellationToken.None)).IsSuccess);
        using var stop = new CancellationTokenSource();
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = fixture.Session.ImportAsync(paths.Older, ThemeSourceKind.Directory, false, stop.Token,
            (_, token) => { shown.TrySetResult(); return decision.Task.WaitAsync(token); });
        fixture.Pump(() => shown.Task.IsCompleted);
        stop.Cancel();
        var result = fixture.Run(operation);
        AssertFalse(result.IsSuccess);
        AssertEqual(XsrErrorKind.Cancelled, result.Error!.Kind);
        AssertEqual("2.0.0", fixture.Current().Selection!.Version);
        AssertEqual(1, fixture.Session.Snapshot.Catalogue.Themes.Count(t => t.Manifest.Id == "fixture.upgrade"));
    }
}
