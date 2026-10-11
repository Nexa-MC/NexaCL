using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Nexa.Pxml.Generators;

[Generator(LanguageNames.CSharp)]
public sealed partial class PxmlControlCatalogGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor MissingCatalog = new(
        "PXMLGEN001",
        "PXML control catalog is empty",
        "No .pxml-control files were supplied through PxmlControlCatalogDirectory",
        "PXML",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidControl = new(
        "PXMLGEN002",
        "PXML control descriptor is invalid",
        "Control descriptor '{0}' is invalid: {1}",
        "PXML",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidCatalog = new(
        "PXMLGEN003",
        "PXML control catalog is inconsistent",
        "PXML control catalog is invalid: {0}",
        "PXML",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal)
    {
        "None", "Text", "Button", "Page", "List", "ListItem", "Image", "ProgressBar", "Dialog",
        "TitleBar", "Navigation", "NavigationItem", "Content", "TextInput", "Status",
    };

    private static readonly HashSet<string> ValueKinds = new(StringComparer.Ordinal)
    {
        "Double", "Thickness", "Boolean", "Orientation", "String", "SemanticId", "Alignment",
    };

    private static readonly HashSet<string> Recipes = new(StringComparer.Ordinal)
    {
        "Element", "StackLayout", "Text", "CommandInput", "Image", "VerticalPager", "HorizontalPager", "TextInput",
        "Progress",
    };

    private static readonly HashSet<string> Targets = new(StringComparer.Ordinal)
    {
        "Width", "Height", "MinWidth", "MaxWidth", "MinHeight", "MaxHeight", "Weight",
        "HorizontalAlignment", "VerticalAlignment", "Margin", "Padding", "Visibility", "Label",
        "Orientation", "Spacing", "StretchLastChild", "Scrollable", "Content", "Command",
        "Focusable", "Clickable", "ImageSource", "Key", "Placeholder", "IsPassword", "Enabled", "TransitionKey", "TransitionOffsetX", "TransitionOffsetY", "Value",
    };

    private static readonly HashSet<string> BindingProperties = new(StringComparer.Ordinal)
    {
        "None", "Text", "Visibility", "Enabled", "SemanticLabel", "TransitionKey", "Value",
    };

    private static readonly HashSet<string> DirtyKinds = new(StringComparer.Ordinal)
    {
        "None", "Structure", "Layout", "Paint", "State",
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ParseResult> controls = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".pxml-control", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancellationToken) => Parse(file, cancellationToken));

        context.RegisterSourceOutput(controls.Collect(), static (productionContext, results) =>
            Emit(productionContext, results));
    }
}
