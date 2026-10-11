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

    private sealed class ParseResult
    {
        private ParseResult(string fileName, ControlDefinition? control, string? error)
        {
            FileName = fileName;
            Control = control;
            Error = error;
        }

        public string FileName { get; }
        public ControlDefinition? Control { get; }
        public string? Error { get; }

        public static ParseResult Succeeded(string fileName, ControlDefinition control) =>
            new(fileName, control, null);

        public static ParseResult Failed(string fileName, string error) =>
            new(fileName, null, error);
    }

    private sealed class ControlDefinition
    {
        public ControlDefinition(
            int id,
            string name,
            string role,
            string recipe,
            bool allowsChildren,
            IReadOnlyList<PropertyDefinition> properties)
        {
            Id = id;
            Name = name;
            Role = role;
            Recipe = recipe;
            AllowsChildren = allowsChildren;
            Properties = properties;
        }

        public int Id { get; }
        public string Name { get; }
        public string Role { get; }
        public string Recipe { get; }
        public bool AllowsChildren { get; }
        public IReadOnlyList<PropertyDefinition> Properties { get; }
    }

    private sealed class PropertyDefinition
    {
        public PropertyDefinition(
            string name,
            string kind,
            string target,
            string binding,
            IReadOnlyList<string> dirtyKinds,
            bool required,
            string? defaultValue)
        {
            Name = name;
            Kind = kind;
            Target = target;
            Binding = binding;
            DirtyKinds = dirtyKinds;
            Required = required;
            DefaultValue = defaultValue;
        }

        public string Name { get; }
        public string Kind { get; }
        public string Target { get; }
        public string Binding { get; }
        public IReadOnlyList<string> DirtyKinds { get; }
        public bool Required { get; }
        public string? DefaultValue { get; }
    }
}
