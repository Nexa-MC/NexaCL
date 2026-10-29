using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nexa.Xsr.ArchitectureTests;

internal static partial class Program
{
    private static bool IsServiceImplementation(string name) => name == "Nexa.Services"
        || (name.StartsWith("Nexa.Services.", StringComparison.Ordinal)
            && !name.EndsWith(".Contracts", StringComparison.Ordinal) && name != "Nexa.Services.Composition");

    private static bool RequiresSemanticValidation(string name) =>
        !name.EndsWith(".Tests", StringComparison.Ordinal)
        && (name.StartsWith("Nexa.Services", StringComparison.Ordinal)
            || name.StartsWith("Nexa.Xsr.Renderer", StringComparison.Ordinal)
            || name is "Nexa.Desktop" or "Nexa.Update.Helper");

    private static void ValidateSemanticBoundaries(string root, Dictionary<string, string> projects, List<string> failures)
    {
        // Resolve the compiler's assembly owners, including aliases and type forwards.
        // No source-text spelling can bypass a forbidden API or a contract boundary.
        var libraries = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(projects.Values.SelectMany(p => Directory.Exists(Path.Combine(Path.GetDirectoryName(p)!, "bin")) ? Directory.EnumerateFiles(Path.Combine(Path.GetDirectoryName(p)!, "bin"), "*.dll", SearchOption.AllDirectories) : []))
            .GroupBy(Path.GetFileName, StringComparer.Ordinal).Select(g => g.First()).ToArray();
        // This pass resolves ownership from compiled dependencies. Missing metadata makes
        // logging/term adapters unresolved and turns their keys into false copy violations.
        // Fail the gate explicitly instead of interpreting an incomplete semantic model.
        var available = libraries.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        var missing = projects.Keys.Where(RequiresSemanticValidation)
            .SelectMany(name => AllowedReferences.GetValueOrDefault(name) ?? [])
            .Where(name => !available.Contains(name)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            failures.Add("Semantic architecture checks require compiled project dependencies. "
                + "Build NexaCL.slnx in Release before running this check; missing assemblies: " + string.Join(", ", missing) + ".");
            return;
        }
        foreach ((string projectName, string projectPath) in projects)
        {
            bool renderer = projectName.StartsWith("Nexa.Xsr.Renderer", StringComparison.Ordinal);
            if (!RequiresSemanticValidation(projectName)) continue;
            string directory = Path.GetDirectoryName(projectPath)!;
            var trees = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Where(p => !IsBuildOutput(p))
                .Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), path: p)).ToList();
            trees.Add(CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;"));
            var references = libraries.Where(p => Path.GetFileNameWithoutExtension(p) != projectName)
                .Select(p => MetadataReference.CreateFromFile(p));
            var compilation = CSharpCompilation.Create(projectName, trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            foreach (var tree in trees.Where(t => t.FilePath.Length > 0))
            {
                var model = compilation.GetSemanticModel(tree);
                bool presentation = projectName == "Nexa.Desktop" && tree.FilePath.Contains(Path.DirectorySeparatorChar + "Ui" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
                if (projectName == "Nexa.Desktop")
                    ValidateProductPresentation(model, tree.GetRoot(), root, tree.FilePath, failures);
                foreach (var creation in tree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(creation).Symbol is IMethodSymbol constructor
                        && constructor.ContainingType.ToDisplayString() == "Nexa.Services.Updates.UpdateHighWaterStore")
                        failures.Add($"Production must not construct path-based update high-water state at {Path.GetRelativePath(root, tree.FilePath)}:{creation.GetLocation().GetLineSpan().StartLinePosition.Line + 1}.");
                }
                foreach (var node in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    ISymbol? symbol = model.GetSymbolInfo(node).Symbol;
                    INamedTypeSymbol? type = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
                    string? owner = type?.ContainingAssembly.Name;
                    string location = $"{Path.GetRelativePath(root, tree.FilePath)}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";
                    if (renderer && type is not null && (owner?.StartsWith("Nexa.Sidecar.", StringComparison.Ordinal) == true
                        || (owner == "Nexa.Xsr.Runtime" && (type.Name.StartsWith("Sidecar", StringComparison.Ordinal)
                            || type.Name.StartsWith("XsrSignal", StringComparison.Ordinal)
                            || type.Name.StartsWith("XsrUiPatchRuntime", StringComparison.Ordinal)
                            || type.Name.StartsWith("XsrUiPatchAdmission", StringComparison.Ordinal)
                            || type.Name.StartsWith("XsrUiModuleRuntime", StringComparison.Ordinal)
                            || type.Name.StartsWith("XsrUiModuleAdmission", StringComparison.Ordinal)
                            || type.Name.StartsWith("XsrFunctionPatch", StringComparison.Ordinal)))))
                        failures.Add($"Renderer must emit host intent rather than access Sidecar execution: {symbol} at {location}.");
                    if (presentation && owner is not null && IsServiceImplementation(owner))
                        failures.Add($"UI must consume contracts: {symbol} belongs to {owner} at {location}.");
                    if (projectName.StartsWith("Nexa.Services", StringComparison.Ordinal)
                        && owner is not null && (owner.StartsWith("Nexa.Xsr.Renderer", StringComparison.Ordinal) || owner == "Nexa.Desktop" || owner.StartsWith("Avalonia", StringComparison.Ordinal)))
                        failures.Add($"Service references renderer {owner} at {location}.");
                    if (symbol is IMethodSymbol method)
                    {
                        string fullType = method.ContainingType.ToDisplayString();
                        if (fullType == "Nexa.Services.Updates.UpdateRestartScheduler"
                            && method.Name is "ScheduleInstallAndRestart" or "ScheduleInstallOnExit" or "CreateReplacementProcess")
                            failures.Add($"Production must not execute a caller-controlled staged update helper: {method} at {location}.");
                        bool blocking = (method.Name == "Wait" && fullType.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal))
                            || (method.Name == "GetResult" && fullType.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal) && fullType.Contains("Awaiter", StringComparison.Ordinal))
                            || (method.Name == "WaitForExit" && fullType == "System.Diagnostics.Process");
                        bool nativeStart = fullType == "System.Diagnostics.Process" && method.Name is "Start" or "GetProcessById";
                        if ((presentation || projectName.StartsWith("Nexa.Services", StringComparison.Ordinal)) && (blocking || nativeStart))
                            failures.Add($"Forbidden {(blocking ? "blocking" : "platform")} API {method} at {location}.");
                    }
                }
            }
        }
    }

    private static readonly HashSet<string> ProductPresentationComponents = new(StringComparer.Ordinal)
    {
        "XsrUiElement", "XsrUiStackPanel", "XsrUiText", "XsrUiSemantic", "XsrUiInput", "XsrUiScroll",
        "XsrUiScrollGesture", "XsrUiTransition", "XsrUiSelection", "XsrUiSelectionGroup", "XsrUiToggle",
        "XsrUiVisualStyle", "XsrUiCommandBinding", "XsrUiLiveRegion", "XsrUiImage", "XsrUiTextInput",
        "XsrUiProgress", "XsrUiSegmentReveal", "XsrUiStableContent", "XsrUiPager", "XsrUiSegmentedTrack",
        "XsrUiOverlayLayer", "XsrUiOverlayMotion", "XsrUiAnchoredOverlay", "XsrUiDismissBinding",
    };

    private static bool IsRendererType(ITypeSymbol? type, string name) => type?.Name == name
        && type.ContainingNamespace.ToDisplayString() == "Nexa.Xsr.Renderer";

    private static bool IsProductComponent(ITypeSymbol? type) => type is not null
        && type.ContainingNamespace.ToDisplayString() == "Nexa.Xsr.Renderer" && ProductPresentationComponents.Contains(type.Name);

    private static bool IsPresentationIr(ITypeSymbol? type) => type is not null
        && type.ContainingNamespace.ToDisplayString() == "Nexa.Xsr.Renderer.Nxml"
        && type.Name is "NxmlIrNode" or "NxmlHostIr" or "NxmlDocument" or "NxmlElement" or "NxmlNode" or "NxmlProperty" or "NxmlValue";

    private static string ProductMethod(SyntaxNode node) => node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>()
        .FirstOrDefault()?.Identifier.ValueText ?? ".ctor";
    private static string? ProductOwner(SemanticModel model, SyntaxNode node) => model.GetEnclosingSymbol(node.SpanStart)?.ContainingType?.ToDisplayString();

    private static bool AllowsProductLifetimeComponent(SemanticModel model, SyntaxNode node, ITypeSymbol? type, string file)
    {
        var creation = node as BaseObjectCreationExpressionSyntax
            ?? (node as InvocationExpressionSyntax)?.ArgumentList.Arguments.LastOrDefault()?.Expression as BaseObjectCreationExpressionSyntax;
        if (creation?.ArgumentList is not { Arguments.Count: 1 } arguments) return false;
        var argument = arguments.Arguments[0].Expression;
        if (file == "DesktopDynamicLayer.cs" && ProductOwner(model, node) == "Nexa.Desktop.Ui.DesktopDynamicLayer"
            && ProductMethod(node) == "Append" && IsRendererType(type, "XsrUiOverlayLayer"))
            return model.GetConstantValue(argument) is { HasValue: true, Value: false };
        return file == "DesktopBubbleLayout.cs" && ProductOwner(model, node) == "Nexa.Desktop.Ui.DesktopBubbleLayout"
            && ProductMethod(node) == "Register" && IsRendererType(type, "XsrUiOverlayMotion")
            && model.GetSymbolInfo(argument).Symbol is IFieldSymbol { Name: "Notification", ContainingType.Name: "XsrUiOverlayMotionKind" };
    }

    private static bool AllowsProductProperty(SemanticModel model, SyntaxNode node, ITypeSymbol type, string property, string file)
    {
        string method = ProductMethod(node);
        if (IsRendererType(type, "XsrUiScroll") && property is "OffsetX" or "OffsetY") return true;
        if (IsRendererType(type, "XsrUiSegmentedTrack") && property == "Selected") return true;
        if (IsRendererType(type, "XsrUiTransition") && property is "Key" or "BoundKey" or "Source" or "OffsetX" or "OffsetY") return true;
        if (IsRendererType(type, "XsrUiInput") && property is "IsHovered" or "IsPressed" or "IsFocused" or "IsFocusVisible") return true;
        if (IsRendererType(type, "XsrUiImage") && property == "Raster") return true;
        if (IsRendererType(type, "XsrUiOverlayMotion") && property == "IsClosing") return true;
        if (file == "DesktopBubbleLayout.cs" && ProductOwner(model, node) == "Nexa.Desktop.Ui.DesktopBubbleLayout")
        {
            if (method == "SetVisible" && (IsRendererType(type, "XsrUiElement") && property == "IsVisible"
                || IsRendererType(type, "XsrUiInput") && property == "Enabled")) return true;
            if (method == "Arrange" && IsRendererType(type, "XsrUiElement") && property is "IsVisible" or "Margin") return true;
        }
        if (file == "DesktopBackgroundAppearance.cs" && ProductOwner(model, node) == "Nexa.Desktop.Ui.DesktopBackgroundPresentation" && method == "Present"
            && IsRendererType(type, "XsrUiVisualStyle") && property == "Background") return true;
        if (file == "SettingsPageController.ContentDetails.cs" && ProductOwner(model, node) == "Nexa.Desktop.Ui.SettingsPageController" && method == "ApplyContentFormatting"
            && IsRendererType(type, "XsrUiText") && property is "Content" or "Localize" or "Runs") return true;
        if (file == "NxmlShellComposer.cs" && ProductOwner(model, node) == "Nexa.Desktop.Ui.NxmlShellComposer" && method == "ComposeCore"
            && IsRendererType(type, "XsrUiText") && property == "Content"
            && node is AssignmentExpressionSyntax assignment && assignment.Right is MemberAccessExpressionSyntax member
            && model.GetSymbolInfo(member).Symbol is IPropertySymbol { Name: "Title", ContainingType.Name: "XsrUiShellOptions" }) return true;
        return false;
    }

    private static void ValidateProductPresentation(SemanticModel model, SyntaxNode document, string repositoryRoot, string path, List<string> failures)
    {
        string file = Path.GetFileName(path);
        void Report(SyntaxNode node, string rule) => failures.Add($"Product NXML closure: {rule} at {Path.GetRelativePath(repositoryRoot, path)}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}.");
        foreach (var creation in document.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            var type = model.GetTypeInfo(creation).Type;
            if (IsProductComponent(type) && !AllowsProductLifetimeComponent(model, creation, type, file) || IsPresentationIr(type))
                Report(creation, $"{type!.Name} construction belongs to external templates");
            if (type?.ContainingNamespace.ToDisplayString() == "Nexa.Xsr.Renderer.Nxml" && type.Name == "NxmlDynamicTemplate"
                && !(file == "DesktopMarkup.cs" && ProductOwner(model, creation) == "Nexa.Desktop.Ui.DesktopMarkup" && ProductMethod(creation) == "Dynamic"))
                Report(creation, "dynamic markup must be selected through the external asset adapter");
        }
        foreach (var projection in document.DescendantNodes().OfType<WithExpressionSyntax>())
            if (IsPresentationIr(model.GetTypeInfo(projection.Expression).Type)) Report(projection, "IR projections belong to external templates");
        foreach (var invocation in document.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method) continue;
            if (IsRendererType(method.ContainingType, "XsrUiTree") && method.Name is "Create" or "Attach" or "Detach" or "ReorderChildren")
                Report(invocation, "tree construction and ownership changes must use the NXML owner");
            if (IsRendererType(method.ContainingType, "XsrUiTree") && method.Name == "SetComponent"
                && method.TypeArguments.Any(type => IsProductComponent(type) && !AllowsProductLifetimeComponent(model, invocation, type, file)))
                Report(invocation, "presentation components must reload through their NXML owner");
            if (IsRendererType(method.ContainingType, "XsrUiTree") && method.Name == "SetComponent"
                && method.TypeArguments.Any(type => type.TypeKind is TypeKind.TypeParameter or TypeKind.Dynamic || type.SpecialType == SpecialType.System_Object))
                Report(invocation, "generic component factories cannot hide product presentation");
            if (IsRendererType(method.ContainingType, "XsrUiProgress") && method.Name == "SetTarget")
                Report(invocation, "progress targets must reload through their NXML owner");
            if (method.ContainingType.ToDisplayString() == "Nexa.Xsr.Renderer.Nxml.NxmlUiLoader" && method.Name == "Load")
                Report(invocation, "product roots must use LoadDetached and owned fragments");
            if (method.ContainingType.ToDisplayString() == "Nexa.Xsr.Renderer.Nxml.NxmlParser" && method.Name == "Parse"
                && !(file == "DesktopMarkup.cs" && ProductOwner(model, invocation) == "Nexa.Desktop.Ui.DesktopMarkup" && ProductMethod(invocation) == "Load"))
                Report(invocation, "markup parsing belongs to the external asset adapter");
            if (method.ContainingType.ToDisplayString() == "Nexa.Xsr.Renderer.Nxml.NssStyleSheet" && method.Name == "Parse"
                && !(file == "DesktopMarkup.cs" && ProductOwner(model, invocation) == "Nexa.Desktop.Ui.DesktopMarkup" && ProductMethod(invocation) == "LoadStyles"))
                Report(invocation, "styles must be read from the external NSS bundle");
            if (method.ContainingType.ToDisplayString() == "Nexa.Xsr.Renderer.Nxml.NxmlCompiler" && method.Name == "Compile"
                && !(file == "DesktopMarkup.cs" && ProductOwner(model, invocation) == "Nexa.Desktop.Ui.DesktopMarkup" && ProductMethod(invocation) == "Compile"))
                Report(invocation, "product compilation belongs to the external asset adapter");
            if (method.ContainingType.ToDisplayString() == "Nexa.Xsr.Renderer.Nxml.NxmlDynamicTemplate"
                && method.Name is "Compile" or "CompileLocalized"
                && !(file == "DesktopMarkup.cs" && ProductOwner(model, invocation) == "Nexa.Desktop.Ui.DesktopMarkup" && ProductMethod(invocation) == "Dynamic"))
                Report(invocation, "dynamic compilation belongs to the external asset adapter");
            if (IsPresentationIr(method.ContainingType)) Report(invocation, "IR property factories belong to external templates");
        }
        foreach (var assignment in document.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            foreach (var target in ProductWriteTargets(assignment.Left)) CheckWrite(target, assignment);
        }
        foreach (var increment in document.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>()
            .Where(node => node.IsKind(SyntaxKind.PreIncrementExpression) || node.IsKind(SyntaxKind.PreDecrementExpression))) CheckWrite(increment.Operand, increment);
        foreach (var increment in document.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>()
            .Where(node => node.IsKind(SyntaxKind.PostIncrementExpression) || node.IsKind(SyntaxKind.PostDecrementExpression))) CheckWrite(increment.Operand, increment);
        void CheckWrite(ExpressionSyntax target, SyntaxNode operation)
        {
            if (model.GetSymbolInfo(target).Symbol is IPropertySymbol property && IsProductComponent(property.ContainingType)
                && !AllowsProductProperty(model, operation, property.ContainingType, property.Name, file))
                Report(operation, $"{property.ContainingType.Name}.{property.Name} must reload through its NXML owner");
        }
        foreach (var literal in document.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(node => node.IsKind(SyntaxKind.StringLiteralExpression)))
        {
            string value = literal.Token.ValueText;
            if (ContainsProductMarkup(value)) Report(literal, "literal or assembled product markup belongs to external NXML");
            if ((value.Any(character => character is >= '\u3400' and <= '\u9fff')
                || value.Any(char.IsLetter) && IsProductCopySink(model, literal)) && IsFixedProductCopy(model, literal, file))
                Report(literal, "fixed product wording must be declared in external terms or templates");
        }
        foreach (var interpolation in document.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>())
        {
            if (interpolation.Contents.OfType<InterpolatedStringTextSyntax>().Any(text => ContainsProductMarkup(text.TextToken.ValueText)))
                Report(interpolation, "interpolated product markup belongs to external NXML");
            if (interpolation.Contents.OfType<InterpolatedStringTextSyntax>().Any(text => text.TextToken.ValueText.Any(character => character is >= '\u3400' and <= '\u9fff')
                || text.TextToken.ValueText.Any(char.IsLetter) && IsProductCopySink(model, interpolation))
                && IsFixedProductCopy(model, interpolation, file)) Report(interpolation, "fixed product formats belong in external named text templates");
        }
    }

    private static IEnumerable<ExpressionSyntax> ProductWriteTargets(ExpressionSyntax target)
    {
        if (target is ParenthesizedExpressionSyntax parentheses)
        { foreach (var inner in ProductWriteTargets(parentheses.Expression)) yield return inner; }
        else if (target is TupleExpressionSyntax tuple)
        { foreach (var argument in tuple.Arguments) foreach (var inner in ProductWriteTargets(argument.Expression)) yield return inner; }
        else yield return target;
    }

    private static bool ContainsProductMarkup(string text) => System.Text.RegularExpressions.Regex.IsMatch(text,
        @"<\s*(?:nxml-(?:page|dynamic|component)|Node|Page|StackPanel|ContentHost|Dynamic|Text|TextInput|Button|Image)\b");

    // Only the argument that supplies visible text is a copy sink. Keys in nested external
    // selectors and tuple parameter names are metadata, even below a feedback/localizer call.
    private static bool IsProductCopySink(SemanticModel model, SyntaxNode node)
    {
        if (IsExternalProductTextArgument(model, node)) return false;
        foreach (var argument in node.Ancestors().OfType<ArgumentSyntax>())
        {
            if (argument.Parent?.Parent is InvocationExpressionSyntax invocation
                && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
            {
                var parameter = ProductArgumentParameter(method, invocation.ArgumentList, argument);
                if (method.ContainingType.ToDisplayString() == "Nexa.Desktop.Ui.DesktopFeedbackService")
                    return parameter?.Name is "message" or "title" or "acceptLabel" or "cancelLabel" or "alternateLabel" or "actionLabel";
                if (IsRendererType(method.ContainingType, "XsrUiRenderer") && method.Name == "LocalizeText") return parameter?.Ordinal == 0;
            }
            if (argument.Parent?.Parent is BaseObjectCreationExpressionSyntax creation
                && model.GetSymbolInfo(creation).Symbol is IMethodSymbol constructor
                && constructor.ContainingType.ContainingNamespace.ToDisplayString() == "Nexa.Xsr.Renderer"
                && constructor.ContainingType.Name is "XsrUiContextMenuItem" or "XsrUiShellNavigationItem")
                return ProductArgumentParameter(constructor, creation.ArgumentList!, argument)?.Name is "Label" or "label";
            // A callback's body does not contribute string data to the invocation accepting it.
            if (argument.Expression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax) return false;
        }
        return false;
    }

    private static IParameterSymbol? ProductArgumentParameter(IMethodSymbol method, ArgumentListSyntax arguments, ArgumentSyntax argument)
    {
        if (argument.NameColon is { } named) return method.Parameters.FirstOrDefault(parameter => parameter.Name == named.Name.Identifier.ValueText);
        int ordinal = arguments.Arguments.IndexOf(argument);
        return ordinal >= 0 && method.Parameters.Length > 0 ? method.Parameters[Math.Min(ordinal, method.Parameters.Length - 1)] : null;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, Dictionary<ISymbol, bool>> ProductAssetEvidence = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, Dictionary<ISymbol, bool>> ProductSelectorEvidence = new();

    private static bool IsExternalProductTextArgument(SemanticModel model, SyntaxNode node)
    {
        foreach (var argument in node.Ancestors().OfType<ArgumentSyntax>())
        {
            if (argument.Parent?.Parent is not InvocationExpressionSyntax invocation
                || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method) continue;
            var parameter = ProductArgumentParameter(method, invocation.ArgumentList, argument);
            if (parameter?.Ordinal == 0 && IsProductAssetSelector(model.Compilation, method)) return true;
            if (!IsProductAssetSelector(model.Compilation, method)) continue;
            // Named text formats accept literal facts, but their tuple's first item is a
            // placeholder identity. Exempt only that item, never the value beside it.
            foreach (var tuple in node.Ancestors().OfType<TupleExpressionSyntax>())
                if (argument.Expression.Span.Contains(tuple.Span) && tuple.Arguments.Count == 2
                    && tuple.Arguments[0].Expression.Span.Contains(node.Span)) return true;
            foreach (var key in node.Ancestors().OfType<ImplicitElementAccessSyntax>())
                if (argument.Expression.Span.Contains(key.Span) && key.Parent is AssignmentExpressionSyntax assignment
                    && assignment.Left == key && assignment.Parent is InitializerExpressionSyntax initializer
                    && initializer.Parent is BaseObjectCreationExpressionSyntax dictionary
                    && model.GetTypeInfo(dictionary).Type is INamedTypeSymbol { Name: "Dictionary", TypeArguments.Length: 2 } type
                    && type.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"
                    && type.TypeArguments[0].SpecialType == SpecialType.System_String) return true;
        }
        return false;
    }

    private static bool IsProductAssetSelector(Compilation compilation, IMethodSymbol method)
    {
        var cache = ProductSelectorEvidence.GetValue(compilation, static _ => new(SymbolEqualityComparer.Default));
        return Selector(method, new HashSet<ISymbol>(SymbolEqualityComparer.Default));
        bool Selector(IMethodSymbol candidate, HashSet<ISymbol> visiting)
        {
            candidate = candidate.OriginalDefinition;
            if (cache.TryGetValue(candidate, out bool recorded)) return recorded;
            if (!visiting.Add(candidate)) return false;
            if (IsProductAssetAdapter(candidate)) { visiting.Remove(candidate); cache[candidate] = true; return true; }
            bool found = candidate.ReturnType.SpecialType == SpecialType.System_String && candidate.Parameters.Length > 0
                && candidate.Parameters[0].Type.SpecialType == SpecialType.System_String
                && HasProductAssetEvidence(compilation, candidate);
            if (found)
            {
                int uses = 0;
                foreach (var reference in candidate.DeclaringSyntaxReferences)
                {
                    var declaration = reference.GetSyntax();
                    var semantic = compilation.GetSemanticModel(declaration.SyntaxTree);
                    foreach (var identifier in declaration.DescendantNodes().OfType<IdentifierNameSyntax>()
                        .Where(identifier => SymbolEqualityComparer.Default.Equals(semantic.GetSymbolInfo(identifier).Symbol, candidate.Parameters[0])))
                    {
                        uses++;
                        bool identity = false;
                        foreach (var argument in identifier.Ancestors().OfType<ArgumentSyntax>())
                        {
                            if (argument.Parent?.Parent is InvocationExpressionSyntax invocation
                                && semantic.GetSymbolInfo(invocation).Symbol is IMethodSymbol called)
                            {
                                if (ProductArgumentParameter(called, invocation.ArgumentList, argument)?.Ordinal == 0 && Selector(called, visiting))
                                { identity = true; break; }
                                if (called.Name == "GetValueOrDefault" && invocation.Expression is MemberAccessExpressionSyntax lookup
                                    && HasProductExpressionAssetEvidence(compilation, semantic, lookup.Expression))
                                { identity = true; break; }
                            }
                            if (argument.Parent?.Parent is ElementAccessExpressionSyntax index
                                && HasProductExpressionAssetEvidence(compilation, semantic, index.Expression))
                            { identity = true; break; }
                        }
                        if (!identity) { found = false; break; }
                    }
                    if (!found) break;
                }
                found &= uses > 0;
            }
            visiting.Remove(candidate); cache[candidate] = found; return found;
        }
    }

    private static bool IsProductAssetAdapter(IMethodSymbol method) => method.ContainingType.ToDisplayString() == "Nexa.Desktop.Ui.DesktopMarkup"
        && method.Name is "Load" or "Read" or "Open" or "Dynamic";

    private static bool HasProductExpressionAssetEvidence(Compilation compilation, SemanticModel semantic, ExpressionSyntax expression)
        => expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(identifier =>
            semantic.GetSymbolInfo(identifier).Symbol is ISymbol symbol && symbol is IFieldSymbol or ILocalSymbol
                && HasProductAssetEvidence(compilation, symbol));

    private static bool HasProductAssetEvidence(Compilation compilation, ISymbol symbol)
    {
        var cache = ProductAssetEvidence.GetValue(compilation, static _ => new(SymbolEqualityComparer.Default));
        return Evidence(symbol, new HashSet<ISymbol>(SymbolEqualityComparer.Default), 0);
        bool Evidence(ISymbol candidate, HashSet<ISymbol> visiting, int depth)
        {
            candidate = candidate.OriginalDefinition;
            if (cache.TryGetValue(candidate, out bool recorded)) return recorded;
            if (depth >= 16 || !visiting.Add(candidate)) return false;
            if (candidate is IMethodSymbol adapter && IsProductAssetAdapter(adapter))
            { visiting.Remove(candidate); cache[candidate] = true; return true; }
            bool found = false;
            foreach (var reference in candidate.DeclaringSyntaxReferences)
            {
                var declaration = reference.GetSyntax();
                var semantic = compilation.GetSemanticModel(declaration.SyntaxTree);
                IEnumerable<ExpressionSyntax> returned = declaration switch
                {
                    MethodDeclarationSyntax method => method.ExpressionBody is { } expression ? [expression.Expression]
                        : method.Body?.DescendantNodes().OfType<ReturnStatementSyntax>().Where(statement => statement.Expression is not null
                            && statement.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() == method).Select(statement => statement.Expression!) ?? [],
                    LocalFunctionStatementSyntax function => function.ExpressionBody is { } expression ? [expression.Expression]
                        : function.Body?.DescendantNodes().OfType<ReturnStatementSyntax>().Where(statement => statement.Expression is not null).Select(statement => statement.Expression!) ?? [],
                    VariableDeclaratorSyntax initializedVariable when initializedVariable.Initializer is { } initializer => [initializer.Value],
                    ForEachStatementSyntax loop => [loop.Expression],
                    _ => []
                };
                var expressions = returned.ToArray();
                found = expressions.Length > 0 && expressions.All(expression => ExpressionEvidence(expression, semantic, visiting, depth + 1));
                if (!found && candidate is ILocalSymbol && declaration is VariableDeclaratorSyntax variable
                    && variable.Initializer?.Value is BaseObjectCreationExpressionSyntax creation
                    && semantic.GetTypeInfo(creation).Type is INamedTypeSymbol { Name: "Dictionary", ContainingNamespace: { } space }
                    && space.ToDisplayString() == "System.Collections.Generic")
                {
                    // A term table may merge several assets. Require every inserted value
                    // to come from an external node; an unused Load call proves nothing.
                    var scope = declaration.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
                    var insertions = scope?.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(invocation =>
                        invocation.Expression is MemberAccessExpressionSyntax member
                        && SymbolEqualityComparer.Default.Equals(semantic.GetSymbolInfo(member.Expression).Symbol, candidate)
                        && member.Name.Identifier.ValueText is "Add" or "TryAdd").ToArray() ?? [];
                    var replacements = scope?.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(assignment =>
                        assignment.Left is ElementAccessExpressionSyntax index
                        && SymbolEqualityComparer.Default.Equals(semantic.GetSymbolInfo(index.Expression).Symbol, candidate)).ToArray() ?? [];
                    found = insertions.Length > 0 && insertions.All(invocation => invocation.ArgumentList.Arguments.Count == 2
                        && invocation.ArgumentList.Arguments.All(argument => ExpressionEvidence(argument.Expression, semantic, visiting, depth + 1)))
                        && replacements.All(assignment => ExpressionEvidence(assignment.Right, semantic, visiting, depth + 1));
                }
                if (found) break;
            }
            visiting.Remove(candidate); cache[candidate] = found; return found;
        }
        bool ExpressionEvidence(SyntaxNode expression, SemanticModel semantic, HashSet<ISymbol> visiting, int depth)
        {
            if (expression is LambdaExpressionSyntax lambda)
            {
                if (lambda.Body is ExpressionSyntax body) return ExpressionEvidence(body, semantic, visiting, depth);
                var returned = lambda.Body.DescendantNodes().OfType<ReturnStatementSyntax>().Where(statement => statement.Expression is not null).ToArray();
                return returned.Length > 0 && returned.All(statement => ExpressionEvidence(statement.Expression!, semantic, visiting, depth));
            }
            if (expression is ConditionalExpressionSyntax conditional) return ExpressionEvidence(conditional.WhenTrue, semantic, visiting, depth)
                && ExpressionEvidence(conditional.WhenFalse, semantic, visiting, depth);
            if (expression is InvocationExpressionSyntax invocation && semantic.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                && Evidence(method, visiting, depth + 1)) return true;
            if (expression is IdentifierNameSyntax identifier && semantic.GetSymbolInfo(identifier).Symbol is ISymbol dependency
                && dependency is IFieldSymbol or ILocalSymbol or IMethodSymbol && Evidence(dependency, visiting, depth + 1)) return true;
            return expression.ChildNodes().Any(child => ExpressionEvidence(child, semantic, visiting, depth));
        }
    }

    private static bool IsProductValidationFact(SemanticModel model, SyntaxNode node, string file)
    {
        // These private methods are invoked only by --validate-shell and publish controlled
        // typed facts into an isolated validation scope. Construction/mutation checks still
        // apply; this boundary exempts fixture text only, never an ordinary controller/file.
        string? owner = ProductOwner(model, node); string method = ProductMethod(node);
        if (owner == "Nexa.Desktop.Program")
        {
            var enclosing = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (enclosing is null || model.GetDeclaredSymbol(enclosing) is not IMethodSymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Private }) return false;
            return file switch
            {
                "Program.NxmlValidation.cs" => method is "ValidateDynamicNetworkPageAsync" or "ValidateDynamicTaskPage" or "ValidateDynamicDock"
                    or "ValidateDynamicGlobalOverlays" or "ValidateDynamicInstanceTools" or "ValidateDynamicVersionSelection"
                    or "ValidateDynamicBootstrap" or "ValidateDynamicWardrobe" or "ValidateDynamicResources" or "ValidateDynamicLaunchInstallAsync",
                "Program.LaunchInstallHomeValidation.cs" => method == "ValidateLaunchInstallHomeAndBedrock",
                "Program.SettingsGlobalNxmlValidation.cs" => method == "ValidateDynamicSettingsGlobalAsync",
                "Program.SettingsContentNxmlValidation.cs" => method == "ValidateDynamicSettingsContentAsync",
                "Program.SettingsNxmlValidation.cs" => method is "ValidateDynamicSettingsMigrationAsync" or "ValidateDynamicSettingsWorldsAsync",
                _ => false
            };
        }
        return file switch
        {
            "Program.NxmlValidation.cs" => owner switch
            {
                "Nexa.Desktop.Program.LaunchInstallValidationProfiles" => method is ".ctor" or "Load" or "Save",
                "Nexa.Desktop.Program.LaunchInstallValidationInstances" => method == "DiscoverAsync",
                "Nexa.Desktop.Program.LaunchInstallValidationCatalog" => method is "GetGamesAsync" or "GetLoadersAsync",
                _ => false
            },
            "Program.SettingsContentNxmlValidation.cs" => owner == "Nexa.Desktop.Program.SettingsContentValidationFixture" && method is ".ctor" or "Snapshot",
            "Program.SettingsNxmlValidation.cs" => owner == "Nexa.Desktop.Program.SettingsWorldValidationFacts" && method is ".ctor" or "CompleteServerStatus" or "CompleteRetiredVerification",
            _ => false
        };
    }

    private static bool IsFixedProductCopy(SemanticModel model, SyntaxNode literal, string file)
    {
        if (IsExternalProductTextArgument(model, literal) || IsProductValidationFact(model, literal, file)) return false;
        if (literal.Ancestors().Any(node => node is ConstantPatternSyntax or SwitchLabelSyntax or ElementAccessExpressionSyntax)) return false;
        if (literal.Ancestors().OfType<BinaryExpressionSyntax>().Any(node => node.IsKind(SyntaxKind.EqualsExpression) || node.IsKind(SyntaxKind.NotEqualsExpression))) return false;
        foreach (var argument in literal.Ancestors().OfType<ArgumentSyntax>())
        {
            if (argument.Parent?.Parent is not InvocationExpressionSyntax invocation
                || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method) continue;
            var parameter = ProductArgumentParameter(method, invocation.ArgumentList, argument);
            string owner = method.ContainingType.ToDisplayString();
            if (owner == "Nexa.Desktop.Ui.DesktopFeedbackService" && parameter?.Name is "key") return false;
            if (owner == "Microsoft.Win32.RegistryKey" && parameter?.Ordinal == 0
                && method.Name is "CreateSubKey" or "OpenSubKey" or "DeleteSubKey" or "DeleteSubKeyTree" or "GetValue" or "SetValue" or "DeleteValue") return false;
        }
        foreach (var invocation in literal.Ancestors().OfType<InvocationExpressionSyntax>())
        {
            string name = invocation.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberBindingExpressionSyntax member => member.Name.Identifier.ValueText,
                _ => "",
            };
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method) continue;
            string owner = method.ContainingType.ToDisplayString();
            if (owner is "System.Console" or "System.IO.TextWriter" or "System.Diagnostics.Debug" or "System.Diagnostics.Trace"
                || method.ContainingNamespace.ToDisplayString().StartsWith("Nexa.Services.Logging", StringComparison.Ordinal)) return false;
            if (method.ContainingType.SpecialType == SpecialType.System_String && name is "Contains" or "StartsWith" or "EndsWith" or "Equals") return false;
            if (method.MethodKind == MethodKind.DelegateInvoke)
            {
                string receiver = invocation.Expression is IdentifierNameSyntax identifier ? identifier.Identifier.ValueText
                    : invocation.Parent is ConditionalAccessExpressionSyntax conditional && conditional.Expression is IdentifierNameSyntax target ? target.Identifier.ValueText : "";
                if (receiver is "_log" or "log") return false;
                if (receiver == "report" && ProductOwner(model, literal) is "Nexa.Desktop.Ui.DesktopInstallRecoverySession" or "Nexa.Desktop.DesktopNativeFileActivation") return false;
            }
        }
        if (literal.Ancestors().OfType<BaseObjectCreationExpressionSyntax>().Any(creation =>
            model.GetTypeInfo(creation).Type is INamedTypeSymbol type && InheritsException(type))) return false;
        // This bridge's returned diagnostics have exactly one ordinary consumer: the
        // SystemPreferencesSession logging callback. Its native caption creation is checked.
        if (ProductOwner(model, literal) == "Nexa.Desktop.DesktopJumpList" && ProductMethod(literal) == "ApplyAsync") return false;
        return true;
    }

    private static bool InheritsException(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == "System.Exception") return true;
        return false;
    }

    private static void ValidateProductPresentationGuardFixtures(List<string> failures)
    {
        var primitives = CSharpSyntaxTree.ParseText("""
            namespace Nexa.Xsr.Renderer
            {
                public class XsrUiText(string content) { public string Content { get; set; } = content; }
                public class XsrUiInput { public bool Enabled { get; set; } public bool IsFocused { get; set; } }
                public class XsrUiElement { public double Width { get; set; } public bool IsVisible { get; set; } public double Margin { get; set; } }
                public class XsrUiVisualStyle { public double Background { get; set; } public double Foreground { get; set; } public double FontSize { get; set; } }
                public class XsrUiScroll { public double OffsetY { get; set; } }
                public class XsrUiSegmentedTrack { public int Selected { get; set; } }
                public class XsrUiTransition { public string Key { get; set; } = ""; }
                public class XsrUiImage { public object? Raster { get; set; } }
                public class XsrUiOverlayLayer(bool isModal) { public bool IsModal { get; set; } = isModal; }
                public class XsrUiShellOptions { public string Title { get; set; } = ""; }
                public class XsrUiRenderer { public string LocalizeText(string text) => text; }
                public record XsrUiContextMenuItem(string Label, string Key);
                public class XsrUiTree
                {
                    public int Create(string key) => 1;
                    public void SetComponent<T>(int entity, T component) { }
                }
            }
            namespace Nexa.Xsr.Renderer.Nxml
            {
                public record NxmlIrNode { public string Content { get; init; } = ""; }
                public static class NxmlParser { public static object Parse(string text) => text; }
            }
            """);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        List<string> Check(string source, string file)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: Path.Combine("Nexa.Desktop", "Ui", file));
            var compilation = CSharpCompilation.Create("NxmlClosureGuardFixture", [primitives, tree], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0) failures.Add("Product presentation guard fixture must compile: " + string.Join("; ", errors.Select(error => error.ToString())));
            List<string> observed = [];
            ValidateProductPresentation(compilation.GetSemanticModel(tree), tree.GetRoot(), ".", tree.FilePath, observed);
            return observed;
        }

        var blocked = Check("""
            using Caption = Nexa.Xsr.Renderer.XsrUiText;
            using Nexa.Xsr.Renderer;
            using Nexa.Xsr.Renderer.Nxml;
            class Consumer
            {
                Caption Build(XsrUiTree tree, Caption caption, XsrUiInput input, XsrUiVisualStyle style, NxmlIrNode node)
                {
                    Caption aliased = new("fixed");
                    (caption.Content, input.Enabled) = ("changed", true);
                    style.FontSize++;
                    tree.Create("child");
                    tree.SetComponent(1, caption);
                    Generic(tree, caption);
                    _ = node with { Content = "projected" };
                    return aliased;
                }
                void Generic<T>(XsrUiTree tree, T value) { tree.SetComponent(1, value); }
            }
            """, "ClosureConsumer.cs");
        foreach (string expected in new[] { "XsrUiText construction", "XsrUiText.Content", "XsrUiInput.Enabled", "XsrUiVisualStyle.FontSize", "tree construction", "presentation components", "generic component factories", "IR projections" })
            if (!blocked.Any(failure => failure.Contains(expected, StringComparison.Ordinal))) failures.Add("Product presentation guard missed " + expected + ".");

        var interaction = Check("""
            using Nexa.Xsr.Renderer;
            class Consumer
            {
                void Update(XsrUiScroll scroll, XsrUiSegmentedTrack track, XsrUiTransition transition, XsrUiInput input, XsrUiImage image)
                { scroll.OffsetY = 12; track.Selected = 1; transition.Key = "navigation-epoch"; input.IsFocused = true; image.Raster = null; }
                void Text(System.Collections.Generic.Dictionary<string, string> values) { values["caption"] = "literal metadata"; }
            }
            """, "ClosureConsumer.cs");
        if (interaction.Count > 0) failures.Add("Product presentation guard rejected approved interaction or literal-data projection.");

        var background = Check("""
            using Nexa.Xsr.Renderer;
            namespace Nexa.Desktop.Ui { class DesktopBackgroundPresentation
            { void Present(XsrUiVisualStyle style) { style.Background = 1; style.Foreground = 2; } } }
            """, "DesktopBackgroundAppearance.cs");
        if (background.Count != 1 || !background[0].Contains("XsrUiVisualStyle.Foreground", StringComparison.Ordinal))
            failures.Add("Background primitive exception must allow only the user-color property.");

        var title = Check("""
            using Nexa.Xsr.Renderer;
            namespace Nexa.Desktop.Ui { class NxmlShellComposer
            { void ComposeCore(XsrUiText text, XsrUiShellOptions options) { text.Content = options.Title; text.Content = "fixed"; } } }
            """, "NxmlShellComposer.cs");
        if (title.Count != 1 || !title[0].Contains("XsrUiText.Content", StringComparison.Ordinal))
            failures.Add("Title primitive exception must allow only literal host options.");

        var overlay = Check("""
            using Nexa.Xsr.Renderer;
            namespace Nexa.Desktop.Ui { class DesktopDynamicLayer
            { void Append(XsrUiTree tree) { tree.SetComponent(1, new XsrUiOverlayLayer(false)); tree.SetComponent(2, new XsrUiOverlayLayer(true)); } } }
            """, "DesktopDynamicLayer.cs");
        if (overlay.Count != 2) failures.Add("Overlay lifetime exception must reject modal presentation construction.");

        var copy = Check("""
            namespace Nexa.Desktop.Ui { public class DesktopFeedbackService { public void Info(string message) { } } }
            class Consumer
            {
                void Emit(Nexa.Desktop.Ui.DesktopFeedbackService feedback, string version)
                {
                    feedback.Info("固定提示"); feedback.Info($"进度 {version}"); feedback.Info("Visible wording");
                    _ = new System.IO.IOException("内部诊断"); System.Console.WriteLine("内部日志");
                    bool identity = version == "模组";
                    System.Action<string> _log = _ => { }; _log("内部日志");
                }
            }
            """, "ClosureConsumer.cs");
        if (copy.Count != 3 || !copy.Any(failure => failure.Contains("wording", StringComparison.Ordinal))
            || !copy.Any(failure => failure.Contains("formats", StringComparison.Ordinal)))
            failures.Add("Product copy guard must distinguish visible fixed copy from logs, diagnostics and typed identities.");

        var selectors = Check("""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            namespace Nexa.Desktop.Ui
            {
                class DesktopFeedbackService
                {
                    public void Info(string message) { }
                    public void ShowDialog(string key, string title, string message, string acceptLabel, string cancelLabel) { }
                }
                static class DesktopMarkup
                {
                    public static string Read(string asset) => asset;
                    public static List<Node> Load(string asset) => [];
                }
                record Node(string Key, string Content);
                static class ExternalText
                {
                    static readonly Lazy<Dictionary<string, string>> Terms = new(() =>
                    {
                        Dictionary<string, string> terms = new();
                        foreach (string asset in new[] { "Terms.nxml", "OtherTerms.nxml" })
                        {
                            var nodes = DesktopMarkup.Load(asset);
                            foreach (var node in nodes) terms.TryAdd(node.Key, node.Content);
                        }
                        return terms;
                    });
                    public static string Raw(string key) => Terms.Value[key];
                    public static string Term(string key, Func<string, string> localize) => localize(Raw(key));
                    public static string Format(string name, params (string Key, string Value)[] data) => DesktopMarkup.Read(name);
                    public static string DictionaryFormat(string name, Dictionary<string, string> data) => DesktopMarkup.Read(name);
                }
                class Consumer
                {
                    string Copy(string key, Nexa.Xsr.Renderer.XsrUiRenderer renderer) => renderer.LocalizeText(ExternalText.Raw(key));
                    void Emit(DesktopFeedbackService feedback, Nexa.Xsr.Renderer.XsrUiRenderer renderer, string path)
                    {
                        feedback.Info(renderer.LocalizeText(ExternalText.Raw("use-skin")));
                        feedback.Info(Copy("copy.key", renderer));
                        feedback.Info(ExternalText.Term("wording-key", renderer.LocalizeText));
                        feedback.Info(ExternalText.Format("NamedFormat", ("path", path)));
                        feedback.Info(ExternalText.DictionaryFormat("NamedFormat", new() { ["path"] = path }));
                        feedback.ShowDialog("dialog.identity", ExternalText.Raw("title"), ExternalText.Raw("message"), ExternalText.Raw("accept"), ExternalText.Raw("cancel"));
                        _ = new Nexa.Xsr.Renderer.XsrUiContextMenuItem(ExternalText.Raw("menu.caption"), "menu.identity");
                    }
                }
            }
            """, "ClosureConsumer.cs");
        if (selectors.Count > 0) failures.Add("Product copy guard rejected external selector keys, merged asset terms or named-format parameter identities.");

        var dictionaryValues = Check("""
            namespace Nexa.Desktop.Ui
            {
                class DesktopFeedbackService { public void Info(string message) { } }
                static class DesktopMarkup { public static string Read(string asset) => asset; }
                static class ExternalText
                { public static string Format(string name, System.Collections.Generic.Dictionary<string, string> data) => DesktopMarkup.Read(name); }
                class Consumer
                {
                    void Emit(DesktopFeedbackService feedback)
                    {
                        feedback.Info(ExternalText.Format("NamedFormat", new() { ["path"] = "Visible dictionary value" }));
                        feedback.Info(ExternalText.Format("NamedFormat", new() { ["error"] = "可见字典值" }));
                    }
                }
            }
            """, "ClosureConsumer.cs");
        if (dictionaryValues.Count != 2 || dictionaryValues.Any(failure => !failure.Contains("fixed product wording", StringComparison.Ordinal)))
            failures.Add("Named-format dictionary keys are metadata, but their fixed values must remain visible to the copy guard.");

        var counterfeit = Check("""
            namespace Nexa.Desktop.Ui
            {
                class DesktopFeedbackService { public void Info(string message) { } }
                static class DesktopMarkup { public static string Read(string asset) => asset; }
                static class Counterfeit
                {
                    public static string Raw(string key) => key;
                    public static string Term(string key) { _ = DesktopMarkup.Read("unused.nxml"); return key; }
                    public static string Copy(string key) => DesktopMarkup.Read("terms.nxml") + key;
                    public static string Format(string name, params (string Key, string Value)[] data) => DesktopMarkup.Read(name);
                }
                class Consumer
                {
                    void Emit(DesktopFeedbackService feedback, Nexa.Xsr.Renderer.XsrUiRenderer renderer)
                    {
                        feedback.Info(renderer.LocalizeText(Counterfeit.Raw("Fixed through Raw")));
                        feedback.Info(Counterfeit.Term("伪装文案"));
                        feedback.Info(Counterfeit.Copy("Visible appended copy"));
                        feedback.Info(Counterfeit.Format("NamedFormat", ("path", "Visible format value")));
                        feedback.Info(Counterfeit.Format("NamedFormat", ("path", "可见格式值")));
                    }
                }
            }
            """, "ClosureConsumer.cs");
        if (counterfeit.Count != 5 || counterfeit.Any(failure => !failure.Contains("fixed product wording", StringComparison.Ordinal)))
            failures.Add("Product copy guard must reject counterfeit selectors, unused resource reads and visible named-format values.");

        var sinkParameters = Check("""
            namespace Nexa.Desktop.Ui
            {
                class DesktopFeedbackService
                { public void ShowDialog(string key, string title, string message, string acceptLabel, string cancelLabel) { } }
                class Consumer
                {
                    void Emit(DesktopFeedbackService feedback)
                    {
                        feedback.ShowDialog("dialog.identity", "Visible title", "Visible message", "Accept caption", "Cancel caption");
                        _ = new Nexa.Xsr.Renderer.XsrUiContextMenuItem("Visible menu label", "menu.identity");
                    }
                }
            }
            """, "ClosureConsumer.cs");
        if (sinkParameters.Count != 5) failures.Add("Product copy guard must distinguish feedback and menu labels from their command identities.");

        var validationFacts = Check("""
            namespace Nexa.Desktop
            {
                static class Program
                {
                    private static void ValidateDynamicSettingsWorldsAsync(Nexa.Xsr.Renderer.XsrUiText text)
                    { string path = "夹具路径"; text.Content = path; }
                    private static void OrdinaryController() { string caption = "真实固定文案"; }
                }
            }
            """, "Program.SettingsNxmlValidation.cs");
        if (validationFacts.Count != 2 || !validationFacts.Any(failure => failure.Contains("XsrUiText.Content", StringComparison.Ordinal))
            || !validationFacts.Any(failure => failure.Contains("fixed product wording", StringComparison.Ordinal)))
            failures.Add("CLI validation text boundary must remain exact and keep presentation mutation checks active.");

        var copiedValidationName = Check("""
            namespace Nexa.Desktop
            { static class Program { private static void ValidateDynamicSettingsWorldsAsync() { string caption = "真实固定文案"; } } }
            """, "OrdinaryController.cs");
        if (copiedValidationName.Count != 1) failures.Add("A validation method name in an ordinary file must not exempt fixed copy.");

        var xml = Check("""
            using Nexa.Xsr.Renderer.Nxml;
            class Consumer { void Build(string key) { _ = NxmlParser.Parse($"<nxml-dynamic><Text Key='{key}' /></nxml-dynamic>"); } }
            """, "ClosureConsumer.cs");
        if (!xml.Any(failure => failure.Contains("interpolated product markup", StringComparison.Ordinal)))
            failures.Add("Product presentation guard must reject interpolated XML.");
    }
}
