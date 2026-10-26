using System.Globalization;
using System.Text.Json.Nodes;
using Nexa.Services.Accounts;
using Nexa.Services.Logging;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Libraries;
using Nexa.Services.Minecraft.ModLoaders;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Scheduling;
using Nexa.Services.Settings;
using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Launch;

public sealed partial class MinecraftLaunchCoordinator
{

    private MinecraftLaunchRequest CreateRequest(
        MinecraftInstanceDescriptor instance,
        MinecraftResolvedVersionManifests manifests,
        MinecraftModLoaderDescriptor loader,
        MinecraftLaunchIdentity identity,
        ResolvedJava java,
        string minecraftRootDirectory, SettingsEffectiveSnapshot? effectiveSettings)
    {
        MinecraftInstanceMetadata metadata = instance.Metadata;
        int width = GetSetting("LaunchArgumentWindowWidth", 854);
        int height = GetSetting("LaunchArgumentWindowHeight", 480);
        string customJvm = FirstNonEmpty(
            metadata.JvmArguments,
            GetSetting("LaunchAdvanceJvm", string.Empty));
        string customGame = FirstNonEmpty(
            metadata.GameArguments,
            GetSetting("LaunchAdvanceGame", string.Empty));
        string versionType = FirstNonEmpty(
            metadata.CustomInfo,
            GetSetting("LaunchArgumentInfo", "NexaN"),
            "NexaCL");
        bool instancePreLaunch = !string.IsNullOrWhiteSpace(metadata.PreLaunchCommand);
        DateTimeOffset? releaseTime = ReadReleaseTime(manifests) ?? instance.Version.ReleaseTime;

        var request = new MinecraftLaunchRequest
        {
            VersionJson = manifests.Current,
            InheritedVersionJsons = manifests.Inherited,
            VersionId = instance.VersionId,
            InstanceDirectory = instance.DirectoryPath,
            MinecraftRootDirectory = minecraftRootDirectory,
            PlayerName = identity.PlayerName,
            PlayerUuid = identity.PlayerUuid,
            AccessToken = identity.AccessToken,
            IdentityMode = identity.Mode,
            JavaExecutablePath = java.ExecutablePath,
            JavaMajorVersion = java.MajorVersion,
            MemoryMegabytes = ResolveMemoryMegabytes(metadata, loader.Kind),
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Fullscreen = GetSetting("LaunchArgumentWindowType", 1) == 0,
            WindowTitle = metadata.UseGlobalWindowTitle ? "" : metadata.WindowTitle,
            IsolatedGameDirectory = metadata.InstanceIsolation,
            CustomJvmArguments = string.IsNullOrWhiteSpace(customJvm) ? null : customJvm,
            CustomGameArguments = string.IsNullOrWhiteSpace(customGame) ? null : customGame,
            WrapperCommand = effectiveSettings is null
                ? FirstNonEmpty(metadata.WrapperCommand, GetSetting("LaunchWrapperCommand", string.Empty)) : metadata.WrapperCommand,
            PreLaunchCommand = effectiveSettings is null
                ? FirstNonEmpty(metadata.PreLaunchCommand, GetSetting("LaunchAdvanceRun", string.Empty)) : metadata.PreLaunchCommand,
            WaitForPreLaunchCommand = instancePreLaunch
                ? metadata.WaitForPreLaunchCommand : effectiveSettings is null ? GetSetting("LaunchAdvanceRunWait", true) : true,
            ClasspathHeadEntries = SplitClasspathHead(metadata.ClasspathHead),
            Server = string.IsNullOrWhiteSpace(metadata.ServerToEnter) ? null : metadata.ServerToEnter,
            ReleaseTime = releaseTime,
            LauncherName = "NexaCL",
            LauncherVersion = _launcherVersion,
            VersionType = versionType,
            UseSystemGlfw = metadata.UseSystemGlfw || GetSetting("LaunchUseSystemGlfw", false),
            HasCleanroom = loader.Kind == MinecraftModLoaderKind.Cleanroom,
            OperatingSystem = _platform.OperatingSystem,
            OperatingSystemVersion = _platform.OperatingSystemVersion,
            Is64BitArchitecture = _platform.Is64BitArchitecture,
            IsArm64Architecture = _platform.IsArm64Architecture,
            Features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["has_custom_resolution"] = true,
                ["is_demo_user"] = false,
            },
        };
        if (Volatile.Read(ref _safeLaunchRequired)) request = request with { Overlay = request.Overlay with { SafeLaunch = true } };
        if (effectiveSettings is not null) request = ApplySettings(request, effectiveSettings, ResolveAutomaticMemoryMegabytes(loader.Kind));
        return MinecraftSafeLaunchPolicy.Apply(request);
    }

    internal static JavaPreference ApplyJavaPreference(JavaPreference fallback, SettingsEffectiveSnapshot snapshot)
    {
        var setting = snapshot.Values.Single(value => value.Key == "java.runtime");
        if (setting.ValidationError is not null) throw new InvalidDataException("首选 Java 设置无效。");
        JavaPreference preference = setting.Source == SettingsLayer.Builtin ? fallback
            : setting.Value.Mode == SettingsOverrideMode.Auto ? new AutoSelectJavaPreference()
            : new ExistingJavaPreference(setting.Value.Value!);
        var vendor = snapshot.Values.Single(item => item.Key == "java.vendor");
        if (preference is AutoSelectJavaPreference auto && vendor.Source != SettingsLayer.Builtin)
        {
            if (vendor.ValidationError is not null) throw new InvalidDataException("Java 发行版设置无效。");
            preference = auto with
            {
                PreferredBrand = Enum.TryParse<JavaBrand>(vendor.Value.Value, out var brand)
                && Enum.IsDefined(brand) ? brand : null
            };
        }
        return preference;
    }

    internal static MinecraftLaunchRequest ApplySettings(MinecraftLaunchRequest request, SettingsEffectiveSnapshot snapshot, int? automaticMemoryMegabytes = null)
    {
        bool safeLaunch = request.Overlay.SafeLaunch || snapshot.Overlay.SafeLaunch
            || snapshot.Values.Any(value => value.Key == "game.safe-launch" && value.Value.Value == "true");
        bool hasLegacyPreLaunch = !string.IsNullOrWhiteSpace(request.PreLaunchCommand);
        foreach (var setting in snapshot.Values)
        {
            if (safeLaunch && setting.Key is "game.jvm" or "game.arguments" or "game.wrapper" or "game.pre-launch" or "game.pre-launch-wait"
                or "game.environment" or "game.classpath-head" or "game.post-exit") continue;
            if (setting.Key == "game.title")
            {
                if (setting.ValidationError is not null) throw new InvalidDataException("窗口标题设置无效。");
                if (setting.Source >= SettingsLayer.Instance || string.IsNullOrEmpty(request.WindowTitle))
                    request = request with { WindowTitle = setting.Value.Value ?? "" };
                continue;
            }
            if (setting.Key == "game.launcher-visibility")
            {
                if (setting.ValidationError is not null) throw new InvalidDataException("启动器窗口行为设置无效。");
                request = request with
                {
                    LauncherVisibility = setting.Value.Value switch
                    {
                        "minimize" => MinecraftLauncherVisibility.Minimize,
                        "hide" => MinecraftLauncherVisibility.Hide,
                        "hide-and-close" => MinecraftLauncherVisibility.HideAndClose,
                        _ => MinecraftLauncherVisibility.Keep
                    }
                };
                continue;
            }
            if (setting.Key is "game.cpu-sets" or "game.process-quality")
            {
                if (setting.ValidationError is not null) throw new InvalidDataException(setting.ValidationError);
                request = setting.Key == "game.cpu-sets"
                    ? request with { CpuSets = MinecraftProcessScheduling.ParseCpuSets(setting.Value.Value ?? "") }
                    : request with
                    {
                        ProcessQuality = setting.Value.Value switch
                        {
                            "default" => MinecraftProcessQuality.Default,
                            "efficiency" => MinecraftProcessQuality.Efficiency,
                            "background" => MinecraftProcessQuality.Background,
                            _ => throw new InvalidDataException("Invalid process quality setting.")
                        }
                    };
                continue;
            }
            if (setting.Key == "game.process-priority")
            {
                if (setting.ValidationError is not null) throw new InvalidDataException("进程优先级设置无效。");
                request = request with
                {
                    ProcessPriority = setting.Value.Value switch
                    {
                        "normal" => System.Diagnostics.ProcessPriorityClass.Normal,
                        "below-normal" => System.Diagnostics.ProcessPriorityClass.BelowNormal,
                        "above-normal" => System.Diagnostics.ProcessPriorityClass.AboveNormal,
                        "high" => System.Diagnostics.ProcessPriorityClass.High,
                        "real-time" => System.Diagnostics.ProcessPriorityClass.RealTime,
                        _ => throw new InvalidDataException("进程优先级设置无效。")
                    }
                };
                continue;
            }
            if (setting.Key == "game.server" && setting.Source != SettingsLayer.Builtin)
            {
                if (setting.ValidationError is not null) throw new InvalidDataException("默认服务器设置无效。");
                if (setting.Source >= SettingsLayer.Instance || string.IsNullOrWhiteSpace(request.Server))
                    request = request with { Server = string.IsNullOrEmpty(setting.Value.Value) ? null : setting.Value.Value };
                continue;
            }
            if (setting.Key is "game.wrapper" or "game.pre-launch" or "game.pre-launch-wait")
            {
                if (setting.ValidationError is not null)
                    throw new InvalidDataException("启动钩子设置无效。");
                if (setting.Key == "game.wrapper"
                    && (setting.Source >= SettingsLayer.Instance || string.IsNullOrWhiteSpace(request.WrapperCommand)))
                    request = request with { WrapperCommand = setting.Value.Value ?? "" };
                if (setting.Key == "game.pre-launch"
                    && (setting.Source >= SettingsLayer.Instance || string.IsNullOrWhiteSpace(request.PreLaunchCommand)))
                    request = request with { PreLaunchCommand = setting.Value.Value ?? "" };
                if (setting.Key == "game.pre-launch-wait")
                {
                    var command = snapshot.Values.Single(item => item.Key == "game.pre-launch");
                    if (setting.Source >= SettingsLayer.Instance || command.Source >= SettingsLayer.Instance
                        || !hasLegacyPreLaunch)
                        request = request with { WaitForPreLaunchCommand = setting.Value.Value == "true" };
                }
                continue;
            }
            if (setting.Key is "game.environment" or "game.classpath-head" or "game.post-exit" or "game.safe-launch")
            {
                if (setting.ValidationError is not null) throw new InvalidDataException("扩展启动配置无效。");
                request = setting.Key switch
                {
                    "game.environment" => request with { EnvironmentVariables = MinecraftLaunchHooks.ParseEnvironment(setting.Value.Value ?? "") },
                    "game.classpath-head" when setting.Source != SettingsLayer.Builtin => request with { ClasspathHeadEntries = MinecraftLaunchHooks.ParseClasspathHead(setting.Value.Value ?? "") },
                    "game.post-exit" => request with { PostExitCommand = setting.Value.Value ?? "" },
                    "game.safe-launch" => request with { Overlay = request.Overlay with { SafeLaunch = setting.Value.Value == "true" } },
                    _ => request,
                };
                continue;
            }
            if (setting.Key is "game.gpu-preference" or "game.renderer")
            {
                if (setting.ValidationError is not null) throw new InvalidDataException("游戏图形设置无效。");
                if (setting.Source == SettingsLayer.Builtin) continue;
                request = setting.Key == "game.gpu-preference"
                    ? request with { GpuPreference = setting.Value.Value ?? "auto" }
                    : request with { RendererPreference = setting.Value.Value ?? "auto" };
                continue;
            }
            if (setting.Key == "game.system-glfw")
            {
                if (setting.Source == SettingsLayer.Builtin) continue;
                if (setting.ValidationError is not null || setting.Value.Value is not ("true" or "false"))
                    throw new InvalidDataException("System GLFW 设置无效。");
                request = request with { UseSystemGlfw = setting.Value.Value == "true" };
                continue;
            }
            if (setting.Key == "game.memory" && setting.Source != SettingsLayer.Builtin)
            {
                if (setting.ValidationError is not null) throw new InvalidDataException("内存设置无效。");
                request = request with
                {
                    MemoryMegabytes = setting.Value.Mode == SettingsOverrideMode.Auto
                    ? automaticMemoryMegabytes ?? request.MemoryMegabytes
                    : int.Parse(setting.Value.Value!, CultureInfo.InvariantCulture)
                };
                continue;
            }
            if (setting.Value.Mode != SettingsOverrideMode.Custom
                || (setting.Source < SettingsLayer.Instance && !(setting.Key == "game.window-mode" && setting.Source == SettingsLayer.Global))) continue;
            string value = setting.Value.Value ?? "";
            request = setting.Key switch
            {
                "game.width" => request with { Width = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
                "game.height" => request with { Height = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
                "game.window-mode" => request with { Fullscreen = value == "fullscreen" },
                "game.jvm" => request with { CustomJvmArguments = value },
                "game.arguments" => request with { CustomGameArguments = value },
                _ => request,
            };
        }
        if (safeLaunch) return MinecraftSafeLaunchPolicy.Apply(request with { Overlay = new(SafeLaunch: true) });
        _ = MinecraftLaunchHooks.ParseWrapper(request.WrapperCommand);
        MinecraftLaunchHooks.ValidatePreLaunch(request.PreLaunchCommand);
        MinecraftLaunchHooks.ValidatePreLaunch(request.PostExitCommand);
        return MinecraftSafeLaunchPolicy.Apply(request with { Overlay = snapshot.Overlay with { SafeLaunch = snapshot.Overlay.SafeLaunch || request.Overlay.SafeLaunch } });
    }

    internal static MinecraftJavaRequirementRequest CreateJavaRequirement(
        MinecraftInstanceDescriptor instance,
        MinecraftResolvedVersionManifests manifests,
        MinecraftModLoaderDescriptor loader)
    {
        (int? major, string? component) = ReadManifestJava(manifests);
        MinecraftGameVersion? gameVersion = ResolveGameVersion(manifests);
        return new MinecraftJavaRequirementRequest
        {
            MinecraftVersion = gameVersion,
            HasReliableVanillaVersion = gameVersion is not null,
            ReleaseTime = ReadReleaseTime(manifests) ?? instance.Version.ReleaseTime,
            ManifestJavaMajorVersion = major,
            ManifestJavaComponent = component,
            HasOptiFine = loader.Kind == MinecraftModLoaderKind.OptiFine,
            HasForge = loader.Kind is MinecraftModLoaderKind.Forge or MinecraftModLoaderKind.NeoForge,
            ForgeVersion = loader.Version,
            HasCleanroom = loader.Kind == MinecraftModLoaderKind.Cleanroom,
            CleanroomVersion = loader.Version ?? ReadLoaderVersion(manifests.Current, "cleanroom"),
            HasFabric = loader.Kind is MinecraftModLoaderKind.Fabric or MinecraftModLoaderKind.Quilt,
            HasLiteLoader = loader.Kind == MinecraftModLoaderKind.LiteLoader,
            HasLabyMod = loader.Kind == MinecraftModLoaderKind.LabyMod,
        };
    }

    private static (int? Major, string? Component) ReadManifestJava(
        MinecraftResolvedVersionManifests manifests)
    {
        foreach (JsonObject manifest in EnumerateEffectiveOrder(manifests))
        {
            if (manifest["javaVersion"] is not JsonObject java)
            {
                continue;
            }

            int? major = java["majorVersion"]?.GetValue<int>();
            string? component = java["component"]?.ToString();
            if (major is not null || !string.IsNullOrWhiteSpace(component))
            {
                return (major, component);
            }
        }

        return (null, null);
    }

    private static MinecraftGameVersion? ResolveGameVersion(
        MinecraftResolvedVersionManifests manifests)
    {
        foreach (JsonObject manifest in EnumerateEffectiveOrder(manifests))
        {
            if (MinecraftGameVersion.TryParse(manifest["id"]?.ToString(), out MinecraftGameVersion parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadReleaseTime(MinecraftResolvedVersionManifests manifests)
    {
        foreach (JsonObject manifest in EnumerateEffectiveOrder(manifests))
        {
            string? raw = manifest["releaseTime"]?.ToString() ?? manifest["time"]?.ToString();
            if (DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static IEnumerable<JsonObject> EnumerateEffectiveOrder(
        MinecraftResolvedVersionManifests manifests)
    {
        yield return manifests.Current;
        foreach (JsonObject inherited in manifests.Inherited)
        {
            yield return inherited;
        }
    }

    private static string? ReadLoaderVersion(JsonObject manifest, string marker)
    {
        string? id = manifest["id"]?.ToString();
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        int index = id.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        index += marker.Length;
        while (index < id.Length && id[index] is '-' or '_' or ' ')
        {
            index++;
        }

        int end = index;
        while (end < id.Length && (char.IsDigit(id[end]) || id[end] is '.'))
        {
            end++;
        }

        return end > index ? id[index..end] : null;
    }

    private int ResolveMemoryMegabytes(
        MinecraftInstanceMetadata metadata,
        MinecraftModLoaderKind loader)
    {
        if (metadata.MemorySolution == 1)
        {
            return SliderValueToMemoryMegabytes(metadata.CustomMemorySize);
        }

        if (GetSetting("LaunchRamType", 0) == 1)
        {
            return SliderValueToMemoryMegabytes(GetSetting("LaunchRamCustom", 15));
        }

        return ResolveAutomaticMemoryMegabytes(loader);
    }

    private static int ResolveAutomaticMemoryMegabytes(MinecraftModLoaderKind loader)
    {

        double availableGigabytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes > 0
            ? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d / 1024d
            : 4d;
        double target = loader switch
        {
            MinecraftModLoaderKind.Forge or MinecraftModLoaderKind.NeoForge
                or MinecraftModLoaderKind.Fabric or MinecraftModLoaderKind.Quilt
                or MinecraftModLoaderKind.Cleanroom => 4.5d,
            MinecraftModLoaderKind.OptiFine => 3d,
            _ => 2.5d,
        };
        return Math.Max(512, (int)Math.Round(Math.Min(target, Math.Max(1.5d, availableGigabytes * 0.4d)) * 1024d));
    }

    private static int SliderValueToMemoryMegabytes(int value)
    {
        double gigabytes = value switch
        {
            <= 12 => value * 0.1d + 0.3d,
            <= 25 => (value - 12) * 0.5d + 1.5d,
            <= 33 => value - 25d + 8d,
            _ => (value - 33d) * 2d + 16d,
        };
        return Math.Max(256, (int)Math.Round(gigabytes * 1024d));
    }

    private T GetSetting<T>(string key, T fallback)
    {
        XsrResult<T> result = _settings.GetValue<T>(key);
        return result.IsSuccess ? result.Value : fallback;
    }

    private string SelectExecutable(JavaInstallation installation)
    {
        bool forceConsole = GetSetting("LaunchAdvanceNoJavaw", false);
        return !forceConsole && installation.WindowedJavaExecutablePath is { Length: > 0 } windowed
            ? windowed
            : installation.JavaExecutablePath;
    }

    private string SelectWindowedSibling(string executable)
    {
        if (!OperatingSystem.IsWindows() || GetSetting("LaunchAdvanceNoJavaw", false))
        {
            return executable;
        }

        string windowed = Path.Combine(Path.GetDirectoryName(executable)!, "javaw.exe");
        return File.Exists(windowed) ? windowed : executable;
    }

    private static string[] SplitClasspathHead(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split([';', Path.PathSeparator], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static int JavaMajor(Version version) => version.Major == 1 ? version.Minor : version.Major;

    private sealed record ResolvedJava(string ExecutablePath, int MajorVersion);
}
