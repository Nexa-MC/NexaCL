using System.Diagnostics;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiPlatformActions
{
    public static async Task PreviewScreenshotAsync(string path, long expectedSize, long expectedModifiedUtcTicks)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Quick Look requires macOS.");
        VerifyScreenshotIdentity(path, expectedSize, expectedModifiedUtcTicks);
        _ = ReadScreenshot(path);
        string helper = Path.Combine(AppContext.BaseDirectory, "Nexa.Platform.Helper");
        if (!File.Exists(helper)) throw new FileNotFoundException("The native preview helper is missing.", helper);
        ProcessStartInfo info = new(helper) { UseShellExecute = false, RedirectStandardOutput = true };
        info.ArgumentList.Add("preview"); info.ArgumentList.Add(path);
        VerifyScreenshotIdentity(path, expectedSize, expectedModifiedUtcTicks);
        using var process = Process.Start(info) ?? throw new IOException("Quick Look did not start.");
        try
        {
            if (await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false) != "ready")
                throw new IOException("Quick Look could not open this screenshot.");
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
    }
}
