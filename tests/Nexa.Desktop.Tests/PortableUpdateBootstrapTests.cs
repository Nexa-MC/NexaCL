using Nexa.Platform.Updates;
using Nexa.Services.Updates;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void PortableUpdateBootstrapSelectsCompleteSlotAndPreservesArguments()
    {
        if (PortableUpdateInstallation.IsElevated) return;
        string path = Path.Combine(Directory.GetCurrentDirectory(), ".nexa-bootstrap-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            using var root = PortableUpdateDirectory.Open(path);
            string slot = ".nexa-slot-" + new string('a', 32);
            string executable = PortableUpdateInstallation.Executable(Path.Combine(path, slot));
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, "fixture bytes, never executed");
            using var journal = root.OpenState(AutomaticUpdateTransaction.ActivationName, publicRead: true, exclusive: false);
            UpdateTransactionJournal.Append(journal, "2.0.0.alpha.11", slot, "2.0.0.alpha.10", "");
            int starts = 0;
            AssertTrue(Nexa.Desktop.Program.RedirectSelectedLauncher(root, "2.0.0.alpha.10", ["--open", "pack with spaces;$(literal).mrpack"], start =>
            {
                starts++;
                AssertEqual(executable, start.FileName);
                AssertEqual(Path.GetDirectoryName(executable), start.WorkingDirectory);
                AssertFalse(start.UseShellExecute);
                AssertEqual("pack with spaces;$(literal).mrpack", start.ArgumentList[1]);
                return true;
            }));
            AssertFalse(Nexa.Desktop.Program.RedirectSelectedLauncher(root, "2.0.0.alpha.11", [], _ => throw new InvalidOperationException("Recursive redirect")));
            foreach (string invalid in new[] { "../../outside", ".nexa-slot-" + new string('g', 32), "" })
            {
                UpdateTransactionJournal.Append(journal, "2.0.0.alpha.11", invalid, "2.0.0.alpha.10", "");
                AssertFalse(Nexa.Desktop.Program.RedirectSelectedLauncher(root, "2.0.0.alpha.10", [], _ => throw new InvalidOperationException("Unsafe slot")));
            }
            UpdateTransactionJournal.Append(journal, "2.0.0.alpha.10", "", "", "");
            AssertFalse(Nexa.Desktop.Program.RedirectSelectedLauncher(root, "2.0.0.alpha.10", [], _ => throw new InvalidOperationException("Rollback must use bootstrap")));
            AssertEqual(1, starts);
        }
        finally { Directory.Delete(path, recursive: true); }
    }
}
