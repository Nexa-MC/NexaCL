using System.Text;

#if NEXA_NATIVE_ENGINE
namespace Nexa.Native.Markup;
#else
namespace Nexa.Xsr.Renderer.Nxml;
#endif

internal static class NxmlTextFormat
{
    internal readonly record struct Part(string Value, bool Parameter);

    internal static List<Part> Parse(string source)
    {
        if (source.Length > 1024 * 1024) throw new NxmlParseException("Dynamic text exceeds its format budget.");
        List<Part> parts = [];
        int start = 0, parameters = 0;
        while (start < source.Length)
        {
            int relativeBrace = source.AsSpan(start).IndexOfAny('{', '}');
            int brace = relativeBrace < 0 ? -1 : start + relativeBrace;
            if (brace < 0) { parts.Add(new(source[start..], false)); break; }
            if (brace > start) parts.Add(new(source[start..brace], false));
            if (!source.AsSpan(brace).StartsWith("{data ", StringComparison.Ordinal))
                throw new NxmlParseException("Dynamic text permits only named data placeholders.");
            int close = source.IndexOf('}', brace);
            if (close < 0) throw new NxmlParseException("Dynamic text contains an unclosed placeholder.");
            string name = source[(brace + 6)..close];
            if (name.Length is 0 or > 128 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
                throw new NxmlParseException("Dynamic text needs simple parameter names.");
            if (++parameters > 256) throw new NxmlParseException("Dynamic text exceeds its placeholder budget.");
            parts.Add(new(name, true)); start = close + 1;
        }
        return parts;
    }

#if !NEXA_NATIVE_ENGINE
    internal static string Expand(string source, IReadOnlyDictionary<string, string> data, Func<string, string>? localize, ref long remaining)
    {
        var parts = Parse(source);
        if (localize is not null)
        {
            string localized = localize(source);
            if (!string.Equals(localized, source, StringComparison.Ordinal))
            {
                var translated = Parse(localized);
                var parameters = parts.Where(part => part.Parameter).Select(part => part.Value).ToHashSet(StringComparer.Ordinal);
                if (!parameters.SetEquals(translated.Where(part => part.Parameter).Select(part => part.Value)))
                    throw new NxmlCompileException("Localized dynamic text must preserve its named parameters.");
                parts = translated;
            }
        }
        StringBuilder output = new();
        foreach (var part in parts)
        {
            string value = part.Value;
            if (part.Parameter && !data.TryGetValue(part.Value, out value!))
                throw new NxmlCompileException($"Missing dynamic parameter '{part.Value}'.");
            remaining -= value.Length;
            if (remaining < 0) throw new NxmlCompileException("Expanded dynamic data exceeds its budget.");
            output.Append(value);
        }
        return output.ToString();
    }
#endif
}
