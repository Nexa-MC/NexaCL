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

    internal static async ValueTask ModernJvmTokensAllResolve()
    {
        string directory = CreateTempDirectory();
        try
        {
            string instance = Path.Combine(directory, "instance");
            Directory.CreateDirectory(instance);
            string natives = Path.Combine(instance, "natives");
            Directory.CreateDirectory(natives);
            string clientJar = Path.Combine(instance, "1.20.1.jar");
            File.WriteAllBytes(clientJar, [0xCA, 0xFE]);

            JsonObject manifest = new()
            {
                ["mainClass"] = "net.minecraft.client.main.Main",
                ["arguments"] = new JsonObject
                {
                    ["jvm"] = new JsonArray(
                        "-Djava.library.path=${natives_directory}",
                        "-Djna.nosys=true",
                        "-Dlauncher=${launcher_name}/${launcher_version}",
                        "-Dlibrary=${library_directory}",
                        "${classpath_separator}"),
                    ["game"] = new JsonArray(
                        "--username ${auth_player_name}",
                        "--gameDir ${game_directory}",
                        "--assetsDir ${assets_root}",
                        "--assetIndex ${assets_index_name}",
                        "--uuid ${auth_uuid}",
                        "--accessToken ${auth_access_token}",
                        "--userType ${user_type}",
                        "--versionType ${version_type}",
                        "--width ${resolution_width}",
                        "--height ${resolution_height}"),
                },
            };
            MinecraftLaunchPlan plan = MinecraftLaunchPlanner.CreatePlan(new MinecraftLaunchRequest
            {
                VersionJson = manifest,
                VersionId = "1.20.1",
                InstanceDirectory = instance,
                MinecraftRootDirectory = directory,
                PlayerName = "Steve",
                PlayerUuid = "uuid-1",
                NativesDirectory = natives,
                IsolatedGameDirectory = true,
            });

            foreach (string argument in plan.Arguments)
            {
                AssertFalse(argument.Contains("${", StringComparison.Ordinal));
                AssertFalse(argument.Contains("Nexa-UNRESOLVED-TOKEN", StringComparison.Ordinal));
            }

            AssertTrue(plan.Arguments.Contains("-Djava.library.path=" + natives));
            AssertTrue(plan.Arguments.Contains("-Dlauncher=NexaCL/2.0.0"));
            AssertTrue(plan.Arguments.Any(argument => argument.StartsWith("-Dlibrary=", StringComparison.Ordinal)));
            AssertTrue(plan.Arguments.Contains("--gameDir " + instance, StringComparer.Ordinal));

            // The derived client JAR is automatically the classpath head.
            int cpIndex = plan.Arguments.ToList().IndexOf("-cp");
            AssertTrue(plan.Arguments[cpIndex + 1].StartsWith(clientJar, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        await Task.CompletedTask;
    }

    internal static async ValueTask UnresolvedLaunchTokenFailsPlanning()
    {
        string directory = CreateTempDirectory();
        try
        {
            string instance = Path.Combine(directory, "instance");
            Directory.CreateDirectory(instance);
            File.WriteAllBytes(Path.Combine(instance, "unknown-1.0.jar"), [0x01]);
            JsonObject manifest = new()
            {
                ["mainClass"] = "net.minecraft.client.main.Main",
                ["arguments"] = new JsonObject
                {
                    ["game"] = new JsonArray("--futureToken ${some_future_token}"),
                },
            };

            bool failed = false;
            try
            {
                _ = MinecraftLaunchPlanner.CreatePlan(new MinecraftLaunchRequest
                {
                    VersionJson = manifest,
                    VersionId = "unknown-1.0",
                    InstanceDirectory = instance,
                    MinecraftRootDirectory = directory,
                    PlayerName = "Steve",
                    PlayerUuid = "uuid-1",
                    ClientJarPath = Path.Combine(instance, "unknown-1.0.jar"),
                });
            }
            catch (InvalidDataException failure)
            {
                failed = failure.Message.Contains("some_future_token", StringComparison.Ordinal);
            }

            AssertTrue(failed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        await Task.CompletedTask;
    }

    internal static async ValueTask UnknownJvmTokenFailsPlanning()
    {
        string directory = CreateTempDirectory();
        try
        {
            string instance = Path.Combine(directory, "instance");
            Directory.CreateDirectory(instance);
            string client = Path.Combine(instance, "unknown-jvm.jar");
            File.WriteAllBytes(client, [0x01]);
            bool failed = false;
            try
            {
                _ = MinecraftLaunchPlanner.CreatePlan(new MinecraftLaunchRequest
                {
                    VersionJson = new JsonObject
                    {
                        ["mainClass"] = "net.minecraft.client.main.Main",
                        ["arguments"] = new JsonObject { ["jvm"] = new JsonArray("-Dfuture=${future_jvm_token}") },
                    },
                    VersionId = "unknown-jvm",
                    InstanceDirectory = instance,
                    MinecraftRootDirectory = directory,
                    PlayerName = "Steve",
                    PlayerUuid = "uuid-1",
                    ClientJarPath = client,
                });
            }
            catch (InvalidDataException failure)
            {
                failed = failure.Message.Contains("future_jvm_token", StringComparison.Ordinal);
            }

            AssertTrue(failed);
        }
        finally { Directory.Delete(directory, recursive: true); }

        await Task.CompletedTask;
    }

    internal static async ValueTask VanillaLaunchContainsClientJar()
    {
        string directory = CreateTempDirectory();
        try
        {
            string instance = Path.Combine(directory, "instance");
            Directory.CreateDirectory(instance);
            byte[] clientJar = [0x50, 0x4B, 0x03, 0x04];
            string clientJarPath = Path.Combine(instance, "1.20.1.jar");
            File.WriteAllBytes(clientJarPath, clientJar);

            JsonObject manifest = new()
            {
                ["mainClass"] = "net.minecraft.client.main.Main",
                ["arguments"] = new JsonObject { ["game"] = new JsonArray("--username ${auth_player_name}") },
                ["libraries"] = new JsonArray(),
            };
            MinecraftLaunchPlan plan = MinecraftLaunchPlanner.CreatePlan(new MinecraftLaunchRequest
            {
                VersionJson = manifest,
                VersionId = "1.20.1",
                InstanceDirectory = instance,
                MinecraftRootDirectory = directory,
                PlayerName = "Steve",
                PlayerUuid = "uuid-1",
            });

            int cpIndex = plan.Arguments.ToList().IndexOf("-cp");
            AssertTrue(cpIndex >= 0);
            string classpath = plan.Arguments[cpIndex + 1];
            AssertTrue(classpath.Split(Path.PathSeparator).First(static entry => entry.EndsWith("1.20.1.jar", StringComparison.Ordinal))
                == clientJarPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        await Task.CompletedTask;
    }

    internal static async ValueTask InheritedLaunchUsesBaseClientJarWhenProvided()
    {
        string directory = CreateTempDirectory();
        try
        {
            string instance = Path.Combine(directory, "instance");
            Directory.CreateDirectory(instance);
            string baseJar = Path.Combine(directory, "versions", "1.20.1", "1.20.1.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(baseJar)!);
            File.WriteAllBytes(baseJar, [0x01]);

            JsonObject inherited = new()
            {
                ["mainClass"] = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                ["inheritsFrom"] = "1.20.1",
                ["arguments"] = new JsonObject { ["game"] = new JsonArray("--username ${auth_player_name}") },
                ["libraries"] = new JsonArray(),
            };
            MinecraftLaunchPlan plan = MinecraftLaunchPlanner.CreatePlan(new MinecraftLaunchRequest
            {
                VersionJson = inherited,
                VersionId = "fabric-1.20.1",
                InstanceDirectory = instance,
                MinecraftRootDirectory = directory,
                PlayerName = "Steve",
                PlayerUuid = "uuid-1",
                ClientJarPath = baseJar,
            });

            int cpIndex = plan.Arguments.ToList().IndexOf("-cp");
            AssertTrue(plan.Arguments[cpIndex + 1].StartsWith(baseJar, StringComparison.Ordinal));

            // Legacy loader manifests use a `jar` alias instead of inheritsFrom. The alias must
            // win even when the loader has its own auxiliary JAR in the instance directory.
            string loaderJar = Path.Combine(instance, "fabric-loader-alias.jar");
            File.WriteAllBytes(loaderJar, [0x02]);
            MinecraftLaunchPlan aliasPlan = MinecraftLaunchPlanner.CreatePlan(new MinecraftLaunchRequest
            {
                VersionJson = new JsonObject
                {
                    ["id"] = "fabric-loader-alias",
                    ["jar"] = "1.20.1",
                    ["mainClass"] = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                },
                VersionId = "fabric-loader-alias",
                InstanceDirectory = instance,
                MinecraftRootDirectory = directory,
                PlayerName = "Steve",
                PlayerUuid = "uuid-1",
            });
            AssertTrue(aliasPlan.IsInheritedClientJar);
            AssertEqual(Path.GetFullPath(baseJar), aliasPlan.ClientJarPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        await Task.CompletedTask;
    }

    internal static async ValueTask InheritedLaunchResolvesBaseClientJarAutomatically()
    {
        string directory = CreateTempDirectory();
        try
        {
            string instance = Path.Combine(directory, "instance");
            string baseDirectory = Path.Combine(directory, "versions", "1.20.1");
            Directory.CreateDirectory(instance);
            Directory.CreateDirectory(baseDirectory);
            string baseJar = Path.Combine(baseDirectory, "1.20.1.jar");
            File.WriteAllBytes(baseJar, [0x01]);

            MinecraftLaunchPlan plan = MinecraftLaunchPlanner.CreatePlan(new MinecraftLaunchRequest
            {
                VersionJson = new JsonObject
                {
                    ["id"] = "fabric-loader-0.16.5-1.20.1",
                    ["inheritsFrom"] = "1.20.1",
                    ["mainClass"] = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                    ["libraries"] = new JsonArray(),
                },
                VersionId = "fabric-loader-0.16.5-1.20.1",
                InstanceDirectory = instance,
                MinecraftRootDirectory = directory,
                PlayerName = "Steve",
                PlayerUuid = "uuid-1",
            });

            AssertTrue(plan.IsInheritedClientJar);
            AssertEqual(Path.GetFullPath(baseJar), plan.ClientJarPath);
            AssertEqual(Path.GetFullPath(baseJar), plan.ClasspathEntries[0]);
        }
        finally { Directory.Delete(directory, recursive: true); }

        await Task.CompletedTask;
    }
}
