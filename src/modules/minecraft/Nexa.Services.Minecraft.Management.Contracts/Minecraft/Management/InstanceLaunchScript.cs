using System.Text;
using Nexa.Xsr;
namespace Nexa.Services.Minecraft.Management;

public sealed record InstanceLaunchScriptCommand(string InstanceDirectory, string LauncherExecutable, string DestinationPath);
public static class InstanceLaunchScriptContract
{
    public static readonly XsrSemanticId Export = XsrSemanticId.Parse("minecraft.instance.launch-script.export");
    public static string Link(string root, string instance)
    {
        if (!ValidRoot(root) || !ValidInstance(instance)) throw new ArgumentException("实例启动目标无效。");
        string link = "nexacl://run/" + Encode(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))) + "/" + Encode(instance);
        return link.Length <= 8192 ? link : throw new ArgumentException("实例启动链接超过长度限制。");
    }
    public static bool TryLink(string value, out string root, out string instance)
    {
        root = instance = "";
        if (value.Length > 8192 || !value.StartsWith("nexacl://run/", StringComparison.Ordinal)) return false;
        var parts = value[13..].Split('/');
        if (parts.Length != 2) return false;
        try { root = Decode(parts[0]); instance = Decode(parts[1]); return ValidRoot(root) && ValidInstance(instance) && Link(root, instance) == value; }
        catch (Exception e) when (e is ArgumentException or FormatException or NotSupportedException) { root = instance = ""; return false; }
    }
    private static bool ValidRoot(string root) => root.Length is > 0 and <= 4096 && Path.IsPathFullyQualified(root) && !root.Any(char.IsControl) && !root.StartsWith("\\\\", StringComparison.Ordinal);
    private static bool ValidInstance(string name) => name.Length is > 0 and <= 255 && name is not ("." or "..") && !name.Any(c => char.IsControl(c) || "/\\:\"<>|?*".Contains(c)) && !name.EndsWith('.') && !name.EndsWith(' ');
    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Decode(string value)
    {
        if (value.Length == 0 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw new FormatException();
        string encoded = value.Replace('-', '+').Replace('_', '/');
        return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
    }
}
