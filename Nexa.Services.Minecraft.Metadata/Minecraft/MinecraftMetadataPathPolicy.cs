namespace Nexa.Services.Minecraft;

/// <summary>Rejects linked metadata paths except fixed macOS system-directory aliases.</summary>
internal static class MinecraftMetadataPathPolicy
{
    internal static void CheckLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0) continue;
                // macOS exposes these OS-owned directories through fixed root aliases.
                // Admit only the expected target, never a link inside an installation/cache.
                if (OperatingSystem.IsMacOS() && current is "/var" or "/tmp" or "/etc"
                    && new DirectoryInfo(current).ResolveLinkTarget(true)?.FullName is { } target
                    && string.Equals(target, "/private" + current, StringComparison.Ordinal))
                {
                    CheckLinks(target);
                    continue;
                }
                throw new InvalidDataException("Minecraft metadata path traverses a link.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
