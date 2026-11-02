using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Nexa.Platform;
using Nexa.Services.Memory;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static int RunMemoryCleanupChild()
    {
        byte[] memory = new byte[64 * 1024 * 1024];
        for (int offset = 0; offset < memory.Length; offset += 4096) memory[offset] = 1;
        Console.WriteLine("memory-ready"); Console.Out.Flush();
        _ = Console.ReadLine();
        GC.KeepAlive(memory);
        return 0;
    }

    private static async Task WindowsNativeMemoryCleanupKeepsProcessIdentity()
    {
        var port = new WindowsProcessMemoryPort();
        if (!OperatingSystem.IsWindows()) { AssertFalse(port.IsSupported); AssertFalse(port.Sample().IsValid); return; }
        AssertTrue(port.Sample().IsValid);
        using (var pipe = WindowsMemoryElevationPort.CreateServerPipe("nexa-memory-" + Guid.NewGuid().ToString("N")))
        {
            var security = pipe.GetAccessControl();
            AssertTrue(security.AreAccessRulesProtected);
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            using var current = WindowsIdentity.GetCurrent();
            bool userAllowed = false, adminAllowed = false;
            foreach (PipeAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                AssertFalse(rule.IdentityReference.Equals(everyone));
                if (rule.AccessControlType != AccessControlType.Allow) continue;
                userAllowed |= rule.IdentityReference.Equals(current.User);
                adminAllowed |= rule.IdentityReference.Equals(administrators);
            }
            AssertTrue(userAllowed); AssertTrue(adminAllowed);
        }
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Nexa.Services.Tests.dll"));
        start.ArgumentList.Add("--memory-cleanup-child");
        using var child = Process.Start(start)!;
        try
        {
            AssertEqual("memory-ready", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            var process = port.CaptureProcesses().Single(item => item.Id == child.Id);
            AssertFalse(port.TryTrim(process with { CreationTime = process.CreationTime + 1 }, out _));
            AssertFalse(port.TryTrim(process with { Name = "javaw.exe" }, out _));
            AssertFalse(port.TryTrim(process with { IsProtected = true }, out _));
            AssertTrue(port.TryTrim(process, out long bytes));
            AssertTrue(bytes >= 0);
            AssertFalse(child.HasExited);
            await child.StandardInput.WriteLineAsync("exit");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            AssertFalse(port.TryTrim(process, out _));
            AssertEqual(0, child.ExitCode);
        }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }

        // Hosted Windows CI is already elevated. Exercise the real runas worker/pipe path
        // without a consent dialog and exclude every existing process from the sweep.
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            var exclusions = port.CaptureProcesses().Where(item => item.Id > 0).Select(item => item.Id).ToHashSet();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var elevated = await new WindowsMemoryElevationPort().CleanElevatedAsync(exclusions, deadline.Token);
            AssertEqual(0, elevated.Cleaned);
            AssertTrue(elevated.Skipped > 0);
            Console.WriteLine("Native elevated memory helper authenticated, preserved exclusions and exited.");
        }
    }
}
