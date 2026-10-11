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

    private static ParseResult Parse(AdditionalText file, System.Threading.CancellationToken cancellationToken)
    {
        string fileName = Path.GetFileName(file.Path);
        SourceText? source = file.GetText(cancellationToken);
        if (source is null)
        {
            return ParseResult.Failed(fileName, "the file could not be read");
        }

        string? schema = null;
        string? name = null;
        string? role = null;
        string? recipe = null;
        int? id = null;
        bool? allowsChildren = null;
        int propertyDeclarations = 0;
        List<PropertyDefinition> properties = new();
        List<string> errors = new();
        string[] lines = source.ToString().Replace("\r\n", "\n").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                errors.Add($"line {index + 1} must be key=value");
                continue;
            }

            string key = line.Substring(0, separator).Trim();
            string value = line.Substring(separator + 1).Trim();
            switch (key)
            {
                case "schema":
                    AssignOnce(ref schema, value, key, index, errors);
                    break;
                case "id":
                    if (id is not null)
                    {
                        errors.Add($"line {index + 1} declares id more than once");
                    }
                    else if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedId)
                        || parsedId <= 0)
                    {
                        errors.Add($"line {index + 1} needs a positive integer id");
                    }
                    else
                    {
                        id = parsedId;
                    }

                    break;
                case "name":
                    AssignOnce(ref name, value, key, index, errors);
                    break;
                case "role":
                    AssignOnce(ref role, value, key, index, errors);
                    break;
                case "recipe":
                    AssignOnce(ref recipe, value, key, index, errors);
                    break;
                case "children":
                    if (allowsChildren is not null)
                    {
                        errors.Add($"line {index + 1} declares children more than once");
                    }
                    else if (value == "true")
                    {
                        allowsChildren = true;
                    }
                    else if (value == "false")
                    {
                        allowsChildren = false;
                    }
                    else
                    {
                        errors.Add($"line {index + 1} children must be exactly true or false");
                    }

                    break;
                case "property":
                    propertyDeclarations++;
                    PropertyDefinition? property = ParseProperty(value, index + 1, errors);
                    if (property is not null)
                    {
                        properties.Add(property);
                    }

                    break;
                default:
                    errors.Add($"line {index + 1} uses unknown key '{key}'");
                    break;
            }
        }

        if (schema != "1")
        {
            errors.Add("schema must be exactly 1");
        }

        if (id is null)
        {
            errors.Add("id is required");
        }

        if (name is null || name.Length == 0 || !IsIdentifier(name))
        {
            errors.Add("name must be a non-empty C# identifier");
        }

        if (role is null || role.Length == 0 || !Roles.Contains(role))
        {
            errors.Add("role is missing or unsupported");
        }

        if (recipe is null || recipe.Length == 0 || !Recipes.Contains(recipe))
        {
            errors.Add("recipe is missing or unsupported");
        }

        if (allowsChildren is null)
        {
            errors.Add("children is required");
        }

        if (propertyDeclarations == 0)
        {
            errors.Add("at least one property is required");
        }

        foreach (IGrouping<string, PropertyDefinition> duplicate in properties.GroupBy(
                     property => property.Name,
                     StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            errors.Add($"property '{duplicate.Key}' is declared more than once");
        }

        foreach (IGrouping<string, PropertyDefinition> duplicate in properties.GroupBy(
                     property => property.Target,
                     StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            errors.Add($"IR target '{duplicate.Key}' is assigned more than once");
        }

        if (recipe is not null && Recipes.Contains(recipe))
        {
            foreach (PropertyDefinition property in properties)
            {
                if (!IsTargetSupportedByRecipe(recipe, property.Target))
                {
                    errors.Add($"IR target '{property.Target}' is not supported by recipe '{recipe}'");
                }

                if (property.Binding == "Text"
                    && (property.Target != "Content" || recipe is not ("Text" or "CommandInput")))
                {
                    errors.Add("the Text binding must target Content on the Text or CommandInput recipe");
                }

                if (property.Binding == "Visibility" && property.Target != "Visibility")
                {
                    errors.Add("the Visibility binding must target Visibility");
                }

                if (property.Binding == "SemanticLabel" && property.Target != "Label")
                {
                    errors.Add("the SemanticLabel binding must target Label");
                }
            }
        }

        return errors.Count == 0
            ? ParseResult.Succeeded(fileName, new ControlDefinition(
                id!.Value,
                name!,
                role!,
                recipe!,
                allowsChildren!.Value,
                properties))
            : ParseResult.Failed(fileName, string.Join("; ", errors));
    }

    private static PropertyDefinition? ParseProperty(string value, int lineNumber, List<string> errors)
    {
        string[] parts = value.Split('|');
        if (parts.Length != 7)
        {
            errors.Add($"line {lineNumber} property needs seven pipe-separated fields");
            return null;
        }

        for (int index = 0; index < parts.Length; index++)
        {
            parts[index] = parts[index].Trim();
        }

        string name = parts[0];
        string kind = parts[1];
        string target = parts[2];
        string binding = parts[3];
        string dirty = parts[4];
        string requiredRaw = parts[5];
        string? defaultValue = parts[6].Length == 0 ? null : parts[6];

        bool valid = true;
        if (!IsIdentifier(name))
        {
            errors.Add($"line {lineNumber} has invalid property name '{name}'");
            valid = false;
        }

        valid &= ValidateToken(ValueKinds, kind, "value kind", lineNumber, errors);
        valid &= ValidateToken(Targets, target, "IR target", lineNumber, errors);
        valid &= ValidateToken(BindingProperties, binding, "binding property", lineNumber, errors);

        if (ValueKinds.Contains(kind) && Targets.Contains(target) && !IsCompatibleTarget(kind, target))
        {
            errors.Add($"line {lineNumber} value kind '{kind}' is incompatible with IR target '{target}'");
            valid = false;
        }

        string[] dirtyParts = dirty.Split(',');
        if (dirtyParts.Length == 0 || dirtyParts.Any(part => !DirtyKinds.Contains(part)))
        {
            errors.Add($"line {lineNumber} has unsupported dirty kinds '{dirty}'");
            valid = false;
        }

        if (binding == "None" && dirty != "None" || binding != "None" && dirty == "None")
        {
            errors.Add($"line {lineNumber} must pair bindings with non-None dirty kinds");
            valid = false;
        }

        if ((binding is "Text" or "SemanticLabel" or "TransitionKey") && kind != "String"
            || binding is "Visibility" or "Enabled" && kind != "Boolean"
            || binding is "Value" && kind != "Double")
        {
            errors.Add($"line {lineNumber} binding '{binding}' is incompatible with '{kind}'");
            valid = false;
        }

        if (binding == "Text" && dirty != "Layout,Paint"
            || binding is "Visibility" or "Enabled" or "Value" && dirty != "State"
            || binding is "SemanticLabel" or "TransitionKey" && dirty != "Paint")
        {
            errors.Add($"line {lineNumber} binding '{binding}' uses an incompatible dirty-kind set '{dirty}'");
            valid = false;
        }

        bool required;
        if (requiredRaw == "true")
        {
            required = true;
        }
        else if (requiredRaw == "false")
        {
            required = false;
        }
        else
        {
            errors.Add($"line {lineNumber} required must be exactly true or false");
            required = false;
            valid = false;
        }

        if (required && defaultValue is not null)
        {
            errors.Add($"line {lineNumber} cannot be required and have a default");
            valid = false;
        }

        if (defaultValue is not null && !IsValidLiteral(kind, defaultValue))
        {
            errors.Add($"line {lineNumber} has invalid default '{defaultValue}' for '{kind}'");
            valid = false;
        }

        return valid
            ? new PropertyDefinition(name, kind, target, binding, dirtyParts, required, defaultValue)
            : null;
    }
}
