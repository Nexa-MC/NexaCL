using System.Xml.Linq;

namespace Nexa.Xsr.ArchitectureTests;

internal static partial class Program
{
    private static IReadOnlyDictionary<string, string[]> AllowedReferences = new Dictionary<string, string[]>();
    private static readonly Dictionary<string, string[]> ActualReferences = new(StringComparer.Ordinal);

    private static readonly HashSet<string> ExecutableProjects =
        [
            "CapabilityProbe",
            "Nexa.Minecraft.Benchmarks",
            "Nexa.Jvm.Host",
            "Nexa.Update.Helper",
            "Nexa.Desktop",
            "Nexa.Xsr.Renderer.Benchmarks",
            "Nexa.Xsr.ArchitectureTests",
            "Nexa.Xsr.Runtime.Tests",
            "Nexa.Xsr.Patch.Compiler",
            "Nexa.Xsr.Renderer.Tests",
            "Nexa.Xsr.Renderer.Backend.Avalonia.Tests",
            "Nexa.Xsr.Renderer.Nxml.Tests",
            "Nexa.Sidecar.Tests",
            "Nexa.Services.Tests",
            "Nexa.Desktop.Tests",
        ];

    private static readonly HashSet<string> GeneratorProjects =
        [];

    private static readonly HashSet<string> AotCompatibleProjects =
        [
            "Nexa.Jvm.Host",
            "Nexa.Xsr.Abstractions",
            "Nexa.Xsr.Runtime",
            "Nexa.Xsr.State",
            "Nexa.Xsr.Diagnostics",
            "Nexa.Xsr.Renderer",
            "Nexa.Xsr.Renderer.Nxml.Compiler",
            "Nexa.Xsr.Renderer.Nxml.Runtime",
            "Nexa.Services",
            "Nexa.Services.Composition",
        ];

    public static int Main(string[] args)
    {
        string repositoryRoot = ResolveRepositoryRoot(args);
        AllowedReferences = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            File.ReadAllText(Path.Combine(repositoryRoot, "docs/xsr/project-references.json")))!;
        List<string> failures = [];
        Dictionary<string, string> projectPaths = DiscoverProjects(repositoryRoot, failures);

        ValidateProjectInventory(projectPaths, failures);
        ValidateProjects(repositoryRoot, projectPaths, failures);
        ValidateSolution(repositoryRoot, failures);
        ValidateCommonBuildProperties(repositoryRoot, failures);
        ValidateSemanticBoundaries(repositoryRoot, projectPaths, failures);

        ValidateNativeHostInterop(repositoryRoot, failures);
        ValidateProtectedDifferentialUpdates(repositoryRoot, failures);
        ValidateNxmlControlCatalog(repositoryRoot, projectPaths, failures);
        ValidateFunctionPatchCompiler(repositoryRoot, projectPaths, failures);
        ValidateWave3Ci(repositoryRoot, failures);
        ValidateAcyclicGraph(failures);

        if (failures.Count == 0)
        {
            Console.WriteLine($"XSR architecture tests passed for {projectPaths.Count} projects.");
            return 0;
        }

        Console.Error.WriteLine($"XSR architecture tests failed with {failures.Count} error(s):");
        foreach (string failure in failures.Order(StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return 1;
    }

    private static void ValidateNativeHostInterop(string repositoryRoot, List<string> failures)
    {
        foreach (string file in Directory.EnumerateFiles(Path.Combine(repositoryRoot, "Nexa.Desktop"), "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file)) continue;
            string source = File.ReadAllText(file);
            if (source.Contains("[ComImport", StringComparison.Ordinal)
                || source.Contains("Marshal.GetObjectForIUnknown", StringComparison.Ordinal)
                || source.Contains("Marshal.GetTypedObjectForIUnknown", StringComparison.Ordinal))
                failures.Add($"{Path.GetRelativePath(repositoryRoot, file)} uses built-in COM, unsupported by NativeAOT; use generated COM wrappers.");
        }
    }

    private static void ValidateProtectedDifferentialUpdates(string root, List<string> failures)
    {
        foreach (string name in new[] { "AutomaticUpdateTransaction.cs", "ProtectedDeltaExtractor.cs" })
        {
            string file = Path.Combine(root, "Nexa.Services.Updates", "Updates", name);
            string source = File.ReadAllText(file);
            foreach (string forbidden in new[] { "File.Open", "File.Create", "File.Delete", "Directory.", "Path.Combine", "ExtractToDirectory", "Process.Start" })
                if (source.Contains(forbidden, StringComparison.Ordinal))
                    failures.Add($"{name} bypasses retained update namespace authority with {forbidden}.");
        }
        string transport = File.ReadAllText(Path.Combine(root, "Nexa.Services.Updates", "Updates", "GitHubUpdateReleaseSource.cs"));
        if (!transport.Contains("IUpdateDeltaSource.ReadDeltaIndexAsync", StringComparison.Ordinal)
            || !transport.Contains("Nexa-Delta.json.asc", StringComparison.Ordinal))
            failures.Add("Differential discovery must use the independent fixed-repository publisher transport.");
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "launcher-build.yml"));
        foreach (string required in new[] { "delta_history.py", "delta.py", "'automatic update native'", "eng/release/sign.py" })
            if (!workflow.Contains(required, StringComparison.Ordinal))
                failures.Add($"Differential publishing/native transaction validation is missing {required}.");
    }

