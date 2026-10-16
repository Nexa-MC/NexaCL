using Nexa.Services.Setup;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void LauncherMigrationPageCyclesEverySupportedLauncherType()
    {
        using var ui = new LauncherMigrationUiFixture();
        ui.ChooseSourceAndDestination();
        foreach (var kind in Enum.GetValues<LauncherMigrationSourceKind>())
        {
            ui.Click("preview"); ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
            AssertEqual(kind, ui.LastPreview!.Query.SourceKind);
            AssertTrue(ui.Text(ui.Find("LauncherMigration.SourceKind")).Length > 0);
            ui.Click("kind");
            AssertFalse(ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        }
        ui.Click("preview"); ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        AssertEqual(LauncherMigrationSourceKind.Auto, ui.LastPreview!.Query.SourceKind);
    }

    private static void LauncherMigrationDiscoverySelectsSourceWithoutImporting()
    {
        using var ui = new LauncherMigrationUiFixture();
        ui.ChooseSourceAndDestination(); ui.Click("preview");
        ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        string destination = ui.LastPreview!.Query.DestinationDirectory;
        string alternate = Path.Combine(ui.Fixture.TemporaryDirectory, "gd-launcher-data");
        Directory.CreateDirectory(alternate);
        ui.DiscoveryHandler = (_, _) => new(XsrResult.Success(new LauncherMigrationDiscoveryResult(
            [new(LauncherMigrationSourceKind.Technic, "Literal <source> {data private}", ui.Source),
             new(LauncherMigrationSourceKind.GdLauncher, "GDLauncher", alternate)], ["Literal <warning> {state private}"])));
        ui.Click("discover"); ui.Wait(() => ui.Find("LauncherMigration.Detected.1", false).IsAssigned);
        AssertEqual(LauncherMigrationSourceKind.Auto, ui.LastDiscovery!.SourceKind);
        AssertEqual(1, ui.PreviewCalls); AssertEqual(0, ui.ApplyCalls);
        AssertEqual(ui.Source, ui.Text(ui.Find("LauncherMigration.Source")));
        AssertEqual("Literal <source> {data private}", ui.Text(ui.Find("LauncherMigration.Detected.0")));
        AssertTrue(ui.AllText().Contains("Literal <warning> {state private}", StringComparison.Ordinal));
        AssertTrue(ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        ui.ClickKey("LauncherMigration.Detected.1");
        AssertFalse(ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        AssertEqual(alternate, ui.Text(ui.Find("LauncherMigration.Source")));
        AssertEqual(destination, ui.Text(ui.Find("LauncherMigration.Destination")));
        AssertTrue(ui.Fixture.Shell.Tree.GetComponent<XsrUiToggle>(ui.Find("LauncherMigration.Detected.1"))!.IsChecked);
        AssertFalse(ui.Fixture.Shell.Tree.GetComponent<XsrUiToggle>(ui.Find("LauncherMigration.Detected.0"))!.IsChecked);
        ui.Click("preview"); ui.Wait(() => ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        AssertEqual(LauncherMigrationSourceKind.GdLauncher, ui.LastPreview!.Query.SourceKind);
        AssertEqual(alternate, ui.LastPreview.Query.SourceDirectory);
        AssertEqual(destination, ui.LastPreview.Query.DestinationDirectory);
        AssertEqual(0, ui.ApplyCalls); AssertFalse(Directory.Exists(destination));
        ui.Click("discover"); ui.Wait(() => ui.DiscoveryCalls == 2 && ui.Enabled("discover"));
        AssertEqual(LauncherMigrationSourceKind.GdLauncher, ui.LastDiscovery!.SourceKind);
        ui.Picks.Enqueue(ui.Source); ui.Click("source"); ui.Wait(() => ui.Text(ui.Find("LauncherMigration.Source")) == ui.Source);
        AssertFalse(ui.Find("LauncherMigration.Select.one", false).IsAssigned);
        AssertTrue(Directory.Exists(alternate)); AssertTrue(Directory.Exists(ui.Source));
    }

    private static void LauncherMigrationDiscoveryCancelsRetiredResultsAndRecovers()
    {
        using var ui = new LauncherMigrationUiFixture();
        ui.ChooseSourceAndDestination();
        var pending = new TaskCompletionSource<XsrResult<LauncherMigrationDiscoveryResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken discoveryToken = default;
        ui.DiscoveryHandler = (_, token) => { discoveryToken = token; return new(pending.Task); };
        ui.Click("discover"); ui.Wait(() => ui.DiscoveryCalls == 1);
        AssertFalse(ui.Enabled("kind")); AssertFalse(ui.Enabled("preview")); AssertTrue(ui.Enabled("cancel"));
        ui.Navigate("general"); AssertTrue(discoveryToken.IsCancellationRequested);
        var found = new LauncherMigrationDiscoveryResult([new(LauncherMigrationSourceKind.Prism, "Late source", ui.Source)], []);
        pending.SetResult(XsrResult.Success(found));
        ui.Navigate("storage"); ui.Wait(() => ui.Enabled("discover"));
        AssertFalse(ui.Find("LauncherMigration.Detected.0", false).IsAssigned);
        AssertEqual(ui.Source, ui.Text(ui.Find("LauncherMigration.Source")));
        ui.DiscoveryHandler = (_, _) => new(XsrResult.Failure<LauncherMigrationDiscoveryResult>(XsrRuntimeErrors.TargetNotFound("Discovery fixture failure")));
        ui.Click("discover"); ui.Wait(() => ui.DiscoveryCalls == 2 && ui.Enabled("discover"));
        AssertFalse(ui.Find("LauncherMigration.Detected.0", false).IsAssigned);
        ui.DiscoveryHandler = (_, _) => new(XsrResult.Success(new LauncherMigrationDiscoveryResult([], ["No default directory"])));
        ui.Click("discover"); ui.Wait(() => ui.DiscoveryCalls == 3 && ui.Enabled("discover"));
        AssertTrue(ui.AllText().Contains("便携或自定义安装请手动选择", StringComparison.Ordinal));
        AssertTrue(ui.AllText().Contains("No default directory", StringComparison.Ordinal));
        ui.DiscoveryHandler = (_, _) => new(XsrResult.Success(found));
        ui.Click("discover"); ui.Wait(() => ui.Find("LauncherMigration.Detected.0", false).IsAssigned);
        AssertEqual(0, ui.PreviewCalls); AssertEqual(0, ui.ApplyCalls);
        ui.Click("kind"); AssertFalse(ui.Find("LauncherMigration.Detected.0", false).IsAssigned);
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.DiscoveryHandler = (_, token) => { discoveryToken = token; return new(pending.Task); };
        ui.Click("discover"); ui.Wait(() => ui.DiscoveryCalls == 5);
        ui.Click("cancel"); AssertTrue(discoveryToken.IsCancellationRequested);
        pending.SetResult(XsrResult.Success(found));
        ui.Pump(); AssertTrue(ui.Enabled("discover"));
        AssertFalse(ui.Find("LauncherMigration.Detected.0", false).IsAssigned);
    }
}
