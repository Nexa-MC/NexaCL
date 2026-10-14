namespace Nexa.Services.Themes;

internal static class ThemePaths
{
    internal static string Relative(string value)
    {
        if (value.Length is < 1 or > 1024 || value.Contains('\\') || value.Contains(':') || value.Any(char.IsControl) || value.StartsWith('/') || Path.IsPathRooted(value)) throw new InvalidDataException("Invalid package path.");
        foreach (string part in value.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || Reserved(part)) throw new InvalidDataException("Ambiguous package path.");
        }
        return value;
    }
    internal static (string Function, string Argument) Expression(string expression)
    {
        int open = expression.IndexOf('(');
        if (open < 1 || !expression.EndsWith(')')) throw new InvalidDataException("Invalid resource expression.");
        string function = expression[..open], argument = expression[(open + 1)..^1].Trim();
        if (argument.Length >= 2 && argument[0] == '"' && argument[^1] == '"') argument = argument[1..^1];
        if (argument.Length is < 1 or > 4096 || argument.Any(char.IsControl) || argument.Contains('"') || function is not ("asset" or "file" or "folder")) throw new InvalidDataException("Invalid resource expression.");
        if (function == "asset") Relative(argument);
        return (function, argument);
    }
    internal static string Within(string root, string relative)
    {
        Relative(relative); string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Path escapes its root.");
        NoLinks(path); return path;
    }
    internal static void NoLinks(string path)
    {
        string full = Path.GetFullPath(path); string? cursor = full;
        while (cursor is not null)
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Links are not allowed in theme sources or resource grants.");
            cursor = Path.GetDirectoryName(cursor);
        }
    }
    private static bool Reserved(string part)
    {
        string name = part.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase) || name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && name[3] is >= '1' and <= '9';
    }
}
