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

    public ValueTask<XsrResult<MinecraftLaunchPreparation>> PrepareAsync(
        string instanceId,
        int accountIndex,
        CancellationToken cancellationToken = default)
        => PrepareAsync(instanceId, accountIndex, _minecraftRootDirectory, cancellationToken);

    public async ValueTask<XsrResult<MinecraftLaunchPreparation>> PrepareAsync(
        string instanceId, int accountIndex, string minecraftRootDirectory,
        CancellationToken cancellationToken = default)
    {
        using LogOperation? operation = _log?.BeginOperation("Launch", "PrepareLaunch", $"instance={instanceId} account_index={accountIndex}");
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            operation?.Reject("minecraft.invalid_request");
            return XsrResult.Failure<MinecraftLaunchPreparation>(
                MinecraftErrors.InvalidRequest("an instance id is required."));
        }

        try
        {
            string root = Nexa.Core.PathIdentity.Normalize(minecraftRootDirectory);
            string javaRoot = Nexa.Core.PathIdentity.Comparer.Equals(root, _minecraftRootDirectory)
                ? _javaRuntimeRootDirectory : Path.Combine(root, "runtime");
            operation?.Stage("resolve_instance");
            IReadOnlyList<MinecraftInstanceDescriptor> installed = await _instances.RefreshAsync(root, cancellationToken).ConfigureAwait(false);
            MinecraftInstanceDescriptor? instance = installed.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, instanceId, StringComparison.OrdinalIgnoreCase));
            if (instance is null)
            {
                operation?.Reject("minecraft.instance_not_found");
                return XsrResult.Failure<MinecraftLaunchPreparation>(
                    MinecraftErrors.InstanceNotFound(instanceId));
            }
            if (instance.MetadataError is { } metadataError)
                return XsrResult.Failure<MinecraftLaunchPreparation>(MinecraftErrors.InvalidRequest(metadataError));
            MinecraftInstanceMetadataStore.ValidateShape(instance.Metadata);

            operation?.Stage("resolve_account");
            SettingsEffectiveSnapshot? effectiveSettings = null;
            if (_settingsPolicy is not null)
            {
                var policy = _settingsPolicy.Read(new(instance.DirectoryPath));
                if (!policy.IsSuccess) throw new InvalidOperationException("无法读取版本启动设置。");
                effectiveSettings = policy.Value;
            }
            string method = string.Empty;
            XsrResult<MinecraftLaunchIdentity> identityResult =
                XsrResult.Failure<MinecraftLaunchIdentity>(MinecraftErrors.InvalidRequest("the account was not resolved."));
            XsrResult<LaunchProfile> profileResult =
                XsrResult.Failure<LaunchProfile>(MinecraftErrors.InvalidRequest("the account was not resolved."));
            await RunStageAsync(
                MinecraftLaunchStages.Login,
                completedBefore: 0d,
                MinecraftLaunchStages.LoginWeight,
                method,
                async token =>
                {
                    profileResult = _accounts.GetProfile(accountIndex);
                    if (!profileResult.IsSuccess)
                    {
                        return;
                    }

                    identityResult = await _identityResolver
                        .ResolveAsync(accountIndex, profileResult.Value, token)
                        .ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            if (!profileResult.IsSuccess)
            {
                operation?.Reject(profileResult.Error!.Code.Value);
                return XsrResult.Failure<MinecraftLaunchPreparation>(profileResult.Error!);
            }

            if (!identityResult.IsSuccess)
            {
                operation?.Reject(identityResult.Error!.Code.Value);
                return XsrResult.Failure<MinecraftLaunchPreparation>(identityResult.Error!);
            }
            if (MinecraftServerEnvironmentPolicy.ValidateIdentity(instance.Metadata, identityResult.Value) is { } identityMismatch)
                return XsrResult.Failure<MinecraftLaunchPreparation>(MinecraftErrors.InvalidRequest(identityMismatch));

            method = identityResult.Value.Mode.ToString().ToLowerInvariant();

            operation?.Stage("resolve_manifests");
            MinecraftResolvedVersionManifests manifests = default!;
            MinecraftModLoaderDescriptor loader = default!;
            await RunStageAsync(
                MinecraftLaunchStages.CompleteFiles,
                MinecraftLaunchStages.LoginWeight,
                MinecraftLaunchStages.CompleteFilesWeight,
                method,
                async token =>
                {
                    manifests = await MinecraftVersionJsonReader
                        .ResolveAsync(instance, root, token)
                        .ConfigureAwait(false);
                    loader = MinecraftModLoaderDetector.Detect(manifests.Current);
                    // The legacy 补全文件 step: verify every referenced file on disk and
                    // repair the missing ones before the JVM starts. Real download progress
                    // flows through the same stage reports, so the narration stays honest.
                    if (_fileCompletion is { } completion
                        && effectiveSettings?.Values.Single(value => value.Key == "game.auto-repair").Value.Value != "false")
                    {
                        await completion.CompleteAsync(
                            root,
                            instance,
                            manifests,
                            _platform,
                            method,
                            _progress,
                            token).ConfigureAwait(false);
                    }
                },
                cancellationToken,
                heartbeat: false).ConfigureAwait(false);
            _log?.Debug("Launch", $"Effective manifest resolved instance={instanceId} inherited={manifests.Inherited.Count} loader={loader.Kind}");
            LaunchModInventory? environmentInventory = null;
            if (instance.Metadata.ServerRequiredMods.Length > 0)
            {
                bool safe = Volatile.Read(ref _safeLaunchRequired) || effectiveSettings?.Overlay.SafeLaunch == true
                    || effectiveSettings?.Values.Any(value => value.Key == "game.safe-launch" && value.Value.Value == "true") == true;
                environmentInventory = safe ? new([], 0, true) : effectiveSettings?.Overlay.ModsSource is { } overlayMods
                    ? await LaunchModInventoryReader.ReadDirectoryAsync(overlayMods, cancellationToken).ConfigureAwait(false)
                    : await LaunchModInventoryReader.ReadAsync(instance.Metadata.InstanceIsolation ? instance.DirectoryPath : root, cancellationToken).ConfigureAwait(false);
            }
            var environmentErrors = MinecraftServerEnvironmentPolicy.CompareEnvironment(instance.Metadata,
                ResolveGameVersion(manifests)?.ToString() ?? instance.VersionId, loader.Kind, environmentInventory);
            if (environmentErrors.Count > 0)
                return XsrResult.Failure<MinecraftLaunchPreparation>(MinecraftErrors.InvalidRequest(string.Join("\n", environmentErrors)));
            MinecraftJavaRequirementRequest javaRequest = CreateJavaRequirement(
                instance,
                manifests,
                loader);
            JavaPreference preference = instance.Metadata.JavaSelectionMode == 2
                && !string.IsNullOrWhiteSpace(instance.Metadata.SelectedJavaPath)
                ? new ExistingJavaPreference(instance.Metadata.SelectedJavaPath)
                : new AutoSelectJavaPreference();
            bool automaticallyInstallJava = false;
            if (effectiveSettings is not null)
            {
                preference = ApplyJavaPreference(preference, effectiveSettings);
                automaticallyInstallJava = effectiveSettings.Values.Single(value => value.Key == "java.auto-install").Value.Value == "true";
            }
            operation?.Stage("select_java", $"os={_platform.OperatingSystem} os_version={_platform.OperatingSystemVersion} arm64={_platform.IsArm64Architecture} manifest_major={javaRequest.ManifestJavaMajorVersion}");
            JavaSelectionResult java = default!;
            XsrResult<ResolvedJava> resolvedJava = XsrResult.Failure<ResolvedJava>(MinecraftErrors.JavaUnavailable("java was not resolved."));
            await RunStageAsync(
                MinecraftLaunchStages.GetJava,
                MinecraftLaunchStages.LoginWeight + MinecraftLaunchStages.CompleteFilesWeight,
                MinecraftLaunchStages.GetJavaWeight,
                method,
                async token =>
                {
                    java = await _javaSelection
                        .SelectAsync(javaRequest, preference, token)
                        .ConfigureAwait(false);
                    if (!java.Success && java.FailureReason == JavaSelectionFailureReason.NoCompatibleRuntime
                        && !Nexa.Core.PathIdentity.Comparer.Equals(root, _minecraftRootDirectory))
                    {
                        JavaSelectionResult local = await new JavaSelectionService(new LocalJavaRuntimeLocator(javaRoot, _log)
                        { RegisteredRuntimes = JavaRegistrations is null ? null : () => JavaRegistrations.Read().Registrations })
                            .SelectAsync(javaRequest, preference, token).ConfigureAwait(false);
                        if (local.Success) java = local;
                    }
                    resolvedJava = await ResolveJavaAsync(
                        java,
                        preference,
                        loader.Kind is MinecraftModLoaderKind.Forge or MinecraftModLoaderKind.NeoForge,
                        operation, javaRoot, automaticallyInstallJava, token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            if (!resolvedJava.IsSuccess)
            {
                operation?.Reject(resolvedJava.Error!.Code.Value);
                return XsrResult.Failure<MinecraftLaunchPreparation>(resolvedJava.Error!);
            }

            operation?.Stage("apply_launch_settings", $"java_major={resolvedJava.Value.MajorVersion} java_path={resolvedJava.Value.ExecutablePath}");
            MinecraftLaunchRequest request = CreateRequest(
                instance,
                manifests,
                loader,
                identityResult.Value,
                resolvedJava.Value, root, effectiveSettings);
            if (identityResult.Value.AuthServer is { } authServer)
            {
                if (_authlib is null) throw new InvalidOperationException("Authlib Injector preparation is not composed.");
                operation?.Stage("prepare_authlib_injector");
                string injector = await _authlib.EnsureAsync(root, cancellationToken).ConfigureAwait(false);
                request = request with { AuthlibServer = authServer, AuthlibInjectorPath = injector };
            }
            operation?.Complete($"instance={instance.Id} memory_mb={request.MemoryMegabytes}");
            return XsrResult.Success(new MinecraftLaunchPreparation(
                instance,
                request,
                java.Requirement));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation?.Cancel();
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or HttpRequestException
            or PlatformNotSupportedException)
        {
            operation?.Fail(exception);
            return XsrResult.Failure<MinecraftLaunchPreparation>(
                MinecraftErrors.LaunchPreparationFailed(exception.Message));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not AccessViolationException)
        {
            operation?.Fail(exception);
            throw;
        }
    }
}
