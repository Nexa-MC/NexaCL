using Nexa.Services.Minecraft.Java;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask InstanceJavaSelectionStaysLocalAndChecksCompatibility()
    {
        string root = CreateTempDirectory();
        try
        {
            string instance = Path.Combine(root, "versions", "local"), other = Path.Combine(root, "versions", "other");
            JavaRuntimeCandidate Runtime(string home, int major)
            {
                string executable = Path.Combine(home, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
                Directory.CreateDirectory(Path.GetDirectoryName(executable)!); File.WriteAllText(executable, "fixture");
                return new(new(home, executable, null, new Version(major, 0), JavaBrand.EclipseTemurin, JavaArchitecture.X64, true, false));
            }
            var incompatible = Runtime(Path.Combine(instance, "jre"), 8);
            var compatible = Runtime(Path.Combine(instance, "runtime", "java-runtime-gamma", "linux", "jdk-17"), 17);
            var foreign = Runtime(Path.Combine(other, "java"), 17);
            var global = Runtime(Path.Combine(root, "global"), 17);
            var locator = new InstanceOnlyJavaInspector([incompatible, compatible, foreign, global]);
            var selection = new JavaSelectionService(locator);
            var requirement = new MinecraftJavaRequirementRequest { HasReliableVanillaVersion = true, VanillaVersion = new Version(1, 20, 1) };
            var result = await selection.SelectAsync(requirement, new InstanceJavaPreference(instance));
            AssertTrue(result.Success); AssertEqual(compatible.Installation.JavaExecutablePath, result.SelectedJava!.Installation.JavaExecutablePath);
            AssertEqual(0, locator.GlobalReads);
            File.Delete(compatible.Installation.JavaExecutablePath);
            result = await selection.SelectAsync(requirement, new InstanceJavaPreference(instance));
            AssertFalse(result.Success); AssertEqual(JavaSelectionFailureReason.NoCompatibleRuntime, result.FailureReason);
            AssertEqual(0, locator.GlobalReads);
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(Path.Combine(instance, "java"), Path.Combine(other, "java"));
                result = await selection.SelectAsync(requirement, new InstanceJavaPreference(instance));
                AssertFalse(result.Success); AssertEqual(0, locator.GlobalReads);
            }
            using var stop = new CancellationTokenSource(); stop.Cancel();
            await TrustRejectsAsync<OperationCanceledException>(async () => await selection.SelectAsync(requirement, new InstanceJavaPreference(instance), stop.Token));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class InstanceOnlyJavaInspector(IReadOnlyList<JavaRuntimeCandidate> candidates) : IJavaRuntimeLocator
    {
        internal int GlobalReads;
        public ValueTask<IReadOnlyList<JavaRuntimeCandidate>> FindAllAsync(CancellationToken cancellationToken = default)
        { GlobalReads++; return ValueTask.FromResult(candidates); }
        public ValueTask<JavaRuntimeCandidate?> InspectAsync(string javaExecutablePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(candidates.FirstOrDefault(candidate => Nexa.Core.PathIdentity.Comparer.Equals(candidate.Installation.JavaExecutablePath, javaExecutablePath)));
        }
    }
}
