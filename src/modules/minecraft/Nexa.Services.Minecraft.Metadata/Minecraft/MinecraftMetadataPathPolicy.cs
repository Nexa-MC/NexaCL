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
                if (Nexa.Core.PathIdentity.IsMacOSSystemDirectoryAlias(current)) continue;
                throw new InvalidDataException("Minecraft metadata path traverses a link.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
