using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Nxml;

internal enum ConditionKind { When, Supports, Viewport }
internal sealed class ThemeCondition
{
    private readonly Func<XsrUiThemeStyleContext, bool> _predicate;
    private ThemeCondition(Func<XsrUiThemeStyleContext, bool> predicate) => _predicate = predicate;
    public bool Matches(XsrUiThemeStyleContext context) => _predicate(context);
    public static ThemeCondition Parse(string expression, NssThemeCompileInputs inputs, ConditionKind kind)
    {
        if (expression.Length > 1024) throw NssThemeCompiler.Error("Condition budget exceeded.");
        expression = NormalizeOperators(expression);
        int nodes = 0;
        return new(ParsePart(expression.Trim(), 0));

        Func<XsrUiThemeStyleContext, bool> ParsePart(string text, int depth)
        {
            if (++nodes > 64 || depth > 16 || text.Length == 0) throw NssThemeCompiler.Error("Invalid or excessive condition.");
            text = text.Trim();
            if (text[0] == '(' && OuterGroup(text)) return ParsePart(text[1..^1], depth + 1);
            int split = Logical(text, " or ");
            if (split >= 0) { var left = ParsePart(text[..split], depth + 1); var right = ParsePart(text[(split + 4)..], depth + 1); return context => left(context) || right(context); }
            split = Logical(text, " and ");
            if (split >= 0) { var left = ParsePart(text[..split], depth + 1); var right = ParsePart(text[(split + 5)..], depth + 1); return context => left(context) && right(context); }
            if (text.StartsWith("not ", StringComparison.Ordinal)) { var inverse = ParsePart(text[4..], depth + 1); return context => !inverse(context); }
            if (kind == ConditionKind.Supports)
            {
                if (!NssThemeCompiler.Identifier(text)) throw NssThemeCompiler.Error("Invalid capability condition.");
                string capability = text;
                return context => (context.Environment.Capabilities ?? inputs.Capabilities).Contains(capability);
            }
            if (kind == ConditionKind.Viewport)
            {
                string[] parts = Comparison(text);
                if (parts[0] is not ("width" or "height")) throw NssThemeCompiler.Error("Viewport conditions require width or height.");
                double boundary = NssThemeCompiler.Number(parts[2]);
                return context => Compare(parts[0] == "width" ? context.Viewport.Width : context.Viewport.Height, boundary, parts[1]);
            }
            string[] comparison = Comparison(text);
            string expected = NssThemeCompiler.UnquoteText(comparison[2]);
            if (comparison[0] == "mode")
            {
                if (expected is not ("light" or "dark" or "Light" or "Dark") || comparison[1] is not ("=" or "==" or "!=")) throw NssThemeCompiler.Error("Invalid mode condition.");
                bool dark = expected.Equals("dark", StringComparison.OrdinalIgnoreCase);
                return context => comparison[1] == "!=" ? context.Environment.IsDark != dark : context.Environment.IsDark == dark;
            }
            if (comparison[0] == "platform")
            {
                if (expected is not ("windows" or "linux" or "macos" or "Windows" or "Linux" or "macOS" or "unknown") || comparison[1] is not ("=" or "==" or "!=")) throw NssThemeCompiler.Error("Invalid platform condition.");
                return context => string.Equals(context.Environment.Platform, expected, StringComparison.OrdinalIgnoreCase) != (comparison[1] == "!=");
            }
            if (NssThemeCompiler.Call(comparison[0], "option", out string option))
            {
                option = NssThemeCompiler.Unquote(option);
                if (!inputs.Options.TryGetValue(option, out string? literal)) throw NssThemeCompiler.Error("Unknown condition option.");
                object value = Scalar(literal), boundary = Scalar(comparison[2]);
                bool result;
                if (comparison[1] is "=" or "==" or "!=") result = Equals(value, boundary) != (comparison[1] == "!=");
                else if (value is double number && boundary is double bound) result = Compare(number, bound, comparison[1]);
                else throw NssThemeCompiler.Error("Ordered option conditions require numeric scalar values.");
                return _ => result;
            }
            throw NssThemeCompiler.Error("Unsupported condition identifier.");
        }
    }
    private static object Scalar(string literal)
    {
        literal = literal.Trim();
        if (literal.Length >= 2 && literal[0] is '\'' or '"') return NssThemeCompiler.UnquoteText(literal);
        if (bool.TryParse(literal, out bool boolean)) return boolean;
        if (double.TryParse(literal, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number))
        {
            if (!double.IsFinite(number) || Math.Abs(number) > 1e12) throw NssThemeCompiler.Error("Condition number budget exceeded.");
            return number;
        }
        return NssThemeCompiler.UnquoteText(literal);
    }
    private static string NormalizeOperators(string expression)
    {
        System.Text.StringBuilder output = new(); char quote = '\0';
        for (int i = 0; i < expression.Length; i++)
        {
            char c = expression[i];
            if (quote != '\0') { output.Append(c); if (c == '\\' && i + 1 < expression.Length) output.Append(expression[++i]); else if (c == quote) quote = '\0'; continue; }
            if (c is '\'' or '"') { quote = c; output.Append(c); continue; }
            if (c == '&' && i + 1 < expression.Length && expression[i + 1] == '&') { output.Append(" and "); i++; }
            else if (c == '|' && i + 1 < expression.Length && expression[i + 1] == '|') { output.Append(" or "); i++; }
            else if (c == '!' && (i + 1 >= expression.Length || expression[i + 1] != '=')) output.Append(" not ");
            else output.Append(c);
        }
        return output.ToString();
    }

