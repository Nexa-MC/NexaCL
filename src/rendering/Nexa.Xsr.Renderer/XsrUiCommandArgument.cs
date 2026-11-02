using System.Globalization;

namespace Nexa.Xsr.Renderer;

/// <summary>Literal command data authored by the shared NXML pipeline.</summary>
public sealed record XsrUiCommandArgument(string Value);
public sealed record XsrUiArgumentSource(XsrUiEntityId Entity);

public sealed partial class XsrUiRenderer
{
    public string ReadIntentArgument(XsrUiEntityId source)
    {
        if (!_tree.IsAlive(source)) return "";
        if (_tree.GetComponent<XsrUiArgumentSource>(source) is { } reference) return ReadIntentArgument(reference.Entity);
        if (_tree.GetComponent<XsrUiCommandArgument>(source) is { } literal) return literal.Value;
        if (_tree.GetComponent<XsrUiTextInput>(source) is { } input) return input.ReadDraft();
        if (_tree.GetComponent<XsrUiToggle>(source) is { } toggle) return (!toggle.IsChecked).ToString(CultureInfo.InvariantCulture);
        if (_tree.GetComponent<XsrUiRangeInput>(source) is { } range) return range.Value.ToString(CultureInfo.InvariantCulture);
        return "";
    }
}
