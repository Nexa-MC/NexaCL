using System.Globalization;

namespace Nexa.Services.Themes;

/// <summary>Finite, bounded declaration evaluator shared by admission and host-authored option forms.</summary>
public static class ThemeOptionCondition
{
    public static bool Evaluate(string? expression, IReadOnlyDictionary<string, ThemeOptionValue> values)
    {
        ArgumentNullException.ThrowIfNull(values); if (string.IsNullOrWhiteSpace(expression)) return true;
        if (expression.Length > 4096) throw new InvalidDataException("Option condition exceeds its budget.");
        return new Parser(expression, values).Read();
    }
    private sealed class Parser(string source, IReadOnlyDictionary<string, ThemeOptionValue> values)
    {
        private int _position;
        private int _operations;
        private int _depth;
        internal bool Read() { bool result = Or(); Space(); if (_position != source.Length) throw Invalid(); return result; }
        private bool Or() { bool result = And(); while (Take("||")) { bool right = And(); result |= right; } return result; }
        private bool And() { bool result = Boolean(); while (Take("&&")) { bool right = Boolean(); result &= right; } return result; }
        private bool Boolean()
        {
            if (++_operations > 512 || ++_depth > 16) throw Invalid();
            try
            {
                if (Take("!")) return !Boolean();
                if (Take("(")) { bool nested = Or(); if (!Take(")")) throw Invalid(); return nested; }
                object left = Value();
                if (Take("==")) return Equals(left, Value());
                if (Take("!=")) return !Equals(left, Value());
                return left is bool boolean ? boolean : throw Invalid();
            }
            finally { _depth--; }
        }
        private object Value()
        {
            if (++_operations > 512) throw Invalid(); Space();
            if (Take("option("))
            {
                Space(); string id;
                if (_position < source.Length && source[_position] is '\'' or '"') id = Quoted();
                else { int start = _position; while (_position < source.Length && (char.IsAsciiLetterOrDigit(source[_position]) || source[_position] is '.' or '-' or '_')) _position++; id = source[start.._position]; }
                if (!Take(")") || !values.TryGetValue(id, out var value)) throw Invalid();
                return value.Kind switch
                {
                    ThemeOptionKind.Boolean when value.Value is "true" or "false" => value.Value == "true",
                    ThemeOptionKind.Number or ThemeOptionKind.Range when double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number) => number,
                    ThemeOptionKind.Files => throw Invalid(),
                    _ => value.Value
                };
            }
            if (Take("true")) return true; if (Take("false")) return false;
            if (_position < source.Length && source[_position] is '\'' or '"') return Quoted();
            int first = _position; while (_position < source.Length && (char.IsAsciiDigit(source[_position]) || source[_position] is '.' or '-' or '+' or 'e' or 'E')) _position++;
            if (first == _position || !double.TryParse(source.AsSpan(first, _position - first), NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw Invalid();
            return result;
        }
        private string Quoted()
        {
            char quote = source[_position++]; int start = _position;
            while (_position < source.Length && source[_position] != quote) { if (source[_position] == '\\' || char.IsControl(source[_position])) throw Invalid(); _position++; }
            if (_position >= source.Length) throw Invalid(); string value = source[start.._position]; _position++; return value;
        }
        private bool Take(string token) { Space(); if (!source.AsSpan(_position).StartsWith(token, StringComparison.Ordinal)) return false; _position += token.Length; return true; }
        private void Space() { while (_position < source.Length && char.IsWhiteSpace(source[_position])) _position++; }
        private static InvalidDataException Invalid() => new("Invalid option condition. Only bounded boolean, equality and option expressions are supported.");
    }
}