    private static string ResolveRepositoryRoot(string[] args)
    {
        int optionIndex = Array.IndexOf(args, "--repo-root");
        string candidate = optionIndex >= 0 && optionIndex + 1 < args.Length
            ? args[optionIndex + 1]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");

        return Path.GetFullPath(candidate);
    }

    private static Dictionary<string, string> DiscoverProjects(string repositoryRoot, List<string> failures)
    {
        string[] paths = Directory
            .EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, string> projects = new(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!projects.TryAdd(name, path))
            {
                failures.Add($"Duplicate project name '{name}' at '{path}'.");
            }
        }

        return projects;
    }

    private static bool IsBuildOutput(string path)
    {
        string[] segments = Path.GetRelativePath(Environment.CurrentDirectory, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateProjectInventory(
        Dictionary<string, string> projectPaths,
        List<string> failures)
    {
        foreach (string missing in AllowedReferences.Keys.Except(projectPaths.Keys, StringComparer.Ordinal))
        {
            failures.Add($"Required project '{missing}' is missing.");
        }

        foreach (string unexpected in projectPaths.Keys.Except(AllowedReferences.Keys, StringComparer.Ordinal))
        {
            failures.Add($"Project '{unexpected}' is not registered in the architecture graph.");
        }
    }

    private static void ValidateProjects(
        string repositoryRoot,
        Dictionary<string, string> projectPaths,
        List<string> failures)
    {
        foreach ((string projectName, string[] allowed) in AllowedReferences)
        {
            if (!projectPaths.TryGetValue(projectName, out string? projectPath))
            {
                continue;
            }

            XDocument project = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
            HashSet<string> actualReferences = [];

            foreach (XElement reference in Elements(project, "ProjectReference"))
            {
                string? include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    failures.Add($"{projectName} has a ProjectReference without Include.");
                    continue;
                }

                string targetPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, include));
                if (!IsInsideRepository(repositoryRoot, targetPath))
                {
                    failures.Add($"{projectName} references a project outside the XSR repository: '{include}'.");
                    continue;
                }

                if (!File.Exists(targetPath))
                {
                    failures.Add($"{projectName} references missing project '{include}'.");
                    continue;
                }

                actualReferences.Add(Path.GetFileNameWithoutExtension(targetPath));
            }

            foreach (string forbidden in actualReferences.Except(allowed, StringComparer.Ordinal))
            {
                failures.Add($"{projectName} has forbidden project reference '{forbidden}'.");
            }

            foreach (string missing in allowed.Except(actualReferences, StringComparer.Ordinal))
            {
                failures.Add($"{projectName} is missing locked project reference '{missing}'.");
            }

            ActualReferences[projectName] = actualReferences.ToArray();
            bool portable = projectName.EndsWith(".Contracts", StringComparison.Ordinal)
                || projectName is "Nexa.Core" or "Nexa.Domain" or "Nexa.Contracts" or "Nexa.Platform.Abstractions";
            if (portable)
                foreach (string implementation in actualReferences.Where(IsServiceImplementation))
                    failures.Add($"Portable contract {projectName} references implementation {implementation}.");
            if (!GeneratorProjects.Contains(projectName)
                && !Directory.EnumerateFiles(Path.GetDirectoryName(projectPath)!, "*.cs", SearchOption.AllDirectories)
                    .Any(path => !IsBuildOutput(path)))
                failures.Add($"Empty source project: {projectName}.");
            ValidateProjectKind(projectName, project, failures);
            ValidateFrameworkPackages(projectName, project, failures);
        }
    }

    private static void ValidateProjectKind(string projectName, XDocument project, List<string> failures)
    {
        if (projectName == "Nexa.Desktop" && string.Equals(Property(project, "CETCompat"), "false", StringComparison.OrdinalIgnoreCase))
            failures.Add("Desktop must not inherit the isolated JVM host CET exception.");
        if (projectName == "Nexa.Jvm.Host" && !string.Equals(Property(project, "CETCompat"), "false", StringComparison.OrdinalIgnoreCase))
            failures.Add("The isolated JVM host requires its documented CET compatibility setting.");
        string? outputType = Property(project, "OutputType");
        string expectedOutputType = projectName is "Nexa.Desktop" or "Nexa.Update.Helper" ? "WinExe" : "Exe";
        if (ExecutableProjects.Contains(projectName) && !string.Equals(outputType, expectedOutputType, StringComparison.Ordinal))
        {
            failures.Add($"{projectName} must remain an executable project with OutputType={expectedOutputType}.");
        }

        if (GeneratorProjects.Contains(projectName)
            && !string.Equals(Property(project, "IsRoslynComponent"), "true", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{projectName} must remain marked as a Roslyn component.");
        }

        if (AotCompatibleProjects.Contains(projectName)
            && !string.Equals(Property(project, "IsAotCompatible"), "true", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{projectName} must remain marked as AOT compatible.");
        }
    }

    private static void ValidateFunctionPatchCompiler(string root, Dictionary<string, string> projects, List<string> failures)
    {
        if (!projects.TryGetValue("Nexa.Xsr.Patch.Compiler", out var compilerPath))
        { failures.Add("Explicit Function Patch compiler project is missing."); return; }
        var compiler = XDocument.Load(compilerPath);
        var desktop = XDocument.Load(projects["Nexa.Desktop"]);
        var reference = Elements(desktop, "ProjectReference").SingleOrDefault(item =>
            item.Attribute("Include")?.Value.Replace('\\', '/').EndsWith("Nexa.Xsr.Patch.Compiler.csproj", StringComparison.Ordinal) == true);
        if (reference?.Attribute("ReferenceOutputAssembly")?.Value != "false"
            || reference.Attribute("Private")?.Value != "false"
            || Property(compiler, "ShouldBeValidatedAsExecutableReference") != "false"
            || Property(compiler, "SelfContained") != "false" || Property(compiler, "PublishAot") != "false")
            failures.Add("Function Patch compiler must be an isolated, non-shipped managed build tool.");
        if (Property(desktop, "ValidateExecutableReferencesMatchSelfContained") == "false")
            failures.Add("Desktop must retain product executable-reference validation.");
        var targets = XDocument.Load(Path.Combine(root, "eng/xsr/Xsr.FunctionPatches.targets"));
        if (!Elements(targets, "MSBuild").Any(task => task.Attribute("Targets")?.Value == "GetTargetPath"
            && task.Attribute("Projects")?.Value == "$(_NexaPatchCompilerProject)"
            && task.Descendants().Any(output => output.Name.LocalName == "Output"
                && output.Attribute("TaskParameter")?.Value == "TargetOutputs")))
            failures.Add("Function Patch compiler must resolve its actual build output instead of a fixed bin path.");
        var replacement = Elements(targets, "Target").SingleOrDefault(target => target.Attribute("Name")?.Value == "UseNexaFunctionPatches");
        if (replacement?.Attribute("BeforeTargets")?.Value != "CoreCompile"
            || !replacement!.Descendants().Any(item => item.Name.LocalName == "Compile"
                && item.Attribute("Remove")?.Value == "@(NexaFunctionPatchSource)"
                && item.Attribute("MatchOnMetadata")?.Value == "FullPath"
                && item.Attribute("MatchOnMetadataOptions")?.Value == "PathLike")
            || !replacement.Descendants().Any(item => item.Name.LocalName == "Compile" && item.Attribute("Include")?.Value == "$(_NexaPatchedSource)"))
            failures.Add("Function Patch compile-item replacement must run before CoreCompile on incremental builds.");
    }

    private static void ValidateFrameworkPackages(string projectName, XDocument project, List<string> failures)
    {
        foreach (XElement package in Elements(project, "PackageReference"))
        {
            string? packageName = package.Attribute("Include")?.Value;
            if (packageName is null)
            {
                continue;
            }

            if (AllowedReferences.ContainsKey(packageName))
            {
                failures.Add($"{projectName} bypasses the project graph with internal package '{packageName}'.");
            }

            // Avalonia.Headless is the one sanctioned exception outside the backend project: the
            // backend test project runs the real desktop lifetime headlessly for regression
            // tests and must never leak the platform into product projects.
            bool headlessLifetimeTestPackage = string.Equals(projectName, "Nexa.Xsr.Renderer.Backend.Avalonia.Tests", StringComparison.Ordinal)
                && string.Equals(packageName, "Avalonia.Headless", StringComparison.Ordinal);
            if (!string.Equals(projectName, "Nexa.Xsr.Renderer.Backend.Avalonia", StringComparison.Ordinal)
                && packageName.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
                && !headlessLifetimeTestPackage)
            {
                failures.Add($"{projectName} must not reference Avalonia package '{packageName}'.");
            }
        }
    }

    private static void ValidateSolution(string repositoryRoot, List<string> failures)
    {
        string solutionPath = Path.Combine(repositoryRoot, "NexaCL.slnx");
        if (!File.Exists(solutionPath))
        {
            failures.Add("NexaCL.slnx is missing.");
            return;
        }

        XDocument solution = XDocument.Load(solutionPath);
        HashSet<string> solutionProjects = [];
        foreach (XElement project in Elements(solution, "Project"))
        {
            string? path = project.Attribute("Path")?.Value;
            if (string.IsNullOrWhiteSpace(path))
            {
                failures.Add("NexaCL.slnx contains a Project without Path.");
                continue;
            }

            string projectPath = Path.GetFullPath(Path.Combine(repositoryRoot, path));
            if (!IsInsideRepository(repositoryRoot, projectPath) || !File.Exists(projectPath))
            {
                failures.Add($"NexaCL.slnx contains invalid project path '{path}'.");
                continue;
            }

            solutionProjects.Add(Path.GetFileNameWithoutExtension(projectPath));
        }

        foreach (string missing in AllowedReferences.Keys.Except(solutionProjects, StringComparer.Ordinal))
        {
            failures.Add($"NexaCL.slnx is missing project '{missing}'.");
        }

        foreach (string unexpected in solutionProjects.Except(AllowedReferences.Keys, StringComparer.Ordinal))
        {
            failures.Add($"NexaCL.slnx contains unregistered project '{unexpected}'.");
        }
    }

    private static void ValidateCommonBuildProperties(string repositoryRoot, List<string> failures)
    {
        string propsPath = Path.Combine(repositoryRoot, "Directory.Build.props");
        if (!File.Exists(propsPath))
        {
            failures.Add("Directory.Build.props is missing.");
            return;
        }

        XDocument props = XDocument.Load(propsPath);
        bool importsVersion = Elements(props, "Import")
            .Select(element => element.Attribute("Project")?.Value)
            .Any(path => path?.Replace('\\', '/').EndsWith("eng/xsr/Xsr.Version.props", StringComparison.Ordinal) == true);

        if (!importsVersion)
        {
            failures.Add("Directory.Build.props must import eng/xsr/Xsr.Version.props.");
        }

        if (!string.Equals(Property(props, "TargetFramework"), "net10.0", StringComparison.Ordinal))
        {
            failures.Add("The XSR project graph must target net10.0 by default.");
        }

        if (!string.Equals(Property(props, "TreatWarningsAsErrors"), "true", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("TreatWarningsAsErrors must remain enabled for XSR projects.");
        }
    }

    private static void ValidateAcyclicGraph(List<string> failures)
    {
        HashSet<string> visiting = new(StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal);

        foreach (string project in ActualReferences.Keys)
        {
            Visit(project, visiting, visited, failures);
        }
    }

    private static void ValidateNxmlControlCatalog(
        string repositoryRoot,
        Dictionary<string, string> projectPaths,
        List<string> failures)
    {
        string directory = Path.Combine(repositoryRoot, "Nexa.Xsr.Renderer", "Components");
        string[] components = Directory.GetFiles(directory, "*.c.nxml");
        if (components.Length < 17) failures.Add("The external NXML primitive component bundle is incomplete.");
        foreach (string file in components)
        {
            XElement root = XDocument.Load(file).Root!;
            if (root.Name.LocalName != "nxml-component" || root.Elements().Count() != 1)
                failures.Add($"Invalid external component wrapper: {file}");
        }
        string dynamicDirectory = Path.Combine(repositoryRoot, "Nexa.Desktop", "Ui", "Dynamic");
        foreach (string file in Directory.GetFiles(dynamicDirectory, "*.d.nxml"))
        {
            XElement root = XDocument.Load(file).Root!;
            if (root.Name.LocalName != "nxml-dynamic" || root.Elements().Count() != 1)
                failures.Add($"Invalid dynamic template wrapper: {file}");
        }
        // Completed migrations must not regress to imperative presentation factories.
        foreach (string name in new[] { "SettingsPageController.Network.cs", "SettingsPageController.NetworkDiagnostics.cs", "TaskCenterController.cs", "DesktopTaskBubblePresenter.cs", "DesktopDynamicLayer.cs", "ProcessLogPageController.cs", "LaunchPageController.Process.cs", "SettingsPageController.Catalog.cs", "SettingsPageController.Editors.cs", "SettingsPageController.Hooks.cs", "SettingsPageController.Navigation.cs", "SettingsPageController.Java.cs", "SettingsPageController.About.cs", "SettingsPageController.DiagnosticExport.cs", "SettingsPageController.GeneralRuntime.cs", "SettingsPageController.Storage.cs", "SettingsPageController.StorageLocations.cs", "SettingsPageController.Transfer.cs", "SettingsPageController.Logs.cs", "SettingsPageController.ContentWorkspace.cs", "SettingsPageController.DiagnosticAi.cs", "SettingsPageController.Diagnostics.cs", "SettingsPageController.Platform.cs", "SettingsPageController.HardwareAdvice.cs", "SettingsPageController.RuntimeDiagnostics.cs", "SettingsPageController.RuntimeTrace.cs", "SettingsPageController.AdvancedWorkspace.cs", "SettingsPageController.InstanceDiagnostics.cs", "SettingsPageController.InstanceDiagnostics.History.cs", "SettingsPageController.InstanceDiagnostics.Operations.cs", "SettingsPageController.AdvancedLaunchDiagnostics.cs", "SettingsPageController.InstanceTools.cs", "SettingsPageController.DevelopmentMode.cs", "SettingsPageController.OfflineReadiness.cs", "SettingsPageController.RecoveryTimeline.cs", "SettingsPageController.FileWorkspace.cs", "SettingsPageController.FileWorkspace.Projection.cs", "DesktopCommandPalette.cs", "DesktopFeedbackPresenter.cs", "VersionSelectionController.cs", "VersionSelectionController.Window.cs", "VersionSelectionController.Projection.cs", "AccountFormController.cs", "FirstRunController.cs", "CustomAppearanceSession.cs", "WardrobePageController.cs", "WardrobeLibraryPageController.cs" })
        {
            string source = File.ReadAllText(Path.Combine(repositoryRoot, "Nexa.Desktop", "Ui", name));
            foreach (string token in new[] { ".Tree.Create(", "new XsrUiText(", "new XsrUiVisualStyle", "new XsrUiStackPanel", "FormGroup(", "ActionButton(" })
                if (source.Contains(token, StringComparison.Ordinal)) failures.Add($"{name}: presentation must come from dynamic NXML.");
        }
        foreach (string name in new[] { "SettingsPageController.RuntimeDiagnostics.cs", "SettingsPageController.RuntimeTrace.cs", "SettingsPageController.AdvancedWorkspace.cs", "SettingsPageController.InstanceDiagnostics.cs", "SettingsPageController.InstanceDiagnostics.History.cs", "SettingsPageController.InstanceDiagnostics.Operations.cs", "SettingsPageController.AdvancedLaunchDiagnostics.cs", "SettingsPageController.InstanceTools.cs", "SettingsPageController.DevelopmentMode.cs", "SettingsPageController.OfflineReadiness.cs", "SettingsPageController.RecoveryTimeline.cs", "SettingsPageController.FileWorkspace.cs", "SettingsPageController.FileWorkspace.Projection.cs" })
        {
            string source = File.ReadAllText(Path.Combine(repositoryRoot, "Nexa.Desktop", "Ui", name));
            if (System.Text.RegularExpressions.Regex.IsMatch(source, @"\b(?:Stack|Text|Style|SettingsCard|ManagementFactIn|DiagnosticText|ManagementButton)\s*\("))
                failures.Add($"{name}: legacy helper factories cannot hide imperative presentation.");
        }
        foreach (string name in new[] { "TaskCenterController.cs", "DesktopTaskBubblePresenter.cs", "ProcessLogPageController.cs", "LaunchPageController.Process.cs", "DesktopCommandPalette.cs", "DesktopFeedbackPresenter.cs", "DesktopOverlayText.cs", "VersionSelectionController.cs", "VersionSelectionController.Window.cs", "VersionSelectionController.Projection.cs", "DesktopVersionText.cs", "AccountFormController.cs", "FirstRunController.cs", "CustomAppearanceSession.cs", "WardrobePageController.cs", "WardrobeLibraryPageController.cs", "DesktopBootstrapText.cs", "DesktopWardrobeText.cs" })
        {
            string source = File.ReadAllText(Path.Combine(repositoryRoot, "Nexa.Desktop", "Ui", name));
            if (System.Text.RegularExpressions.Regex.IsMatch(source, "[\\u4e00-\\u9fff]"))
                failures.Add($"{name}: fixed operation copy and formats must be external.");
        }
        foreach (string name in new[] { "DesktopCommandPalette.cs", "DesktopFeedbackPresenter.cs" })
        {
            string source = File.ReadAllText(Path.Combine(repositoryRoot, "Nexa.Desktop", "Ui", name));
            if (source.Contains(".SetComponent", StringComparison.Ordinal) || source.Contains("new NxmlIrNode", StringComparison.Ordinal)
                || source.Contains("new NxmlHostIr", StringComparison.Ordinal))
                failures.Add($"{name}: presentation primitives and IR construction belong to external templates.");
        }
        foreach(string name in new[]{"VersionSelectionController.cs","VersionSelectionController.Window.cs","VersionSelectionController.Projection.cs"})
        {
            string source=File.ReadAllText(Path.Combine(repositoryRoot,"Nexa.Desktop","Ui",name));
            foreach(string token in new[]{"NxmlUiLoader.Load(","new NxmlIrNode","new NxmlHostIr",".Background =",".Border =",".Height =",".Width =",".Margin =",".Content =","MarkSelected(","CreateRow("})
                if(source.Contains(token,StringComparison.Ordinal))failures.Add($"{name}: version presentation must use owned templates.");
        }
        foreach(string name in new[]{"AccountFormController.cs","FirstRunController.cs","CustomAppearanceSession.cs","WardrobePageController.cs","WardrobeLibraryPageController.cs"})
        {
            string source=File.ReadAllText(Path.Combine(repositoryRoot,"Nexa.Desktop","Ui",name));
            foreach(string token in new[]{"NxmlUiLoader.Load(","new NxmlIrNode","new NxmlHostIr","new XsrUiElement",".Background =",".Height =",".Width =",".Margin =",".Content =","Style(","GetComponent<XsrUiVisualStyle>"})
                if(source.Contains(token,StringComparison.Ordinal))failures.Add($"{name}: bootstrap presentation must use owned templates.");
        }
        string textProjection = File.ReadAllText(Path.Combine(repositoryRoot, "Nexa.Desktop", "Ui", "DesktopPresentationText.cs"));
        if (textProjection.Contains(".Tree.Create(", StringComparison.Ordinal) || textProjection.Contains("new NxmlNode", StringComparison.Ordinal))
            failures.Add("External text projection must not hide an imperative UI factory.");
        XDocument compiler = XDocument.Load(projectPaths["Nexa.Xsr.Renderer.Nxml.Compiler"]);
        if (Elements(compiler, "ProjectReference").Any(item => item.Attribute("OutputItemType")?.Value == "Analyzer"))
            failures.Add("NXML components must not be generated into the executable.");
        if (!Elements(compiler, "Content").Any(item => item.Attribute("Include")?.Value.EndsWith("*.c.nxml", StringComparison.Ordinal) == true
            && item.Attribute("CopyToPublishDirectory")?.Value == "PreserveNewest"))
            failures.Add("External components must accompany published applications.");
        XDocument desktop = XDocument.Load(projectPaths["Nexa.Desktop"]);
        if (Elements(desktop, "EmbeddedResource").Any(item => item.Attribute("Include")?.Value.EndsWith(".nxml", StringComparison.Ordinal) == true))
            failures.Add("Desktop NXML pages must be external files, not embedded resources.");
        if (!Elements(desktop, "Content").Any(item => item.Attribute("Include")?.Value.Contains("*.nss", StringComparison.Ordinal) == true
            && item.Attribute("CopyToPublishDirectory")?.Value == "PreserveNewest"))
            failures.Add("Desktop NSS styles must accompany published applications.");
        if (Elements(desktop, "EmbeddedResource").Any(item => item.Attribute("Include")?.Value == "Ui/Localization/messages.json"))
            failures.Add("Static localization must accompany the external presentation bundle.");
        if (!Elements(desktop, "Content").Any(item => item.Attribute("Include")?.Value.Contains("Ui/Localization/messages.json", StringComparison.Ordinal) == true
            && item.Attribute("CopyToOutputDirectory")?.Value == "PreserveNewest"
            && item.Attribute("CopyToPublishDirectory")?.Value == "PreserveNewest"))
            failures.Add("External localization must be copied to build and publish outputs.");
        foreach (string project in new[] { "Nexa.Xsr.Renderer.Nxml.Compiler", "Nexa.Xsr.Renderer.Nxml.Runtime" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(repositoryRoot, project), "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(Path.Combine(repositoryRoot, project), file).Replace('\\', '/');
                if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal)) continue;
                string source = File.ReadAllText(file);
                foreach (string token in new[] { "System.Reflection", "Activator.", "BindingFlags", "MethodInfo", "PropertyInfo", "Type.GetType(" })
                    if (source.Contains(token, StringComparison.Ordinal)) failures.Add($"{project}/{relative}: reflection is not an NXML primitive.");
                if (project.EndsWith(".Runtime", StringComparison.Ordinal)
                    && (source.Contains("File.", StringComparison.Ordinal) || source.Contains("Directory.", StringComparison.Ordinal)))
                    failures.Add("The tree loader must use validated IR, not perform file I/O.");
            }
        }
    }

    private static void ValidateWave3Ci(string repositoryRoot, List<string> failures)
    {
        string workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "xsr-ci.yml");
        if (!File.Exists(workflowPath))
        {
            failures.Add("The XSR CI workflow is missing.");
            return;
        }

        string workflow = File.ReadAllText(workflowPath);
        string[] requiredFragments =
        [
            "dotnet run --project tests/Nexa.Xsr.Renderer.Nxml.Tests/Nexa.Xsr.Renderer.Nxml.Tests.csproj",
            "dotnet publish tests/Nexa.Xsr.Renderer.Nxml.Tests/Nexa.Xsr.Renderer.Nxml.Tests.csproj",
            "-p:PublishAot=true",
            "pxml-aot/Nexa.Xsr.Renderer.Nxml.Tests",
            "pcl-desktop-aot/Nexa.Desktop\" --validate-shell",
            "pcl-desktop-trimmed/Nexa.Desktop\" --validate-shell",
            "-p:PublishTrimmed=true",
            "-p:TrimMode=link",
            "Validate external NXML component bundle",
            "dotnet format NexaCL.slnx --no-restore --verify-no-changes --diagnostics IDE0055 IMPORTS",
        ];
        foreach (string requiredFragment in requiredFragments)
        {
            if (!workflow.Contains(requiredFragment, StringComparison.Ordinal))
            {
                failures.Add($"The XSR CI workflow is missing the Wave 3 gate fragment '{requiredFragment}'.");
            }
        }
    }

    private static void Visit(
        string project,
        HashSet<string> visiting,
        HashSet<string> visited,
        List<string> failures)
    {
        if (visited.Contains(project))
        {
            return;
        }

        if (!visiting.Add(project))
        {
            failures.Add($"The actual project graph contains a cycle at '{project}'.");
            return;
        }

        foreach (string dependency in ActualReferences[project])
        {
            if (ActualReferences.ContainsKey(dependency))
            {
                Visit(dependency, visiting, visited, failures);
            }
        }

        visiting.Remove(project);
        visited.Add(project);
    }

    private static IEnumerable<XElement> Elements(XDocument document, string localName) =>
        document.Descendants().Where(element => element.Name.LocalName == localName);

    private static string? Property(XDocument document, string localName) =>
        Elements(document, localName).Select(element => element.Value.Trim()).LastOrDefault();

    private static bool IsInsideRepository(string repositoryRoot, string path)
    {
        string relative = Path.GetRelativePath(repositoryRoot, path);
        return !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }
}
