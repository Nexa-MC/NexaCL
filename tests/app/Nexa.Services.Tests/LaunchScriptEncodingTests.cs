using System.Diagnostics;
using System.Text;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask LaunchScriptsPreserveUnicodePaths()
    {
        string root = CreateTempDirectory();
        try
        {
            string instance = ResourceInstanceFixture(root);
            bool windows = OperatingSystem.IsWindows();
            string launcher = Path.Combine(root, "启动器's & app" + (windows ? ".cmd" : ".sh"));
            string helper = windows ? "@echo off\r\n> \"%~dp0capture.txt\" echo %~1\r\n"
                : "#!/bin/sh\nprintf '%s' \"$1\" > \"$(dirname \"$0\")/capture.txt\"\n";
            await File.WriteAllTextAsync(launcher, helper, new UTF8Encoding(false));
            if (!windows) File.SetUnixFileMode(launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string script = Path.Combine(root, "launch" + (windows ? ".cmd" : ".sh"));
            var result = await InstanceLaunchScriptService.ExportAsync(new(instance, launcher, script));
            AssertTrue(result.IsSuccess, result.Error?.Message ?? "");
            string driver = Path.Combine(root, "driver.cmd");
            if (windows) await File.WriteAllTextAsync(driver, "@echo off\r\nchcp 437 >nul\r\ncall \"%~dp0launch.cmd\"\r\n", Encoding.ASCII);
            var start = new ProcessStartInfo(windows ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe") : "/bin/sh")
            { UseShellExecute = false, WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            if (windows) { start.ArgumentList.Add("/d"); start.ArgumentList.Add("/c"); start.ArgumentList.Add("driver.cmd"); }
            else start.ArgumentList.Add(script);
            using var process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            AssertTrue(process.ExitCode == 0, await output + await error);
            string captured = (await File.ReadAllTextAsync(Path.Combine(root, "capture.txt"))).Trim();
            AssertEqual(InstanceLaunchScriptContract.Link(root, Path.GetFileName(instance)), captured);
        }
        finally { Directory.Delete(root, true); }
    }
}
