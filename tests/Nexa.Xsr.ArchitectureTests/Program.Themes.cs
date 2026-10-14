using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nexa.Xsr.ArchitectureTests;

internal static partial class Program
{
    // An additive presentation field requires a conscious boundary update here.
    // This does not authorize product factories or alter the ordinary NXML guard.
    private static readonly Dictionary<string, string> ThemeStylePropertyTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Width"] = "double?",
            ["Height"] = "double?",
            ["MinWidth"] = "double?",
            ["MaxWidth"] = "double?",
            ["MinHeight"] = "double?",
            ["MaxHeight"] = "double?",
            ["Weight"] = "double?",
            ["Margin"] = "XsrUiThickness?",
            ["Padding"] = "XsrUiThickness?",
            ["HorizontalAlignment"] = "XsrUiAlignment?",
            ["VerticalAlignment"] = "XsrUiAlignment?",
            ["Direction"] = "XsrUiOrientation?",
            ["Spacing"] = "double?",
            ["Background"] = "XsrUiColor?",
            ["Foreground"] = "XsrUiColor?",
            ["Border"] = "XsrUiColor?",
            ["Hover"] = "XsrUiColor?",
            ["Surface"] = "XsrUiSurfaceKind?",
            ["Opacity"] = "double?",
            ["CornerRadius"] = "double?",
            ["BorderWidth"] = "double?",
            ["BlurRadius"] = "double?",
            ["FontSize"] = "double?",
            ["FontWeight"] = "double?",
            ["FontFamily"] = "string?",
            ["TextAlignment"] = "XsrUiTextAlignment?",
            ["WrapText"] = "bool?",
            ["ImageSource"] = "string?",
            ["VirtualItemWidth"] = "double?",
            ["VirtualItemHeight"] = "double?",
            ["VirtualItemSpacing"] = "double?",
            ["VirtualItemColumns"] = "int?",
            ["Order"] = "int?",
            ["Position"] = "XsrUiThemePosition?",
            ["Left"] = "double?",
            ["Top"] = "double?",
            ["Right"] = "double?",
            ["Bottom"] = "double?",
            ["Grid"] = "XsrUiGridLayout?",
            ["GridColumn"] = "int?",
            ["GridRow"] = "int?",
            ["GridColumnSpan"] = "int?",
            ["GridRowSpan"] = "int?",
            ["Transform"] = "XsrUiStyleTransform?",
            ["Shadow"] = "XsrUiShadow?",
            ["BackgroundImage"] = "XsrUiBackgroundImage?",
            ["BackgroundGradient"] = "XsrUiLinearGradient?",
            ["Animations"] = "IReadOnlyList<XsrUiStyleAnimation>?",
        };

    private static List<string> ThemeStyleSurfaceErrors(string source)
    {
        List<string> errors = [];
        SyntaxNode document = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (RecordDeclarationSyntax record in document.DescendantNodes().OfType<RecordDeclarationSyntax>()
            .Where(record => record.Identifier.ValueText == "XsrUiComputedStyle"))
        {
            if (record.ParameterList is not null || record.BaseList is not null)
                errors.Add("Style snapshots cannot expose an unreviewed constructor or inherited behavior.");
            foreach (MemberDeclarationSyntax member in record.Members)
            {
                if (member is not PropertyDeclarationSyntax property)
                {
                    errors.Add("Style snapshots contain finite immutable properties only.");
                    continue;
                }
                string name = property.Identifier.ValueText;
                if (name == "Empty" && property.Modifiers.Any(SyntaxKind.StaticKeyword)
                    && property.Type.ToString() == "XsrUiComputedStyle"
                    && property.AccessorList is { Accessors.Count: 1 } emptyAccessors
                    && emptyAccessors.Accessors[0] is { Body: null, ExpressionBody: null } emptyGetter
                    && emptyGetter.IsKind(SyntaxKind.GetAccessorDeclaration)
                    && property.Initializer?.Value is ImplicitObjectCreationExpressionSyntax
                    {
                        ArgumentList.Arguments.Count: 0, Initializer: null,
                    }) continue;
                string type = property.Type.WithoutTrivia().ToFullString().Replace(" ", "", StringComparison.Ordinal);
                if (!ThemeStylePropertyTypes.TryGetValue(name, out string? expected) || type != expected)
                    errors.Add($"Style-only snapshots cannot expose '{name}' with type '{type}'.");
                if (property.AccessorList is null || property.ExpressionBody is not null || property.Initializer is not null
                    || property.AccessorList.Accessors.Any(accessor => accessor.Body is not null || accessor.ExpressionBody is not null
                        || accessor.Kind() is not (SyntaxKind.GetAccessorDeclaration or SyntaxKind.InitAccessorDeclaration)))
                    errors.Add($"Style-only property '{name}' must be an immutable data fact.");
            }
        }
        return errors;
    }

    private static void ValidateThemeArchitecture(string root, Dictionary<string, string> projects, List<string> failures)
    {
        ValidateThemeGuardFixtures(failures);
        string rendererTheme = Path.Combine(root, "Nexa.Xsr.Renderer", "XsrUiTheme.cs");
        if (!File.Exists(rendererTheme))
        {
            failures.Add("NTP/1 must expose a reviewed style-only renderer snapshot.");
            return;
        }
        foreach (string error in ThemeStyleSurfaceErrors(File.ReadAllText(rendererTheme)))
            failures.Add("NTP/1 style boundary: " + error);

        foreach (string project in new[] { "Nexa.Services.Themes.Contracts", "Nexa.Services.Themes" })
        {
            if (!projects.TryGetValue(project, out string? path)) continue;
            foreach (XElement reference in Elements(XDocument.Load(path), "ProjectReference"))
            {
                string target = Path.GetFileNameWithoutExtension(reference.Attribute("Include")?.Value) ?? "";
                if (target.StartsWith("Nexa.Xsr.Renderer", StringComparison.Ordinal)
                    || target is "Nexa.Desktop" || target.StartsWith("Avalonia", StringComparison.Ordinal))
                    failures.Add($"NTP/1 package service cannot depend on presentation: {project} -> {target}.");
            }
        }

        XDocument desktop = XDocument.Load(projects["Nexa.Desktop"]);
        if (!Elements(desktop, "Content").Any(content => content.Attribute("Include")?.Value.Replace('\\', '/').StartsWith("Themes/", StringComparison.Ordinal) == true
            && content.Attribute("CopyToOutputDirectory")?.Value == "PreserveNewest"
            && content.Attribute("CopyToPublishDirectory")?.Value == "PreserveNewest"))
            failures.Add("NTP/1 author themes must be external content in both build and publish outputs.");
        if (Elements(desktop, "EmbeddedResource").Any(content => content.Attribute("Include")?.Value.Replace('\\', '/').StartsWith("Themes/", StringComparison.Ordinal) == true))
            failures.Add("NTP/1 author resources cannot be embedded in Desktop.");

        foreach (string themeName in new[] { "BuiltIn", "Minimal", "Advanced" })
            ValidateAuthorTheme(root, themeName, failures);

        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "xsr-ci.yml"));
        foreach (string fragment in new[]
        {
            "tests/Nexa.Services.Themes.Tests/Nexa.Services.Themes.Tests.csproj",
            "theme-services-aot/Nexa.Services.Themes.Tests", "pcl-desktop-aot/Nexa.Desktop\" --validate-themes",
            "pcl-desktop-trimmed/Nexa.Desktop\" --validate-themes", "eng/themes/pack.py",
            "--verify-published", "test_*.py",
        })
            if (!workflow.Contains(fragment, StringComparison.Ordinal)) failures.Add($"NTP/1 CI gate is missing '{fragment}'.");
    }

    private static void ValidateAuthorTheme(string root, string themeName, List<string> failures)
    {
        string directory = Path.Combine(root, "Nexa.Desktop", "Themes", themeName);
        string manifestPath = Path.Combine(directory, "theme.json"), mimePath = Path.Combine(directory, "mimetype");
        if (!File.Exists(manifestPath) || !File.Exists(mimePath))
        {
            failures.Add($"NTP/1 author theme {themeName} is missing its manifest/mimetype.");
            return;
        }
        if (File.ReadAllText(mimePath).TrimEnd('\n') != "application/x-nexa-theme")
            failures.Add($"NTP/1 author theme {themeName} has an invalid mimetype.");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement value = manifest.RootElement;
        if (value.GetProperty("format").GetString() != "NTP/1" || value.GetProperty("styleApi").GetInt32() != 1
            || value.GetProperty("nss").GetString() != "NSS/1")
            failures.Add($"NTP/1 author theme {themeName} has an invalid format/API.");
        foreach (JsonElement style in value.GetProperty("styles").EnumerateArray())
        {
            string relative = style.GetString()!;
            string file = Path.GetFullPath(Path.Combine(directory, relative));
            if (!IsInsideRepository(directory, file) || !relative.EndsWith(".nss", StringComparison.Ordinal) || !File.Exists(file))
                failures.Add($"NTP/1 author theme {themeName} is missing its declared NSS.");
        }
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            if (Path.GetExtension(file) is ".nxml" or ".dll" or ".exe" or ".so" or ".dylib" or ".js" or ".py" or ".ps1")
                failures.Add($"NTP/1 author theme {themeName} contains prohibited executable/presentation content.");
    }

    private static void ValidateThemeGuardFixtures(List<string> failures)
    {
        string good = "public sealed record XsrUiComputedStyle { public double? Opacity { get; init; } public string? FontFamily { get; init; } }";
        if (ThemeStyleSurfaceErrors(good).Count != 0) failures.Add("NTP/1 style guard rejects immutable presentation facts.");
        foreach (string member in new[]
        {
            "public string? Command { get; init; }", "public bool? IsVisible { get; init; }",
            "public bool? Enabled { get; init; }", "public string? Recipe { get; init; }",
            "public object? Binding { get; init; }", "public Action? FontFamily { get; init; }",
            "public double? Opacity { get; set; }", "public double? Opacity { get { return 1; } init { } }",
            "public void Apply(object tree) { }", "public object Factory;",
            "public static XsrUiComputedStyle Empty { get; set; } = new();",
            "public double? Opacity { get; init; } = Callback();",
        })
            if (ThemeStyleSurfaceErrors("public sealed record XsrUiComputedStyle { " + member + " }").Count == 0)
                failures.Add("NTP/1 style guard failed to reject a behavior/callback/factory fixture: " + member);
        string alias = "using String = System.Action; public sealed record XsrUiComputedStyle { public String? FontFamily { get; init; } }";
        if (ThemeStyleSurfaceErrors(alias).Count == 0) failures.Add("NTP/1 style guard admits a callback hidden behind an alias.");
    }
}
