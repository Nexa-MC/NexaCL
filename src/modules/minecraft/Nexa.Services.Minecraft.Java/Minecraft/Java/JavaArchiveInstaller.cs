using System.IO.Compression;
using System.Security.Cryptography;
using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Java;

/// <summary>Installs a verified ZIP distribution in a new directory, then probes and registers its Java executable.</summary>
public sealed class JavaArchiveInstaller(IJavaRuntimeLocator locator, JavaRuntimeManagementService management)
{
    public async ValueTask<XsrResult> InstallAsync(JavaArchiveInstallCommand command, CancellationToken token = default)
    {
        string? stage = null;
        try
        {
            if (!Path.IsPathFullyQualified(command.Archive) || !Path.IsPathFullyQualified(command.Destination) || command.Size is <= 0 or > 536870912
                || command.Sha256.Length != 64 || !command.Sha256.All(char.IsAsciiHexDigit) || command.Major is < 8 or > 99) throw new InvalidDataException("Invalid Java archive request.");
            CheckLinks(command.Archive); CheckLinks(command.Destination);
            if (Path.Exists(command.Destination)) throw new IOException("Java destination already exists.");
            await using var file = new FileStream(command.Archive, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            if (file.Length != command.Size || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)), command.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Java archive integrity check failed.");
            file.Position = 0;
            stage = command.Destination + ".install-" + Guid.NewGuid().ToString("N"); CheckLinks(stage); Directory.CreateDirectory(stage);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, true);
            if (archive.Entries.Count is < 1 or > 8192) throw new InvalidDataException("Java archive exceeds its entry budget.");
            long total = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var executables = new List<string>();
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                string name = entry.FullName.TrimEnd('/');
                if (name.Length is < 1 or > 1024 || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Any(char.IsControl)
                    || name.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')) || !names.Add(name)
                    || ((entry.ExternalAttributes >> 16) & 0xF000) is 0xA000 || entry.Length > 536870912 || (total = checked(total + entry.Length)) > 2147483648)
                    throw new InvalidDataException("Unsafe or oversized Java archive entry.");
                string path = Path.Combine(stage, name.Replace('/', Path.DirectorySeparatorChar)); CheckLinks(path);
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using (var input = entry.Open())
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    byte[] buffer = new byte[65536]; long copied = 0; int count;
                    while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                    { if ((copied += count) > entry.Length) throw new InvalidDataException("Java archive entry expanded beyond its declared size."); await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); }
                    if (copied != entry.Length) throw new IOException("Truncated Java archive entry.");
                }
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead
                    | (UnixFileMode)((entry.ExternalAttributes >> 16) & 0x49));
                if (Path.GetFileName(path) == (OperatingSystem.IsWindows() ? "java.exe" : "java") && Path.GetFileName(Path.GetDirectoryName(path)) == "bin") executables.Add(path);
            }
            if (executables.Count != 1) throw new InvalidDataException("Java archive must contain exactly one bin/java executable.");
            var candidate = await locator.InspectAsync(executables[0], token).ConfigureAwait(false);
            if (candidate is not { IsAvailable: true } || (candidate.Installation.Version.Major == 1 ? candidate.Installation.Version.Minor : candidate.Installation.Version.Major) != command.Major
                || !candidate.Installation.Architecture.ToString().Equals(command.Architecture, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Java runtime does not match its distribution descriptor.");
            string executable = Path.Combine(command.Destination, Path.GetRelativePath(stage, executables[0]));
            token.ThrowIfCancellationRequested(); CheckLinks(command.Destination); Directory.Move(stage, command.Destination); stage = null;
            locator.Invalidate();
            // A failed registry CAS leaves the verified directory available for explicit registration.
            return await management.ManageAsync(new(executable, JavaRuntimeManagementAction.Add, command.ExpectedRegistryRevision), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException)
        { return XsrResult.Failure(new XsrError(XsrErrorKind.Rejected, XsrSemanticId.Parse("java.archive.rejected"), error.Message)); }
        finally { if (stage is not null && Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    private static void CheckLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Java archive paths cannot traverse links.");
    }
}