    private static bool Compare(double value, double boundary, string operation) => operation switch
    { "=" or "==" => value == boundary, "!=" => value != boundary, ">" => value > boundary, "<" => value < boundary, ">=" => value >= boundary, "<=" => value <= boundary, _ => false };
    private static string[] Comparison(string expression)
    {
        int depth = 0; char quote = '\0';
        for (int i = 0; i < expression.Length; i++)
        {
            char c = expression[i];
            if (quote != '\0') { if (c == '\\') i++; else if (c == quote) quote = '\0'; continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && c is '=' or '!' or '>' or '<')
            {
                int length = i + 1 < expression.Length && expression[i + 1] == '=' ? 2 : 1;
                string left = expression[..i].Trim(), operation = expression.Substring(i, length), right = expression[(i + length)..].Trim();
                if (left.Length == 0 || right.Length == 0 || operation == "!") throw NssThemeCompiler.Error("Invalid comparison.");
                return [left, operation, right];
            }
        }
        if (NssThemeCompiler.Call(expression, "option", out _)) return [expression, "=", "true"];
        throw NssThemeCompiler.Error("Condition requires a finite comparison.");
    }
    private static int Logical(string value, string operation)
    {
        int depth = 0; char quote = '\0';
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (quote != '\0') { if (c == '\\') i++; else if (c == quote) quote = '\0'; continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && value.AsSpan(i).StartsWith(operation, StringComparison.Ordinal)) return i;
        }
        return -1;
    }
    private static bool OuterGroup(string value)
    {
        int depth = 0; char quote = '\0';
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (quote != '\0') { if (c == '\\') i++; else if (c == quote) quote = '\0'; continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c == '(') depth++; else if (c == ')' && --depth == 0) return i == value.Length - 1;
        }
        return false;
    }
}

