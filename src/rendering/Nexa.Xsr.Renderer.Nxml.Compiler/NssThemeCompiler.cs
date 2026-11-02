using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>Finite inputs copied at compilation. Text option literals may be quoted; no executable expressions.</summary>
public sealed record NssThemeCompileInputs
{
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> ResourceBindings { get; init; } = new Dictionary<string, string>();
    public string Platform { get; init; } = "unknown";
    public string Mode { get; init; } = "Light";
    public IReadOnlySet<string> Capabilities { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

public enum NssThemeResourceKind { Asset, File, Folder }
public sealed record NssThemeResourceReference(NssThemeResourceKind Kind, string Value, string Expression);

/// <summary>NSS/1 compilation is bounded and validates unmatched rules and unused tokens/keyframes.</summary>
public static class NssThemeCompiler
{
    public static NssThemeStyleProgram Compile(string source, NssThemeCompileInputs? inputs = null, long revision = 1) =>
        Compile([source], inputs ?? new(), revision);

    public static NssThemeStyleProgram Compile(IReadOnlyList<string> sources, NssThemeCompileInputs inputs, long revision = 1)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(inputs);
        if (sources.Count is < 1 or > 64 || sources.Any(source => source is null) || sources.Sum(source => (long)Encoding.UTF8.GetByteCount(source)) > 1024 * 1024)
            throw new NxmlCompileException("NSS/1 source budget exceeded.");
        if (revision < 0 || inputs.Options.Count > 256 || inputs.ResourceBindings.Count > 4096)
            throw new NxmlCompileException("NSS/1 input budget exceeded.");
        foreach (var option in inputs.Options)
            if (!Identifier(option.Key) || option.Value is null || option.Value.Length > 4096 || option.Value.Any(char.IsControl))
                throw new NxmlCompileException("Invalid NSS/1 option input.");
        foreach (var binding in inputs.ResourceBindings)
            if (binding.Key.Length > 4096 || binding.Value is null || binding.Value.Length is 0 or > 4096 || binding.Value.Any(char.IsControl))
                throw new NxmlCompileException("Invalid NSS/1 resource binding.");
        if (inputs.Capabilities.Count > 256 || inputs.Capabilities.Any(value => !Identifier(value)))
            throw new NxmlCompileException("Invalid NSS/1 capability input.");
        var snapshot = inputs with
        {
            Options = new ReadOnlyDictionary<string, string>(inputs.Options.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
            ResourceBindings = new ReadOnlyDictionary<string, string>(inputs.ResourceBindings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
            Capabilities = new HashSet<string>(inputs.Capabilities, StringComparer.Ordinal),
        };
        var parser = new ThemeParser(snapshot);
        foreach (string source in sources) parser.Read(source);
        return parser.Finish(revision);
    }

    /// <summary>Shared host-side finite condition evaluation, suitable for manifest option visibility.</summary>
    public static bool EvaluateCondition(string expression, NssThemeCompileInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Options.Count > 256 || inputs.Options.Any(pair => !Identifier(pair.Key) || pair.Value is null || pair.Value.Length > 4096 || pair.Value.Any(char.IsControl)))
            throw new NxmlCompileException("Invalid NSS/1 condition input.");
        return ThemeCondition.Parse(expression, inputs, ConditionKind.When).Matches(new(default,
            new XsrUiStyleIdentity("Node", null, []), [], XsrUiThemeInteractionState.None, default,
            new XsrUiThemeEnvironment(inputs.Platform, inputs.Mode.Equals("dark", StringComparison.OrdinalIgnoreCase), Capabilities: inputs.Capabilities), default));
    }

    internal static bool Identifier(string value) => value.Length is > 0 and <= 128
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    private sealed class ThemeParser(NssThemeCompileInputs inputs)
    {
        private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
        private readonly List<RawRule> _rules = [];
        private readonly Dictionary<string, IReadOnlyList<RawFrame>> _keyframes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);
        private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NssThemeResourceReference> _resources = new(StringComparer.Ordinal);
        private int _declarationCount;

        public void Read(string source) => ReadBlocks(new Cursor(Cursor.RemoveComments(source)), [], 0);

        private void ReadBlocks(Cursor cursor, IReadOnlyList<ThemeCondition> conditions, int depth)
        {
            if (depth > 16) throw Error("Condition nesting budget exceeded.");
            while (!cursor.End)
            {
                string header = cursor.ReadUntil('{', ';');
                if (header.Length == 0) throw Error("A block requires a selector or directive.");
                char delimiter = cursor.Take();
                if (delimiter == ';')
                {
                    if (!header.StartsWith("@token ", StringComparison.Ordinal) || depth != 0) throw Error("Only document-scoped @token accepts a statement.");
                    AddToken(header[7..]);
                    continue;
                }
                string body = cursor.ReadBlock();
                if (header == "@tokens")
                {
                    if (depth != 0) throw Error("Tokens must be declared at document scope.");
                    foreach (string declaration in Split(body, ';')) AddToken(declaration);
                }
                else if (header.StartsWith("@keyframes ", StringComparison.Ordinal))
                {
                    if (depth != 0) throw Error("Keyframes must be declared at document scope.");
                    string name = header[11..].Trim();
                    if (!Identifier(name) || _keyframes.Count >= 128 || _keyframes.ContainsKey(name)) throw Error("Invalid or duplicate keyframe identity.");
                    Cursor frames = new(body);
                    List<RawFrame> items = [];
                    while (!frames.End)
                    {
                        string position = frames.ReadUntil('{'); frames.Take();
                        double progress = position == "from" ? 0 : position == "to" ? 1 : position.EndsWith('%') ? Number(position[..^1], 0, 100) / 100 : throw Error("Keyframe position requires 0..100%.");
                        if (items.Any(item => item.Position == progress) || items.Count >= 64) throw Error("Duplicate keyframe position or frame budget exceeded.");
                        items.Add(new(progress, Declarations(frames.ReadBlock())));
                    }
                    if (items.Count < 2 || !items.Any(item => item.Position == 0) || !items.Any(item => item.Position == 1))
                        throw Error("Keyframes require distinct 0% and 100% endpoints.");
                    _keyframes.Add(name, items.OrderBy(item => item.Position).ToArray());
                }
                else if (header.StartsWith('@'))
                {
                    ThemeCondition condition = header.StartsWith("@when ", StringComparison.Ordinal) ? ThemeCondition.Parse(header[6..], inputs, ConditionKind.When)
                        : header.StartsWith("@supports ", StringComparison.Ordinal) ? ThemeCondition.Parse(header[10..], inputs, ConditionKind.Supports)
                        : header.StartsWith("@viewport ", StringComparison.Ordinal) ? ThemeCondition.Parse(header[10..], inputs, ConditionKind.Viewport)
                        : throw Error("Unsupported NSS/1 directive.");
                    ReadBlocks(new Cursor(body), [.. conditions, condition], depth + 1);
                }
                else
                {
                    IReadOnlyList<RawDeclaration> declarations = Declarations(body);
                    foreach (string selector in Split(header, ','))
                    {
                        if (_rules.Count >= 4096) throw Error("Rule budget exceeded.");
                        _rules.Add(new(ThemeSelector.Parse(selector), declarations, conditions.ToArray(), _rules.Count));
                    }
                }
            }
        }

        private void AddToken(string declaration)
        {
            var parsed = Declaration(declaration);
            string name = parsed.Name.TrimStart('-');
            if (!Identifier(name) || _tokens.Count >= 512 || !_tokens.TryAdd(name, parsed.Value)) throw Error("Invalid or duplicate token identity.");
        }

        private RawDeclaration[] Declarations(string body)
        {
            var declarations = Split(body, ';').Select(Declaration).ToArray();
            _declarationCount += declarations.Length;
            if (declarations.Length > 128 || _declarationCount > 32768) throw Error("Declaration budget exceeded.");
            return declarations;
        }

        private static RawDeclaration Declaration(string text)
        {
            int colon = FindTop(text, ':');
            if (colon <= 0 || colon == text.Length - 1) throw Error("Invalid declaration.");
            string name = text[..colon].Trim(), value = text[(colon + 1)..].Trim();
            if (name.Length is 0 or > 128 || value.Length > 4096 || value.Length == 0) throw Error("Declaration value budget exceeded.");
            return new(name, value);
        }

        private string Resolve(string expression, int depth = 0)
        {
            if (depth > 32) throw Error("Token expression depth exceeded.");
            expression = expression.Trim();
            if (expression.Any(char.IsControl) || expression.Contains('{') || expression.Contains('}')) throw Error("Invalid declaration literal.");
            if (Call(expression, "var", out string variable))
            {
                variable = variable.TrimStart('-');
                if (!Identifier(variable) || !_tokens.TryGetValue(variable, out string? tokenExpression)) throw Error("Unknown token reference.");
                if (_resolved.TryGetValue(variable, out string? found)) return found;
                if (!_resolving.Add(variable)) throw Error("Cyclic token reference.");
                string value = Resolve(tokenExpression, depth + 1);
                _resolving.Remove(variable); _resolved.Add(variable, value); return value;
            }
            if (Call(expression, "option", out string option))
            {
                option = Unquote(option);
                if (!Identifier(option) || !inputs.Options.TryGetValue(option, out string? value)) throw Error("Unknown option reference.");
                // Resource-valued options are admitted declarations, never arbitrary nested programs.
                if (Call(value, "asset", out _) || Call(value, "file", out _) || Call(value, "folder", out _)) return Resolve(value, depth + 1);
                bool quoted = value.Length >= 2 && value[0] is '\'' or '"' && value[^1] == value[0];
                if (!quoted && (value.StartsWith('[') || value.Contains('(') || value.Contains('{') || value.Contains(';'))) throw Error("Option values cannot contain expressions.");
                return value;
            }
            foreach (var pair in new[] { (Name: "asset", Kind: NssThemeResourceKind.Asset), (Name: "file", Kind: NssThemeResourceKind.File), (Name: "folder", Kind: NssThemeResourceKind.Folder) })
                if (Call(expression, pair.Name, out string reference))
                {
                    reference = Unquote(reference);
                    if (reference.Length is 0 or > 4096 || reference.Any(char.IsControl)) throw Error("Invalid resource reference.");
                    if (pair.Kind == NssThemeResourceKind.Asset && (Path.IsPathRooted(reference) || reference.Contains('\\') || reference.Split('/').Any(part => part is "" or "." or "..")))
                        throw Error("Package resource paths must be relative and normalized.");
                    string canonical = pair.Name + "(\"" + reference + "\")";
                    _resources.TryAdd(canonical, new(pair.Kind, reference, canonical));
                    if (_resources.Count > 4096) throw Error("Resource reference budget exceeded.");
                    return inputs.ResourceBindings.TryGetValue(canonical, out string? id) && id is not null ? id : canonical;
                }
            if (expression.Contains("var(", StringComparison.Ordinal) || expression.Contains("option(", StringComparison.Ordinal))
                throw Error("NSS/1 expressions must occupy the entire value.");
            return expression;
        }

        public NssThemeStyleProgram Finish(long revision)
        {
            foreach (string token in _tokens.Keys)
            {
                string literal = Resolve("var(" + token + ")");
                if (literal is "NaN" or "Infinity" or "-Infinity" or "+Infinity" || literal.Any(char.IsControl)
                    || literal.Contains('{') || literal.Contains(';')) throw Error("Invalid token literal.");
                if (double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric) && (!double.IsFinite(numeric) || Math.Abs(numeric) > 100000))
                    throw Error("Token numeric budget exceeded.");
            }
            Dictionary<string, IReadOnlyDictionary<XsrUiStyleAnimationProperty, IReadOnlyList<XsrUiStyleKeyframe>>> animations = new(StringComparer.Ordinal);
            foreach (var pair in _keyframes)
            {
                Dictionary<XsrUiStyleAnimationProperty, List<XsrUiStyleKeyframe>> properties = [];
                foreach (RawFrame frame in pair.Value)
                    foreach (RawDeclaration declaration in frame.Declarations)
                    {
                        string name = Normalize(declaration.Name);
                        if (!Enum.TryParse(name, out XsrUiStyleAnimationProperty property) || !Enum.IsDefined(property)) throw Error("Unsupported animated property.");
                        double value = Number(Resolve(declaration.Value), property == XsrUiStyleAnimationProperty.Opacity ? 0 : property is XsrUiStyleAnimationProperty.ScaleX or XsrUiStyleAnimationProperty.ScaleY ? .01 : property is XsrUiStyleAnimationProperty.CornerRadius or XsrUiStyleAnimationProperty.BlurRadius ? 0 : -100000,
                            property == XsrUiStyleAnimationProperty.Opacity ? 1 : property is XsrUiStyleAnimationProperty.ScaleX or XsrUiStyleAnimationProperty.ScaleY ? 100 : property is XsrUiStyleAnimationProperty.CornerRadius or XsrUiStyleAnimationProperty.BlurRadius ? 4096 : 100000);
                        if (!properties.TryGetValue(property, out var frames)) properties.Add(property, frames = []);
                        if (frames.Any(item => item.Position == frame.Position)) throw Error("Duplicate animated declaration.");
                        frames.Add(new(frame.Position, value));
                    }
                if (properties.Count == 0 || properties.Values.Any(frames => frames[0].Position != 0 || frames[^1].Position != 1))
                    throw Error("Each animated property requires both endpoints.");
                animations.Add(pair.Key, new ReadOnlyDictionary<XsrUiStyleAnimationProperty, IReadOnlyList<XsrUiStyleKeyframe>>(properties.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<XsrUiStyleKeyframe>)Array.AsReadOnly(pair.Value.ToArray()))));
            }
            List<ThemeRule> compiled = [];
            int expandedDeclarations = 0;
            foreach (RawRule rule in _rules.OrderBy(rule => rule.Selector.Specificity).ThenBy(rule => rule.Ordinal))
            {
                expandedDeclarations += rule.Declarations.Count;
                if (expandedDeclarations > 32768) throw Error("Expanded declaration budget exceeded.");
                List<ThemeDeclaration> declarations = [];
                foreach (var declaration in rule.Declarations)
                    declarations.Add(ThemeDeclaration.Compile(Normalize(declaration.Name), Resolve(declaration.Value), animations));
                XsrUiComputedStyle ruleStyle = XsrUiComputedStyle.Empty;
                foreach (ThemeDeclaration declaration in declarations) ruleStyle = declaration.Apply(ruleStyle);
                if (ruleStyle.MinWidth is { } minWidth && ruleStyle.MaxWidth is { } maxWidth && minWidth > maxWidth
                    || ruleStyle.MinHeight is { } minHeight && ruleStyle.MaxHeight is { } maxHeight && minHeight > maxHeight)
                    throw Error("A rule cannot declare contradictory size bounds.");
                compiled.Add(new(rule.Selector, declarations.ToArray(), rule.Conditions));
            }
            return new(revision, compiled.ToArray(), Array.AsReadOnly(_resources.Values.ToArray()));
        }
    }

    private sealed record RawDeclaration(string Name, string Value);
    private sealed record RawFrame(double Position, IReadOnlyList<RawDeclaration> Declarations);
    private sealed record RawRule(ThemeSelector Selector, IReadOnlyList<RawDeclaration> Declarations, IReadOnlyList<ThemeCondition> Conditions, int Ordinal);

    internal static NxmlCompileException Error(string message) => new("NSS/1: " + message);
    internal static double Number(string value, double minimum = 0, double maximum = 100000)
    {
        if (value.EndsWith("px", StringComparison.Ordinal)) value = value[..^2];
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number) || number < minimum || number > maximum)
            throw Error("Invalid bounded numeric value.");
        return number;
    }
    internal static string Normalize(string name)
    {
        if (!name.Contains('-')) return name == "color" ? "Foreground" : char.ToUpperInvariant(name[0]) + name[1..];
        string normalized = string.Concat(name.Split('-').Select(part => part.Length == 0 ? "" : char.ToUpperInvariant(part[0]) + part[1..]));
        return normalized switch
        {
            "BackgroundColor" => "Background",
            "Color" => "Foreground",
            "BorderColor" => "Border",
            "BorderRadius" => "CornerRadius",
            "GridTemplateColumns" => "GridColumns",
            "GridTemplateRows" => "GridRows",
            "BoxShadow" => "Shadow",
            _ => normalized,
        };
    }
    internal static bool Call(string value, string function, out string argument)
    {
        string prefix = function + "(";
        if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(')'))
        { argument = value[prefix.Length..^1].Trim(); return true; }
        argument = ""; return false;
    }
    internal static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] is '\'' or '"' && value[^1] == value[0]) return value[1..^1];
        return value;
    }
    internal static string UnquoteText(string value)
    {
        value = value.Trim();
        if (value.Length > 4096) throw Error("Text literal budget exceeded.");
        string literal;
        if (value.Length >= 2 && value[0] == '"')
        {
            try
            {
                using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 1 });
                if (document.RootElement.ValueKind != JsonValueKind.String) throw Error("Invalid quoted text literal.");
                literal = document.RootElement.GetString()!;
            }
            catch (JsonException) { throw Error("Invalid quoted text literal."); }
        }
        else literal = Unquote(value);
        if (literal.Length > 4096 || literal.Any(char.IsControl)) throw Error("Invalid bounded text literal.");
        return literal;
    }
    internal static int FindTop(string value, char target)
    {
        int depth = 0; char quote = '\0';
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; else if (c == '\\') i++; continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') { if (--depth < 0) throw Error("Unbalanced expression."); }
            else if (c == target && depth == 0) return i;
        }
        if (quote != '\0' || depth != 0) throw Error("Unbalanced expression.");
        return -1;
    }
    internal static string[] Split(string value, char delimiter)
    {
        List<string> result = []; int position = 0;
        while (position < value.Length)
        {
            int offset = FindTop(value[position..], delimiter);
            int end = offset < 0 ? value.Length : position + offset;
            string item = value[position..end].Trim();
            if (item.Length != 0) result.Add(item);
            position = end + 1;
        }
        return result.ToArray();
    }

    private sealed class Cursor(string source)
    {
        private int _position;
        public bool End { get { Skip(); return _position == source.Length; } }
        public char Take() => _position < source.Length ? source[_position++] : throw Error("Unexpected end of source.");
        public string ReadUntil(params char[] terminators)
        {
            Skip(); int start = _position, depth = 0; char quote = '\0';
            while (_position < source.Length)
            {
                char c = source[_position];
                if (quote != '\0') { _position++; if (c == quote) quote = '\0'; else if (c == '\\') _position++; continue; }
                if (c is '\'' or '"') { quote = c; _position++; continue; }
                if (c is '(' or '[') depth++;
                else if (c is ')' or ']') { if (--depth < 0) throw Error("Unbalanced expression."); }
                if (depth == 0 && terminators.Contains(c)) return source[start.._position].Trim();
                _position++;
            }
            throw Error("Unclosed statement or rule.");
        }
        public string ReadBlock()
        {
            int start = _position, depth = 1; char quote = '\0';
            while (_position < source.Length)
            {
                char c = source[_position++];
                if (quote != '\0') { if (c == quote) quote = '\0'; else if (c == '\\') _position++; continue; }
                if (c is '\'' or '"') { quote = c; continue; }
                if (c == '/' && _position < source.Length && source[_position] == '*')
                { int end = source.IndexOf("*/", _position + 1, StringComparison.Ordinal); if (end < 0) throw Error("Unclosed comment."); _position = end + 2; continue; }
                if (c == '{') { if (++depth > 32) throw Error("Block nesting budget exceeded."); }
                else if (c == '}' && --depth == 0) return RemoveComments(source[start..(_position - 1)]);
            }
            throw Error("Unclosed block.");
        }
        private void Skip()
        {
            while (_position < source.Length)
            {
                if (char.IsWhiteSpace(source[_position])) { _position++; continue; }
                if (source.AsSpan(_position).StartsWith("/*", StringComparison.Ordinal))
                { int end = source.IndexOf("*/", _position + 2, StringComparison.Ordinal); if (end < 0) throw Error("Unclosed comment."); _position = end + 2; continue; }
                return;
            }
        }
        internal static string RemoveComments(string body)
        {
            StringBuilder result = new(); char quote = '\0';
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (quote != '\0') { result.Append(c); if (c == quote) quote = '\0'; else if (c == '\\' && i + 1 < body.Length) result.Append(body[++i]); continue; }
                if (c is '\'' or '"') { quote = c; result.Append(c); continue; }
                if (c == '/' && i + 1 < body.Length && body[i + 1] == '*')
                { int end = body.IndexOf("*/", i + 2, StringComparison.Ordinal); if (end < 0) throw Error("Unclosed comment."); i = end + 1; result.Append(' '); continue; }
                result.Append(c);
            }
            return result.ToString();
        }
    }
}
