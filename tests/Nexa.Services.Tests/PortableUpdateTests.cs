using System.Runtime.InteropServices;
using Nexa.Platform.Updates;
using Nexa.Services.Updates;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static string CreatePortableUpdateTestDirectory()
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), ".nexa-portable-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async ValueTask PortableUpdateUsesDeltaAndRestoresOriginalBaseline()
    {
        if (PortableUpdateInstallation.IsElevated)
        {
            AssertThrows<UnauthorizedAccessException>(() => PortableUpdateDirectory.Open(Path.GetTempPath()));
            Console.WriteLine("SKIP: portable mutation is deliberately unavailable to elevated processes.");
            return;
        }
        string path = CreatePortableUpdateTestDirectory();
        try
        {
            var fixture = CreateDeltaFixture(RuntimeInformation.RuntimeIdentifier);
            using (var seed = new UpdateFixtureDirectory(path)) SeedDeltaSource(seed, fixture);
            File.WriteAllText(Path.Combine(path, "user-settings.json"), "preserve me");
            var control = new PortableUpdateControl(path, fixture.Identity, fixture.Verifier, fixture.Source);
            var ready = await control.ReadAsync(default);
            AssertTrue(ready.CanInstall);
            AssertEqual("idle", ready.Phase);
            var completed = await control.InstallAsync("2.0.0.alpha.6", "alpha", default);
            AssertEqual("complete", completed.Phase);
            AssertTrue(completed.CanRollback);
            AssertEqual(0, fixture.Source.FullRequests);
            AssertEqual(1, fixture.Source.DeltaRequests);
            using var directory = PortableUpdateDirectory.Open(path);
            string slot;
            using (var active = directory.OpenRead(AutomaticUpdateTransaction.ActivationName)) slot = UpdateTransactionJournal.Read(active)![1];
            AssertTrue(PortableUpdateInstallation.IsSlot(slot));
            AssertDeltaOutput(directory, slot, fixture);
            AssertEqual(path, PortableUpdateInstallation.ResolveRoot(PortableUpdateInstallation.Executable(path)));
            AssertEqual(path, PortableUpdateInstallation.ResolveRoot(PortableUpdateInstallation.Executable(Path.Combine(path, slot))));
            // The new process must retain the original bootstrap identity, rather than treating
            // its current active-slot version as the baseline and losing rollback/recovery.
            var upgraded = new PortableUpdateControl(path, fixture.Identity with { Version = "2.0.0.alpha.6" }, fixture.Verifier, fixture.Source);
            fixture.Source.Offline = true;
            AssertEqual("complete", (await upgraded.InstallAsync("2.0.0.alpha.6", "alpha", default)).Phase);
            AssertEqual(1, fixture.Source.DeltaRequests);
            AssertEqual("rolledback", (await upgraded.RollbackAsync(default)).Phase);
            using (var active = directory.OpenRead(AutomaticUpdateTransaction.ActivationName))
            {
                string[] selected = UpdateTransactionJournal.Read(active)!;
                AssertEqual("2.0.0.alpha.5", selected[0]); AssertEqual("", selected[1]);
            }
            AssertEqual("preserve me", File.ReadAllText(Path.Combine(path, "user-settings.json")));
            AssertTrue(File.Exists(Path.Combine(path, "obsolete.txt")));
            await ExpectReleaseRejection(() => upgraded.InstallAsync("2.0.0.alpha.6", "alpha", default));
        }
        finally { Directory.Delete(path, recursive: true); }
    }

    private static async ValueTask PortableUpdateFallbackAndOfflineRecovery()
    {
        if (PortableUpdateInstallation.IsElevated) return;
        foreach (string scenario in new[] { "bad-delta", "received", "accepted", "reject-full" })
        {
            string path = CreatePortableUpdateTestDirectory();
            try
            {
                var fixture = CreateDeltaFixture(RuntimeInformation.RuntimeIdentifier);
                using (var seed = new UpdateFixtureDirectory(path)) SeedDeltaSource(seed, fixture);
                var control = new PortableUpdateControl(path, fixture.Identity, fixture.Verifier, fixture.Source);
                _ = await control.ReadAsync(default); // Persist the original baseline.
                if (scenario is "bad-delta" or "reject-full")
                {
                    fixture.Source.Mutate("bundle-digest");
                    if (scenario == "reject-full") fixture.Source.Full[^1] ^= 1;
                    if (scenario == "reject-full")
                    {
                        await ExpectReleaseRejection(() => control.InstallAsync("2.0.0.alpha.6", "alpha", default));
                        using var dir = PortableUpdateDirectory.Open(path);
                        using var active = dir.OpenRead(AutomaticUpdateTransaction.ActivationName);
                        AssertNull(UpdateTransactionJournal.Read(active));
                        AssertEqual("failed", (await control.ReadAsync(default)).Phase);
                        continue;
                    }
                }
                else
                {
                    using var directory = PortableUpdateDirectory.Open(path);
                    var interrupted = new AutomaticUpdateTransaction(directory, fixture.Identity, fixture.Verifier, fixture.Source)
                    { FaultBoundary = phase => { if (phase == scenario) throw new IOException("Portable interrupted fixture"); } };
                    try { await interrupted.InstallAsync("2.0.0.alpha.6", "alpha"); throw new InvalidOperationException("Missing fault"); }
                    catch (IOException) { }
                    fixture.Source.Offline = true;
                }
                AssertEqual("complete", (await control.InstallAsync("2.0.0.alpha.6", "alpha", default)).Phase);
                AssertEqual(scenario == "bad-delta" ? 1 : 0, fixture.Source.FullRequests);
                AssertEqual(1, fixture.Source.DeltaRequests);
            }
            finally { Directory.Delete(path, recursive: true); }
        }
    }

    private static void PortableUpdateRejectsLinksAndSystemLayouts()
    {
        AssertThrows<UnauthorizedAccessException>(() => PortableUpdateDirectory.Open(UpdateInstallation.Root));
        AssertNull(PortableUpdateInstallation.ResolveRoot(UpdateInstallation.Bootstrap));
        AssertNull(PortableUpdateInstallation.ResolveRoot(PortableUpdateInstallation.Executable(Path.Combine(UpdateInstallation.Root,
            ".nexa-slot-" + new string('a', 32)))));
        AssertNull(PortableUpdateInstallation.ResolveRoot(PortableUpdateInstallation.Executable(Path.GetTempPath()), "appimage"));
        AssertFalse(PortableUpdateInstallation.IsSlot(".nexa-slot-" + new string('/', 32)));
        AssertFalse(PortableUpdateInstallation.IsSlot(".nexa-slot-" + new string('g', 32)));
        if (PortableUpdateInstallation.IsElevated) return;
        string path = CreatePortableUpdateTestDirectory();
        try
        {
            using var root = PortableUpdateDirectory.Open(path);
            AssertThrows<ArgumentException>(() => root.OpenDirectory("../other"));
            AssertThrows<InvalidOperationException>(() => root.CreateFile("Nexa.Desktop"));
            using var created = root.CreateDirectory("fresh", publicRead: false);
            AssertThrows<IOException>(() => root.CreateDirectory("fresh", publicRead: false));
            using var existing = created.CreateFile("payload");
            AssertThrows<IOException>(() => created.CreateFile("payload"));
            using var exclusive = root.OpenState("exclusive");
            AssertThrows<IOException>(() => root.OpenState("exclusive"));
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(Path.Combine(path, "link"), Path.Combine(path, "fresh"));
                AssertThrows<IOException>(() => root.OpenDirectory("link"));
                File.CreateSymbolicLink(Path.Combine(path, "state-link"), Path.Combine(path, "exclusive"));
                AssertThrows<IOException>(() => root.OpenState("state-link"));
                File.CreateSymbolicLink(Path.Combine(path, "dangling"), Path.Combine(path, "missing"));
                AssertThrows<IOException>(() => root.OpenState("dangling"));
            }
        }
        finally { Directory.Delete(path, recursive: true); }
    }
}
