using Nexa.Desktop.Ui;
namespace Nexa.Desktop;

/// <summary>One safe product destination, shared by CLI and the command palette.</summary>
internal sealed record DesktopCommandRoute(string Id, string Label, string? NavigationCommand, string? SettingsSection = null);

internal readonly record struct DesktopCommandLineRequest(bool IsSuccess, bool ListCommands, bool SafeMode,
    DesktopCommandRoute? Command, string? Error);

internal static class DesktopCommandLine
{
    private static readonly IReadOnlyList<DesktopCommandRoute> Catalog = Array.AsReadOnly<DesktopCommandRoute>([
        new("launch", DesktopOverlayText.Raw("command.launch"), "ui.navigation.launch"),
        new("install", DesktopOverlayText.Raw("command.install"), "ui.navigation.download"),
        new("resources", DesktopOverlayText.Raw("command.resources"), "ui.navigation.community"),
        new("settings", DesktopOverlayText.Raw("command.settings"), "ui.navigation.settings"),
        new("tasks", DesktopOverlayText.Raw("command.tasks"), "ui.tasks.open"),
        new("java", DesktopOverlayText.Raw("command.java"), "ui.navigation.settings", "java"),
        new("storage", DesktopOverlayText.Raw("command.storage"), "ui.navigation.settings", "storage"),
        new("about", DesktopOverlayText.Raw("command.about"), "ui.navigation.settings", "about"),
    ]);

    internal static IReadOnlyList<DesktopCommandRoute> Commands => Catalog;

    internal static bool TryResolve(string id, out DesktopCommandRoute? command)
    {
        command = Catalog.FirstOrDefault(entry => entry.Id.Equals(id, StringComparison.Ordinal));
        return command is not null;
    }

    internal static DesktopCommandLineRequest Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool listing = false, safe = false;
        DesktopCommandRoute? command = null;
        if (arguments.Count > 128) return Invalid(DesktopOverlayText.Raw("command.error.count"));
        foreach (string argument in arguments)
        {
            if (argument is null || argument.Length > 8192 || argument.Any(char.IsControl))
                return Invalid(DesktopOverlayText.Raw("command.error.invalid"));
            if (argument == "--list-commands")
            {
                if (listing) return Invalid(DesktopOverlayText.Raw("command.error.list"));
                listing = true;
            }
            else if (argument == "--safe-mode")
            {
                if (safe) return Invalid(DesktopOverlayText.Raw("command.error.safe"));
                safe = true;
            }
            else if (argument.StartsWith("--command=", StringComparison.Ordinal))
            {
                if (command is not null) return Invalid(DesktopOverlayText.Raw("command.error.navigation"));
                if (!TryResolve(argument[10..], out command)) return Invalid(DesktopOverlayText.Raw("command.error.unknown"));
            }
            else if (argument == "--command" || argument.StartsWith("--list-commands=", StringComparison.Ordinal)
                || argument.StartsWith("--safe-mode=", StringComparison.Ordinal))
                return Invalid(DesktopOverlayText.Raw("command.error.format"));
        }
        if (listing && command is not null) return Invalid(DesktopOverlayText.Raw("command.error.conflict"));
        return new(true, listing, safe, command, null);
    }

    internal static string FormatListing(Func<string, string>? localize = null) =>
        string.Join(Environment.NewLine, Catalog.Select(entry => entry.Id + "\t" + (localize?.Invoke(entry.Label) ?? entry.Label)));

    private static DesktopCommandLineRequest Invalid(string message) => new(false, false, false, null, message);
}
