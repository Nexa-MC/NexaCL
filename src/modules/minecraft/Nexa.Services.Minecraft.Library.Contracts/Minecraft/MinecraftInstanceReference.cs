namespace Nexa.Services.Minecraft;

/// <summary>Shared admission policy for one instance or version path segment.</summary>
public static class MinecraftInstanceReference
{
    public static bool IsSafe(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 180 && value is not "." and not ".." &&
        !Path.IsPathRooted(value) && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !value.Contains(':', StringComparison.Ordinal) &&
        value.IndexOfAny(['/', '\\']) < 0 && !value.Any(char.IsControl) &&
        string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal);

}
