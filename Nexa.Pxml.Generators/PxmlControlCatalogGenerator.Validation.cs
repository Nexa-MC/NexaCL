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

public sealed partial class PxmlControlCatalogGenerator
{

    private static void AssignOnce(
        ref string? target,
        string value,
        string key,
        int zeroBasedLine,
        List<string> errors)
    {
        if (target is not null)
        {
            errors.Add($"line {zeroBasedLine + 1} declares {key} more than once");
        }
        else
        {
            target = value;
        }
    }

    private static bool ValidateToken(
        HashSet<string> values,
        string value,
        string label,
        int lineNumber,
        List<string> errors)
    {
        if (values.Contains(value))
        {
            return true;
        }

        errors.Add($"line {lineNumber} has unsupported {label} '{value}'");
        return false;
    }

    private static bool IsIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value)
            || SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None
            || !(value[0] == '_' || char.IsLetter(value[0])))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            if (value[index] != '_' && !char.IsLetterOrDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidLiteral(string kind, string value)
    {
        switch (kind)
        {
            case "Boolean":
                return value == "true" || value == "false";
            case "Double":
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                    && !double.IsNaN(number)
                    && !double.IsInfinity(number);
            case "Thickness":
                string[] parts = value.Split(',');
                return (parts.Length == 1 || parts.Length == 4)
                    && parts.All(part => double.TryParse(
                            part,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out double thickness)
                        && !double.IsNaN(thickness)
                        && !double.IsInfinity(thickness));
            case "Orientation":
                return value == "Vertical" || value == "Horizontal";
            case "Alignment":
                return value == "Stretch" || value == "Start" || value == "Center" || value == "End";
            case "SemanticId":
                return value.Length > 0 && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));
            case "String":
                return true;
            default:
                return false;
        }
    }

    private static bool IsCompatibleTarget(string kind, string target)
    {
        switch (kind)
        {
            case "Double":
                return target == "Width" || target == "Height"
                    || target == "MinWidth" || target == "MaxWidth"
                    || target == "MinHeight" || target == "MaxHeight"
                    || target == "Weight" || target == "Spacing" || target == "TransitionOffsetX" || target == "TransitionOffsetY"
                    || target == "Value";
            case "Thickness":
                return target == "Margin" || target == "Padding";
            case "Boolean":
                return target == "Visibility" || target == "Scrollable"
                    || target == "Focusable" || target == "Clickable" || target == "StretchLastChild" || target == "IsPassword" || target == "Enabled";
            case "Orientation":
                return target == "Orientation";
            case "Alignment":
                return target == "HorizontalAlignment" || target == "VerticalAlignment";
            case "String":
                return target == "Label" || target == "Content" || target == "ImageSource"
                    || target == "Key" || target == "Placeholder" || target == "TransitionKey";
            case "SemanticId":
                return target == "Command";
            default:
                return false;
        }
    }

    private static bool IsTargetSupportedByRecipe(string recipe, string target)
    {
        if (target == "Width" || target == "Height"
            || target == "MinWidth" || target == "MaxWidth"
            || target == "MinHeight" || target == "MaxHeight" || target == "Weight"
            || target == "HorizontalAlignment" || target == "VerticalAlignment"
            || target == "Margin" || target == "Padding" || target == "Visibility"
            || target == "Label" || target == "Key" || target is "TransitionKey" or "TransitionOffsetX" or "TransitionOffsetY")
        {
            return true;
        }

        switch (recipe)
        {
            case "Element":
                return false;
            case "StackLayout":
                return target == "Orientation" || target == "Spacing" || target == "Scrollable"
                    || target == "StretchLastChild" || target == "TransitionKey" || target == "TransitionOffsetX";
            case "Text":
                return target == "Content";
            case "CommandInput":
                return target == "Command" || target == "Focusable" || target == "Clickable"
                    || target == "Content" || target == "ImageSource";
            case "Image":
                return target == "ImageSource";
            case "TextInput":
                return target == "Placeholder" || target == "IsPassword" || target == "Enabled";
            case "Progress":
                return target == "Value";
            default:
                return false;
        }
    }
}
