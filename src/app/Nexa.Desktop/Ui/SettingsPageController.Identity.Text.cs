using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly Lazy<Dictionary<string, string>> SettingsCustomTerms = new(() =>
    {
        var terms = DesktopMarkup.Load("SettingsCustomTerms.nxml").Root.Children;
        if (terms.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
            || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("Custom settings terms must be keyed literal text.");
        return terms.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });

    private static string CustomRaw(string key) => SettingsCustomTerms.Value[key];
    private string CustomCopy(string key) => _shell.Renderer.LocalizeText(CustomRaw(key));
    private sealed record CustomText(string Name, Dictionary<string, string>? Data = null);
    private static CustomText CustomTerm(string name) => new(name);
    private static CustomText CustomFormatted(string name, params (string Key, string Value)[] values)
        => new(name, values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));
    private string ProjectCustomText(CustomText text) => text.Data is null ? CustomCopy(text.Name) : CustomFormat(text.Name, text.Data);
    private static string CustomNumber(long value) => value.ToString(System.Globalization.CultureInfo.CurrentCulture);

    private string CustomFormat(string name, Dictionary<string, string> data)
    {
        var text = DesktopMarkup.Dynamic("SettingsCustomFormat" + name + ".d.nxml", data, _shell.Renderer.LocalizeText).Root;
        if (text.Recipe != NxmlRuntimeRecipe.Text || text.Content is null || text.Bindings.Count != 0 || text.Children.Count != 0)
            throw new NxmlCompileException("Custom settings formats must produce literal text only.");
        return text.Content;
    }
}
