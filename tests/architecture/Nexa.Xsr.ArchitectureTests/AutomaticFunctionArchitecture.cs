using System.Xml.Linq;

namespace Nexa.Xsr.ArchitectureTests;

internal static partial class Program
{
    private static void ValidateAutomaticFunctionPatches(string root, Dictionary<string, string> projects,
        XDocument common, XDocument compiler, List<string> failures)
    {
        var enabled = Elements(common, "NexaAutomaticFunctionPatches").SingleOrDefault();
        if (enabled?.Value != "true"
            || enabled.Attribute("Condition")?.Value.Contains("NexaProductSource", StringComparison.Ordinal) != true
            || !Elements(common, "Import").Any(item => item.Attribute("Project")?.Value.EndsWith("Xsr.AutomaticFunctionPatches.targets", StringComparison.Ordinal) == true))
            failures.Add("Every owned product project must inherit automatic function instrumentation from the common build.");
        foreach (var (name, path) in projects.Where(item => Path.GetRelativePath(root, item.Value).Replace('\\', '/').StartsWith("src/", StringComparison.Ordinal)))
            if (Elements(XDocument.Load(path), "NexaAutomaticFunctionPatches").Any(item => item.Value != "true"))
                failures.Add($"{name} must not opt out of automatic product instrumentation.");
        // Rendering is exempt as a whole, by the common build; coverage verification selects the
        // same group by assembly identity, so that identity must not exist anywhere else.
        var properties = XDocument.Load(RepositoryPath(root, "Directory.Build.props"));
        if (enabled?.Attribute("Condition")?.Value.Contains("'$(NexaRenderingSource)' != 'true'", StringComparison.Ordinal) != true
            || Elements(properties, "NexaRenderingRoot").SingleOrDefault()?.Value.Contains("'rendering'", StringComparison.Ordinal) != true
            || Elements(properties, "NexaRenderingSource").SingleOrDefault()?.Attribute("Condition")?.Value.Contains("$(NexaRenderingRoot)", StringComparison.Ordinal) != true)
            failures.Add("Only the rendering root is exempt from automatic instrumentation, through the common build.");
        foreach (var (name, path) in projects.Where(item => item.Key == "Nexa.Xsr.Renderer" || item.Key.StartsWith("Nexa.Xsr.Renderer.", StringComparison.Ordinal)))
        {
            // Tests and tools may share the prefix; they are neither instrumented nor shipped.
            string location = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (location.StartsWith("src/", StringComparison.Ordinal) && !location.StartsWith("src/rendering/", StringComparison.Ordinal))
                failures.Add($"{name} is a product assembly with the rendering identity outside the rendering root.");
        }

        var target = XDocument.Load(RepositoryPath(root, "eng/xsr/Xsr.AutomaticFunctionPatches.targets"));
        var instrument = Elements(target, "Target").SingleOrDefault(item => item.Attribute("Name")?.Value == "InstrumentNexaFunctions");
        if (instrument?.Attribute("AfterTargets")?.Value != "CoreCompile"
            || instrument.Descendants().Any(item => item.Name.LocalName is "Reflection" or "AssemblyLoad")
            || !Elements(target, "Exec").Any(item => item.Attribute("Command")?.Value.Contains("--instrument", StringComparison.Ordinal) == true)
            || !Elements(target, "Content").Any(item => item.Attribute("CopyToPublishDirectory")?.Value == "PreserveNewest"
                && item.Attribute("TargetPath")?.Value == "$(AssemblyName).nfp"))
            failures.Add("Automatic instrumentation must weave lowered bodies before publish and ship the matching function catalog.");
        var reference = Elements(common, "ProjectReference").Single();
        if (!Elements(common, "AssemblyMetadata").Any(item => item.Attribute("Include")?.Value == "Nexa.AutomaticFunctionPatches"
            && item.Attribute("Value")?.Value == "1"))
            failures.Add("Instrumented product references must advertise their bootstrap ABI in compiler reference metadata.");
        if (reference.Attribute("GlobalPropertiesToRemove")?.Value.Contains("PublishAot", StringComparison.Ordinal) != true
            || !Elements(compiler, "PackageReference").Any(item => item.Attribute("Include")?.Value == "Mono.Cecil"
                && item.Attribute("PrivateAssets")?.Value == "all"))
            failures.Add("IL rewriting must remain an isolated managed build dependency, including NativeAOT builds.");
        foreach (var (name, path) in projects.Where(item => item.Key != "Nexa.Xsr.Patch.Compiler"))
            if (Elements(XDocument.Load(path), "PackageReference").Any(item => item.Attribute("Include")?.Value is "Mono.Cecil" or "Microsoft.CodeAnalysis.CSharp"))
                failures.Add($"{name} must not acquire the instrumentation compiler's runtime dependencies.");

        string workflow = File.ReadAllText(RepositoryPath(root, ".github/workflows/xsr-ci.yml"));
        if (!workflow.Contains("--self-test-automatic", StringComparison.Ordinal)
            || !workflow.Contains("--verify-automatic", StringComparison.Ordinal))
            failures.Add("CI must execute automatic signature/phase tests and verify coverage of the real Desktop assembly closure.");
    }
}
