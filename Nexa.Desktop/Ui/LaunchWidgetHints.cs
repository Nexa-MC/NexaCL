using System.Globalization;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

/// <summary>The launch-home trivia catalogue, in its authored presentation order.</summary>
internal static class LaunchWidgetHints
{
    private static readonly Lazy<IReadOnlyList<string>> Hints = new(() =>
    {
        var nodes = DesktopMarkup.Load("LaunchInstallHomeHints.nxml").Root.Children;
        if (nodes.Count == 0 || nodes.Select((node, index) => node.Recipe != NxmlRuntimeRecipe.Text
            || node.Key != "hint." + index.ToString(CultureInfo.InvariantCulture)
            || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0).Any(invalid => invalid))
            throw new NxmlCompileException("Launch hints must be ordered keyed literal text.");
        return Array.AsReadOnly(nodes.Select(node => node.Content!).ToArray());
    });

    public static IReadOnlyList<string> BuiltIn => Hints.Value;
}