internal sealed class ThemeSelector
{
    private sealed record Part(string? Kind, string? Key, string? Role, string[] Classes, XsrUiThemeInteractionState States);
    private readonly Part[] _parts;
    private readonly bool[] _direct;
    public int Specificity { get; }
    private ThemeSelector(Part[] parts, bool[] direct)
    { _parts = parts; _direct = direct; Specificity = parts.Sum(part => (part.Key is null ? 0 : 100) + (part.Role is null ? 0 : 10) + part.Classes.Length * 10 + System.Numerics.BitOperations.PopCount((uint)part.States) * 10 + (part.Kind is null ? 0 : 1)); }
    public static ThemeSelector Parse(string selector)
    {
        if (selector.Length is 0 or > 1024) throw NssThemeCompiler.Error("Selector budget exceeded.");
        List<Part> parts = []; List<bool> direct = []; int position = 0;
        while (position < selector.Length)
        {
            while (position < selector.Length && char.IsWhiteSpace(selector[position])) position++;
            bool immediate = position < selector.Length && selector[position] == '>';
            if (immediate) { if (parts.Count == 0) throw NssThemeCompiler.Error("Selector cannot start with a combinator."); position++; while (position < selector.Length && char.IsWhiteSpace(selector[position])) position++; }
            if (position == selector.Length) throw NssThemeCompiler.Error("Trailing selector combinator.");
            int start = position, brackets = 0; char quote = '\0';
            while (position < selector.Length)
            {
                char c = selector[position];
                if (quote != '\0') { if (c == quote) quote = '\0'; position++; continue; }
                if (c is '\'' or '"') { quote = c; position++; continue; }
                if (c == '[') brackets++; else if (c == ']') brackets--;
                if (brackets == 0 && (char.IsWhiteSpace(c) || c == '>')) break;
                position++;
            }
            if (parts.Count >= 8 || brackets != 0 || quote != '\0') throw NssThemeCompiler.Error("Invalid or excessive selector ancestry.");
            if (parts.Count != 0) direct.Add(immediate);
            parts.Add(ParsePart(selector[start..position]));
        }
        if (parts.Count == 0 || parts.Take(parts.Count - 1).Any(part => part.States != XsrUiThemeInteractionState.None))
            throw NssThemeCompiler.Error("Interaction states are only allowed on the target selector.");
        return new(parts.ToArray(), direct.ToArray());
    }
    private static Part ParsePart(string text)
    {
        string? kind = null, key = null, role = null; List<string> classes = []; XsrUiThemeInteractionState states = default; int position = 0;
        if (text[0] == '*') position++;
        else if (char.IsAsciiLetter(text[0])) kind = ReadIdentifier(text, ref position);
        while (position < text.Length)
        {
            char prefix = text[position++];
            if (prefix == '.') { string value = ReadIdentifier(text, ref position); if (classes.Contains(value)) throw NssThemeCompiler.Error("Duplicate selector class."); classes.Add(value); }
            else if (prefix == '#') { if (key is not null) throw NssThemeCompiler.Error("Duplicate selector key."); key = ReadIdentifier(text, ref position); }
            else if (prefix == ':')
            {
                string name = ReadIdentifier(text, ref position);
                var state = name switch { "hover" => XsrUiThemeInteractionState.Hover, "pressed" => XsrUiThemeInteractionState.Pressed, "focus" => XsrUiThemeInteractionState.Focus, "focus-visible" => XsrUiThemeInteractionState.FocusVisible, "selected" => XsrUiThemeInteractionState.Selected, "disabled" => XsrUiThemeInteractionState.Disabled, "checked" => XsrUiThemeInteractionState.Checked, _ => throw NssThemeCompiler.Error("Unsupported interaction pseudo class.") };
                if ((states & state) != 0) throw NssThemeCompiler.Error("Duplicate interaction state."); states |= state;
            }
            else if (prefix == '[')
            {
                int end = text.IndexOf(']', position); if (end < 0) throw NssThemeCompiler.Error("Unclosed selector attribute.");
                string[] pieces = text[position..end].Split('=', 2, StringSplitOptions.TrimEntries);
                if (pieces.Length != 2 || pieces[1].Length == 0) throw NssThemeCompiler.Error("Invalid selector attribute.");
                string value = NssThemeCompiler.Unquote(pieces[1]);
                if (value.Length > 128 || value.Any(char.IsControl)) throw NssThemeCompiler.Error("Invalid selector identity.");
                if (pieces[0] == "key" && key is null) key = value;
                else if (pieces[0] == "role" && role is null && Enum.TryParse(value, out XsrUiSemanticRole parsed) && Enum.IsDefined(parsed) && parsed.ToString() == value) role = value;
                else throw NssThemeCompiler.Error("Only key and finite role selector attributes are supported.");
                position = end + 1;
            }
            else throw NssThemeCompiler.Error("Unsupported selector syntax.");
        }
        return new(kind, key, role, classes.ToArray(), states);
    }
    private static string ReadIdentifier(string text, ref int position)
    {
        int start = position; while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '_' or '-')) position++;
        string value = text[start..position]; if (value.Length is 0 or > 128) throw NssThemeCompiler.Error("Invalid selector identifier."); return value;
    }
    public bool Matches(XsrUiThemeStyleContext context)
    {
        if (!Match(_parts[^1], context.Identity) || (context.InteractionState & _parts[^1].States) != _parts[^1].States) return false;
        int ancestor = 0;
        for (int part = _parts.Length - 2; part >= 0; part--)
        {
            if (_direct[part]) { if (ancestor >= context.Parents.Count || !Match(_parts[part], context.Parents[ancestor++])) return false; }
            else { while (ancestor < context.Parents.Count && !Match(_parts[part], context.Parents[ancestor])) ancestor++; if (ancestor++ >= context.Parents.Count) return false; }
        }
        return true;
    }
    private static bool Match(Part part, XsrUiStyleIdentity identity) => (part.Kind is null || part.Kind == identity.Kind) && (part.Key is null || part.Key == identity.Key)
        && (part.Role is null || part.Role == identity.Role) && part.Classes.All(value => identity.Classes.Contains(value, StringComparer.Ordinal));
}
