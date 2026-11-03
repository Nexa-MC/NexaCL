using System.Diagnostics;
using Nexa.Native.Minecraft.Client;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.ModLoaders;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr.State;

if (args is [MinecraftLaunchHookWorker.WorkerArgument]) return await MinecraftLaunchHookWorker.RunWorkerAsync();
if (args.Contains("-version", StringComparer.Ordinal))
{
    string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "amd64";
    Console.Error.WriteLine("java.version = 17.0.10\nos.arch = " + arch + "\njava.vendor = Eclipse Adoptium");
    return 0;
}
if (args is ["--jvm-host"])
{
    using var input = new MemoryStream(); await Console.OpenStandardInput().CopyToAsync(input);
    if (input.Length is < 4 or > 1024 * 1024) return 90;
    Console.WriteLine("Native Minecraft fixture access_token=fixture-private-value");
    Console.Error.WriteLine("Native Minecraft fixture stderr");
    if (Environment.GetEnvironmentVariable("NEXA_NATIVE_PROCESS_MODE") == "hold") await Task.Delay(Timeout.InfiniteTimeSpan);
    if (Environment.GetEnvironmentVariable("NEXA_NATIVE_PROCESS_MODE") == "delayed-exit") await Task.Delay(3000);
    return 0;
}

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var token = deadline.Token;
await WorkLeaseCases.RunAsync(token);
if (args is ["--work-contracts"]) return 0;
foreach (Version? version in new Version?[] { null, new(8, 0), new(17, 0, 12), new(21, 0, 5, 9) })
{
    using var writer = new ServiceWireWriter(); writer.Version(version);
    using var reader = new ServiceWireReader(writer.Finish());
    if (!Equals(version, reader.Version())) throw new InvalidDataException("Native version components were not preserved."); reader.End();
}
foreach (var components in new[] { new[] { 21, 0, -1, 1 }, new[] { -1, 0, -1, -1 }, new[] { 21, -1, 0, -1 }, new[] { 21, 0, -2, -1 } })
{
    using var writer = new ServiceWireWriter(); writer.Boolean(true);
    foreach (int component in components) writer.Int32(component);
    using var reader = new ServiceWireReader(writer.Finish()); bool rejected = false;
    try { _ = reader.Version(); } catch (InvalidDataException) { rejected = true; }
    if (!rejected) throw new InvalidDataException("Malformed optional version components were accepted.");
}
string temporary = Path.GetPathRoot(Path.GetTempPath())!;
foreach (string segment in Path.GetRelativePath(temporary, Path.GetTempPath()).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
{
    var directory = new DirectoryInfo(Path.Combine(temporary, segment));
    temporary = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
}
string root = Path.Combine(temporary, "nexa-native-minecraft-实例-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string instance = Path.Combine(root, "versions", "1.20.1"); Directory.CreateDirectory(instance);
    await File.WriteAllTextAsync(Path.Combine(instance, "1.20.1.json"),
        """{"id":"1.20.1","type":"release","mainClass":"net.minecraft.client.main.Main","releaseTime":"2023-06-12T13:25:51Z"}""", token);
    await File.WriteAllBytesAsync(Path.Combine(instance, "1.20.1.jar"), [0], token);
    XsrStateStoreBuilder builder = new(); NativeMinecraftClient.DeclareProcessState(builder); NativeMinecraftClient.DeclareLaunchState(builder);
    Nexa.Services.Minecraft.MinecraftLibraryContract.DeclareState(builder); builder.Replica(Nexa.Services.Minecraft.MinecraftLibraryContract.StateKey);
    Nexa.Services.Minecraft.Install.InstallCatalogStateContract.DeclareState(builder); Nexa.Services.Minecraft.Install.MinecraftInstallDraftContract.DeclareState(builder); NativeMinecraftClient.DeclareCatalogReplicas(builder); var store = builder.Build();
    string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing native caller executable.");
    string javaExecutable = executable;
    if (OperatingSystem.IsWindows())
    {
        string bin = Path.Combine(root, "fixture-jdk", "bin"); Directory.CreateDirectory(Path.Combine(bin, "server"));
        javaExecutable = Path.Combine(bin, "java.exe"); File.Copy(executable, javaExecutable);
        File.Copy(executable, Path.Combine(bin, "server", "jvm.dll"));
    }
    await using var minecraft = new NativeMinecraftClient(Path.Combine(root, "runtime"), Path.Combine(root, "cache"), executable, store);
    var versions = await minecraft.VersionsAsync(root, token);
    Check(versions.Count == 1 && versions[0].Id == "1.20.1" && versions[0].JarPath is not null, "Native version discovery lost manifest metadata.");
    var instances = await minecraft.InstancesAsync(root, token: token);
    Check(instances.Count == 1 && instances[0].DirectoryPath == instance && instances[0].VersionId == "1.20.1", "Native instance discovery changed identity.");
    Check((await minecraft.InstancesAsync(root, refresh: true, token)).Count == 1, "Native instance refresh lost an installation.");
    _ = await minecraft.JavaInventoryAsync(token: token);
    var report = await minecraft.AnalyzeCrashAsync(["java.lang.OutOfMemoryError: Java heap space"], token: token);
    Check(report.AllowedActions.Count > 0 && report.Evidence.Count > 0, "Native crash analysis lost its repair evidence.");

    await FeatureParityCases.RunAsync(minecraft, root, instance, executable, token);

    MinecraftLaunchPlan Plan(string mode) => new(javaExecutable, instance, ["fixture.Main", "--accessToken", "private-argument"], [], [],
        new(MinecraftModLoaderKind.Vanilla, null, null, []))
    { MainClassIndex = 0, EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal) { ["NEXA_NATIVE_PROCESS_MODE"] = mode } };
    bool rejected = false;
    try { await minecraft.StartProcessAsync(Plan("exit") with { MainClassIndex = null }, "rejected-fixture", token); }
    catch (ArgumentException error) when (error.ParamName == "plan") { rejected = true; }
    Check(rejected && (await minecraft.ProcessesAsync(token)).Count == 0, "Native argument admission lost its local failure category or leaked a process.");
    var started = await minecraft.StartProcessAsync(Plan("exit"), "1.20.1", token);
    while ((await minecraft.ProcessesAsync(token)).Single(item => item.SessionId == started.SessionId).State is MinecraftProcessState.Created or MinecraftProcessState.Running)
        await Task.Delay(20, token);
    var output = await minecraft.ReadOutputAsync(started.SessionId, token);
    Check(output.IsSuccess && output.Value!.Entries.Any(entry => entry.Stream == MinecraftProcessOutputChannel.Stderr)
        && output.Value.Entries.Any(entry => entry.Text.Contains("<redacted>", StringComparison.Ordinal))
        && output.Value.Entries.All(entry => !entry.Text.Contains("fixture-private-value", StringComparison.Ordinal)), "Native output lost channels or leaked a credential.");
    var state = store.ReadCollection<MinecraftProcessSnapshot>(store.Resolve(MinecraftProcessStateComposition.SessionsKey), token);
    Check(state.Revision > 0 && state.Items.Any(item => item.SessionId == started.SessionId && item.State == MinecraftProcessState.Exited),
        "Native process state did not preserve its source revision and terminal observation.");

    var canceled = await minecraft.StartProcessAsync(Plan("hold"), "cancel-fixture", token);
    Check((await minecraft.CancelProcessAsync(canceled.SessionId, token)).IsSuccess, "Native process cancellation was rejected.");
    await WaitForExit(canceled.ProcessId, token);
    var shutdown = await minecraft.StartProcessAsync(Plan("hold"), "shutdown-fixture", token);
    await JavaInstallCases.RunAsync(minecraft, root, store, javaExecutable, token);
    await minecraft.DisposeAsync(); await WaitForExit(shutdown.ProcessId, token);
    Console.WriteLine("Independent native Minecraft discovery, Java inventory, process state, redacted output, cancellation and physical shutdown passed.");
    return 0;
}
finally { Directory.Delete(root, recursive: true); }

static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
static async Task WaitForExit(int pid, CancellationToken token)
{
    while (true)
    {
        try { using var process = Process.GetProcessById(pid); if (process.HasExited) return; }
        catch (ArgumentException) { return; }
        await Task.Delay(20, token);
    }
}
