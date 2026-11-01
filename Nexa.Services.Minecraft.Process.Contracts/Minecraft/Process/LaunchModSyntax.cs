using System.Text.RegularExpressions;

namespace Nexa.Services.Minecraft.Process;

public static partial class LaunchModSyntax
{
    public static bool SafeId(string text) => IdPattern().IsMatch(text);
    public static bool SafeVersion(string text) => VersionPattern().IsMatch(text);
    public static bool SafeGameVersion(string text) => GamePattern().IsMatch(text);
    [GeneratedRegex("^(?:unknown|[0-9]{1,2}\\.[0-9]{1,2}(?:\\.[0-9]{1,2})?(?:-(?:pre|rc)[0-9]{1,2})?|[0-9]{2}w[0-9]{2}[a-z])$")]
    private static partial Regex GamePattern();
    public static bool SafeRange(string text) => RangePattern().IsMatch(text);
    [GeneratedRegex("^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,95}$")]
    private static partial Regex IdPattern();
    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.+\\-]{0,95}$")]
    private static partial Regex VersionPattern();
    [GeneratedRegex("^[a-zA-Z0-9_.+*<>=~^|, ()\\[\\]\\-]{1,192}$")]
    private static partial Regex RangePattern();
}
