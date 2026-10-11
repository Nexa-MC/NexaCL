using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Libraries;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{

    internal static async ValueTask Arm64NativeIsNotOnClasspath()
    {
        string directory = CreateTempDirectory();
        try
        {
            string minecraftRoot = Path.Combine(directory, "root");
            const string lwjgl3 = "org.lwjgl:lwjgl:3.3.2";
            JsonObject manifest = new()
            {
                ["mainClass"] = "net.minecraft.client.main.Main",
                ["libraries"] = new JsonArray(
                    new JsonObject
                    {
                        ["name"] = lwjgl3,
                        ["natives"] = new JsonObject { ["linux"] = "natives-linux" },
                        ["downloads"] = new JsonObject
                        {
                            ["artifact"] = new JsonObject { ["path"] = "org/lwjgl/lwjgl/3.3.2/lwjgl-3.3.2.jar", ["sha1"] = "aa", ["size"] = 2 },
                            ["classifiers"] = new JsonObject
                            {
                                ["natives-linux"] = new JsonObject { ["path"] = "org/lwjgl/lwjgl/3.3.2/lwjgl-3.3.2-natives-linux.jar", ["sha1"] = "bb", ["size"] = 3 },
                            },
                        },
                    },
                    new JsonObject
                    {
                        // Some third-party manifests put the native classifier in the coordinate
                        // instead of using the separate `natives` map.
                        ["name"] = "org.lwjgl:lwjgl:3.2.2:natives-linux",
                    }),
            };

            IReadOnlyList<MinecraftLibraryToken> libraries = MinecraftLibraryResolver.Resolve(new MinecraftLibraryResolutionRequest
            {
                VersionJson = manifest,
                MinecraftRootDirectory = minecraftRoot,
                OperatingSystem = MinecraftLibraryOperatingSystem.Linux,
                Is64BitArchitecture = true,
                IsArm64Architecture = true,
            });

            MinecraftLibraryToken? arm64 = libraries.FirstOrDefault(static token => token.OriginalName is not null && token.OriginalName.Contains("natives-linux-arm64", StringComparison.Ordinal));
            AssertTrue(arm64 is not null);
            MinecraftLibraryToken arm64Token = arm64!;
            AssertTrue(arm64Token.IsNatives);
            AssertTrue(libraries.Any(static token => token.OriginalName == "org.lwjgl:lwjgl:3.3.2:natives-linux-arm64" && token.IsNatives));

            MinecraftLibraryToken artifact = libraries.Single(static token => !token.IsNatives && token.LocalPath.EndsWith("lwjgl-3.3.2.jar", StringComparison.Ordinal));

            // Classpath planner excludes natives.
            MinecraftClasspathPlan classpath = MinecraftClasspathPlanner.CreatePlan(new MinecraftClasspathPlanRequest
            {
                Libraries = libraries,
                HasCleanroom = false,
            });
            AssertFalse(classpath.Entries.Any(static entry => entry.Contains("natives", StringComparison.OrdinalIgnoreCase)));
            AssertTrue(classpath.Entries.Contains(artifact.LocalPath, StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        await Task.CompletedTask;
    }

    internal static async ValueTask NativesAreExtractedBeforeLaunch()
    {
        string directory = CreateTempDirectory();
        try
        {
            string jarPath = Path.Combine(directory, "natives.jar");
            using (FileStream stream = File.Create(jarPath))
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry dll = archive.CreateEntry("lwjgl.dll");
                using (var writer = dll.Open()) writer.Write([0x01, 0x02]);
                ZipArchiveEntry so = archive.CreateEntry("liblwjgl.so");
                using (var writer = so.Open()) writer.Write([0x03]);
                ZipArchiveEntry meta = archive.CreateEntry("META-INF/MANIFEST.MF");
                using (var writer = meta.Open()) writer.Write([0xFF, 0xFF]);
                ZipArchiveEntry evil = archive.CreateEntry("../../evil.bin");
                using (var writer = evil.Open()) writer.Write([0xEE]);
            }

            string natives = Path.Combine(directory, "natives");

            // The traversal entry is a hard failure before anything else is staged.
            bool traversalRefused = false;
            try
            {
                await MinecraftNativesExtractor.ExtractAsync([jarPath], natives);
            }
            catch (InvalidDataException failure)
            {
                traversalRefused = failure.Message.Contains("evil.bin", StringComparison.Ordinal);
            }

            AssertTrue(traversalRefused);
            AssertFalse(File.Exists(Path.Combine(directory, "evil.bin")));

            // A clean archive extracts everything except META-INF.
            string cleanJar = Path.Combine(directory, "clean.jar");
            using (FileStream stream = File.Create(cleanJar))
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry dll = archive.CreateEntry("lwjgl.dll");
                using (var writer = dll.Open()) writer.Write([0x01, 0x02]);
                ZipArchiveEntry so = archive.CreateEntry("liblwjgl.so");
                using (var writer = so.Open()) writer.Write([0x03]);
                ZipArchiveEntry meta = archive.CreateEntry("META-INF/MANIFEST.MF");
                using (var writer = meta.Open()) writer.Write([0xFF, 0xFF]);
            }

            await MinecraftNativesExtractor.ExtractAsync([cleanJar], natives);
            AssertTrue(File.Exists(Path.Combine(natives, "lwjgl.dll")));
            AssertTrue(File.Exists(Path.Combine(natives, "liblwjgl.so")));
            AssertFalse(Directory.EnumerateFileSystemEntries(natives, "*", SearchOption.AllDirectories)
                .Any(path => path.Replace('\\', '/').Contains("META-INF", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        await Task.CompletedTask;
    }

    internal static void MojangRuleOrderMatchesManifestSemantics()
    {
        // Ordered rules: the last matching rule decides; no match excludes the value.
        JsonObject manifest = new()
        {
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray(
                    new JsonObject
                    {
                        ["rules"] = new JsonArray(
                            new JsonObject { ["action"] = "allow", ["os"] = new JsonObject { ["name"] = "windows" } },
                            new JsonObject { ["action"] = "disallow", ["os"] = new JsonObject { ["name"] = "windows" } }),
                        ["value"] = "--ordered-disallow",
                    },
                    new JsonObject
                    {
                        ["rules"] = new JsonArray(
                            new JsonObject { ["action"] = "disallow", ["os"] = new JsonObject { ["name"] = "linux" } },
                            new JsonObject { ["action"] = "allow", ["os"] = new JsonObject { ["name"] = "windows" } }),
                        ["value"] = "--ordered-allow",
                    },
                    new JsonObject
                    {
                        ["rules"] = new JsonArray(new JsonObject { ["action"] = "allow", ["os"] = new JsonObject { ["name"] = "linux" } }),
                        ["value"] = "--linux-only",
                    }),
            },
        };

        MinecraftLaunchPlan plan = MinecraftLaunchPlanner.CreatePlan(new MinecraftLaunchRequest
        {
            VersionJson = manifest,
            VersionId = "rules-1",
            InstanceDirectory = Path.Combine(CreateTempDirectory(), "instance"),
            MinecraftRootDirectory = Path.GetTempPath(),
            PlayerName = "Steve",
            PlayerUuid = "uuid",
            OperatingSystem = MinecraftLibraryOperatingSystem.Win32,
            OperatingSystemVersion = "10.0",
            ClientJarPath = Path.GetTempFileName(),
        });

        // Ordered: allow windows then disallow windows → the last match excludes the value.
        AssertFalse(plan.Arguments.Contains("--ordered-disallow", StringComparer.Ordinal));
        // allow-then-allow keeps it, and the non-matching linux-only never appears.
        AssertTrue(plan.Arguments.Contains("--ordered-allow", StringComparer.Ordinal));
        AssertFalse(plan.Arguments.Contains("--linux-only", StringComparer.Ordinal));
    }

    internal static void OsVersionUsesRegex()
    {
        JsonObject manifest = new()
        {
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray(
                    new JsonObject
                    {
                        ["rules"] = new JsonArray(
                            new JsonObject { ["action"] = "allow", ["os"] = new JsonObject { ["name"] = "windows", ["version"] = "^10\\." } }),
                        ["value"] = "--win10",
                    },
                    new JsonObject
                    {
                        ["rules"] = new JsonArray(
                            new JsonObject { ["action"] = "allow", ["os"] = new JsonObject { ["name"] = "windows", ["version"] = "^11\\." } }),
                        ["value"] = "--win11",
                    }),
            },
        };

        MinecraftLaunchPlan plan = MinecraftLaunchPlanner.CreatePlan(new MinecraftLaunchRequest
        {
            VersionJson = manifest,
            VersionId = "rules-2",
            InstanceDirectory = Path.Combine(CreateTempDirectory(), "instance"),
            MinecraftRootDirectory = Path.GetTempPath(),
            PlayerName = "Steve",
            PlayerUuid = "uuid",
            OperatingSystem = MinecraftLibraryOperatingSystem.Win32,
            OperatingSystemVersion = "10.0.22631",
            ClientJarPath = Path.GetTempFileName(),
        });

        AssertTrue(plan.Arguments.Contains("--win10", StringComparer.Ordinal));
        AssertFalse(plan.Arguments.Contains("--win11", StringComparer.Ordinal));
    }

    internal static void AbsentFalseFeatureMatches()
    {
        JsonObject manifest = new()
        {
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray(
                    new JsonObject
                    {
                        ["rules"] = new JsonArray(
                            new JsonObject
                            {
                                ["action"] = "allow",
                                ["features"] = new JsonObject { ["has_custom_resolution"] = false },
                            }),
                        ["value"] = "--no-custom-resolution",
                    }),
            },
        };

        MinecraftLaunchPlan plan = MinecraftLaunchPlanner.CreatePlan(new MinecraftLaunchRequest
        {
            VersionJson = manifest,
            VersionId = "rules-3",
            InstanceDirectory = Path.Combine(CreateTempDirectory(), "instance"),
            MinecraftRootDirectory = Path.GetTempPath(),
            PlayerName = "Steve",
            PlayerUuid = "uuid",
            ClientJarPath = Path.GetTempFileName(),
        });

        AssertTrue(plan.Arguments.Contains("--no-custom-resolution", StringComparer.Ordinal));
    }

    internal static void MinecraftLibraryRulesUseSharedEvaluator()
    {
        string directory = CreateTempDirectory();
        try
        {
            JsonObject manifest = new()
            {
                ["libraries"] = new JsonArray(
                    new JsonObject
                    {
                        ["name"] = "org.example:ordered:1.0",
                        ["rules"] = new JsonArray(
                            new JsonObject { ["action"] = "disallow", ["os"] = new JsonObject { ["name"] = "windows" } },
                            new JsonObject { ["action"] = "allow", ["os"] = new JsonObject { ["name"] = "windows" } }),
                    },
                    new JsonObject
                    {
                        ["name"] = "org.example:regex:1.0",
                        ["rules"] = new JsonArray(new JsonObject { ["action"] = "allow", ["os"] = new JsonObject { ["name"] = "windows", ["version"] = "^10\\." } }),
                    },
                    new JsonObject
                    {
                        ["name"] = "org.example:feature:1.0",
                        ["rules"] = new JsonArray(new JsonObject { ["action"] = "allow", ["features"] = new JsonObject { ["has_custom_resolution"] = false } }),
                    }),
            };

            IReadOnlyList<MinecraftLibraryToken> resolved = MinecraftLibraryResolver.Resolve(new MinecraftLibraryResolutionRequest
            {
                VersionJson = manifest,
                MinecraftRootDirectory = CreateTempDirectory(),
                OperatingSystem = MinecraftLibraryOperatingSystem.Win32,
                OperatingSystemVersion = "10.0.22631",
            });
            AssertTrue(resolved.Any(token => token.OriginalName == "org.example:ordered:1.0"));
            AssertTrue(resolved.Any(token => token.OriginalName == "org.example:regex:1.0"));
            AssertTrue(resolved.Any(token => token.OriginalName == "org.example:feature:1.0"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static async ValueTask MinecraftLaunchRouteStagesNativesBeforeProcessStart()
    {
        string directory = CreateTempDirectory();
        string root = Path.Combine(directory, "root");
        MinecraftProcessService? processes = null;
        try
        {
            string instance = Path.Combine(directory, "instance");
            string natives = Path.Combine(instance, "natives");
            Directory.CreateDirectory(instance);
            string client = Path.Combine(instance, "1.20.1.jar");
            File.WriteAllBytes(client, [0x01]);

            string nativeJar = Path.Combine(root, "libraries", "org", "example", "native", "1.0", "native-1.0-natives-linux.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(nativeJar)!);
            using (FileStream stream = File.Create(nativeJar))
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("libnative.so");
                using Stream output = entry.Open();
                output.Write([0x42]);
            }

            JsonObject manifest = new()
            {
                ["id"] = "1.20.1",
                ["mainClass"] = "net.minecraft.client.main.Main",
                ["arguments"] = new JsonObject
                {
                    ["jvm"] = new JsonArray("-Djava.library.path=${natives_directory}"),
                    ["game"] = new JsonArray("--username", "${auth_player_name}"),
                },
                ["libraries"] = new JsonArray(new JsonObject
                {
                    ["name"] = "org.example:native:1.0",
                    ["natives"] = new JsonObject { ["linux"] = "natives-linux" },
                    ["downloads"] = new JsonObject
                    {
                        ["classifiers"] = new JsonObject
                        {
                            ["natives-linux"] = new JsonObject { ["path"] = "org/example/native/1.0/native-1.0-natives-linux.jar" },
                        },
                    },
                }),
            };

            XsrStateStoreBuilder builder = new();
            MinecraftProcessStateComposition.DeclareState(builder);
            Nexa.Services.Logging.LogService.DeclareState(builder);
            XsrStateStore store = builder.Build();
            Nexa.Services.Logging.LogService log = new(store);
            processes = new MinecraftProcessService(new ExtractionCheckingProcessPort(Path.Combine(natives, "libnative.so")), store, log);
            MinecraftLaunchExecutor executor = new(processes, log);
            XsrCommandHandler<MinecraftLaunchCommand> launch = MinecraftCommands.CreateLaunchHandler(executor);
            XsrResult result = await launch(new MinecraftLaunchCommand(new MinecraftLaunchRequest
            {
                VersionJson = manifest,
                VersionId = "1.20.1",
                InstanceDirectory = instance,
                MinecraftRootDirectory = root,
                PlayerName = "Steve",
                PlayerUuid = "uuid-1",
                OperatingSystem = MinecraftLibraryOperatingSystem.Linux,
                NativesDirectory = natives,
            }), CancellationToken.None);

            AssertTrue(result.IsSuccess);
            AssertTrue(File.Exists(Path.Combine(natives, "libnative.so")));
            MinecraftProcessSnapshot session = processes.ListSessions().Single();
            XsrCollectionSnapshot<MinecraftProcessSnapshot> state = store.ReadCollection<MinecraftProcessSnapshot>(
                store.Resolve(MinecraftProcessStateComposition.SessionsKey));
            AssertTrue(state.Items.Any(item => item.SessionId == session.SessionId));

            XsrResult cancelled = await MinecraftCommands.CreateCancelProcessHandler(processes)(
                new MinecraftCancelProcessCommand(session.SessionId), CancellationToken.None);
            AssertTrue(cancelled.IsSuccess);
            string diagnostic = DiagnosticText(log);
            int extractionIndex = diagnostic.IndexOf("stage=extract_natives", StringComparison.Ordinal);
            int processIndex = diagnostic.IndexOf("stage=os_start", StringComparison.Ordinal);
            AssertTrue(extractionIndex >= 0 && processIndex > extractionIndex);
            AssertTrue(diagnostic.Contains("state=Created", StringComparison.Ordinal));
            AssertTrue(diagnostic.Contains("state=Running", StringComparison.Ordinal));
            AssertTrue(diagnostic.Contains($"Process cancellation requested session={session.SessionId}", StringComparison.Ordinal));
            AssertTrue(diagnostic.Contains("state=Cancelled", StringComparison.Ordinal));
        }
        finally
        {
            if (processes is not null) await processes.DisposeAsync();
            if (Directory.Exists(root)) await WaitForLaunchGameDirectoryReleasedAsync(root);
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static void MinecraftDownloadPathsRejectManifestTraversal()
    {
        bool unsafeClientRejected = false;
        try
        {
            _ = Nexa.Services.Minecraft.Downloads.MinecraftClientDownloadPlanner.CreateClientJarPlan(new Nexa.Services.Minecraft.Downloads.MinecraftClientJarDownloadPlanRequest
            {
                VersionJson = new JsonObject
                {
                    ["downloads"] = new JsonObject
                    {
                        ["client"] = new JsonObject { ["url"] = "https://example.invalid/client.jar" },
                    },
                },
                InstanceDirectory = CreateTempDirectory(),
                VersionName = "../../outside",
            });
        }
        catch (InvalidDataException) { unsafeClientRejected = true; }
        AssertTrue(unsafeClientRejected);

        bool unsafeIndexRejected = false;
        try
        {
            _ = Nexa.Services.Minecraft.Downloads.MinecraftClientDownloadPlanner.CreateAssetIndexPlan(new Nexa.Services.Minecraft.Downloads.MinecraftAssetIndexDownloadPlanRequest
            {
                VersionJson = new JsonObject
                {
                    ["assetIndex"] = new JsonObject
                    {
                        ["id"] = "../../outside",
                        ["url"] = "https://example.invalid/assets.json",
                    },
                },
                MinecraftRootDirectory = CreateTempDirectory(),
            });
        }
        catch (InvalidDataException) { unsafeIndexRejected = true; }
        AssertTrue(unsafeIndexRejected);
    }

    private sealed class ExtractionCheckingProcessPort(string requiredNativePath) : IMinecraftProcessPort
    {
        public bool UsesPrivateArgumentTransport => true;

        public ValueTask<System.Diagnostics.Process> StartAsync(System.Diagnostics.ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(requiredNativePath)) throw new InvalidOperationException("Native extraction did not complete before process start.");
            return ValueTask.FromResult(CreateSleepProcess(2));
        }
    }
}
