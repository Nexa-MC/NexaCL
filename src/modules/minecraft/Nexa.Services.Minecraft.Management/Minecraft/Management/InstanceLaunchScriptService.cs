using System.Text;
using Nexa.Xsr;
namespace Nexa.Services.Minecraft.Management;

public static class InstanceLaunchScriptService
{
    public static async Task<XsrResult> ExportAsync(InstanceLaunchScriptCommand command, CancellationToken token = default)
    {
        string? stage = null;
        try
        {
            var instance = await InstanceManagementService.ReadAsync(new(command.InstanceDirectory), token).ConfigureAwait(false);
            string launcher = command.LauncherExecutable, destination = command.DestinationPath;
            if (!Path.IsPathFullyQualified(launcher) || !File.Exists(launcher) || launcher.Any(c => char.IsControl(c) || c == '"') || Path.GetFileNameWithoutExtension(launcher) is "dotnet" or "testhost") throw new InvalidDataException("请选择已发布的启动器可执行文件。");
            bool windows = destination.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || destination.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            if (!Path.IsPathFullyQualified(destination) || (!windows && !destination.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("请选择完整的 .cmd、.bat 或 .sh 路径。");
            RecoveryBlobStore.CheckLinks(destination);
            string root = Directory.GetParent(instance.InstanceDirectory)!.Parent!.FullName;
            string link = InstanceLaunchScriptContract.Link(root, Path.GetFileName(instance.InstanceDirectory));
            string text = windows ? "@echo off\r\nchcp 65001 >nul\r\nsetlocal DisableDelayedExpansion\r\n\"" + launcher.Replace("%", "%%", StringComparison.Ordinal) + "\" \"" + link + "\"\r\n"
                : "#!/bin/sh\nexec " + Quote(launcher) + " " + Quote(link) + "\n";
            stage = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(stage, text, new UTF8Encoding(false), token).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows() && !windows) File.SetUnixFileMode(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            token.ThrowIfCancellationRequested(); RecoveryBlobStore.CheckLinks(destination); File.Move(stage, destination, false);
            return XsrResult.Success();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return XsrResult.Failure(MinecraftErrors.InvalidRequest(e.Message)); }
        finally { if (stage is not null) File.Delete(stage); }
    }
    private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
