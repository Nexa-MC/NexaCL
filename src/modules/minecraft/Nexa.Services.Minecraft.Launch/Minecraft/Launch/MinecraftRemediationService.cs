
using Nexa.Services.Caching;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;
using Nexa.Xsr.State;

namespace Nexa.Services.Minecraft.Launch;

/// <summary>Minecraft remediation algorithms bound to the same installer lifetimes as product routes.</summary>
public sealed class MinecraftRemediationService(ISettingsPolicyWriter settings, XsrStateStore store)
{
    public ISharedStateCache? SharedStateCache { get; set; }
    private sealed record JavaBindings(IJavaRuntimeLocator Locator, IJavaRuntimeInstaller Installer, string RuntimeRoot, MinecraftLaunchFileCompletion Files);
    private JavaBindings? _java;
    private IMinecraftInstallExecutor? _install;

    public void BindJava(IJavaRuntimeLocator locator, IJavaRuntimeInstaller installer, string runtimeRoot, MinecraftLaunchFileCompletion files)
    {
        if (Interlocked.CompareExchange(ref _java, new(locator, installer, runtimeRoot, files), null) is not null)
            throw new InvalidOperationException("Java remediation services are already bound.");
    }
    public void BindInstall(IMinecraftInstallExecutor installer)
    {
        if (Interlocked.CompareExchange(ref _install, installer, null) is not null)
            throw new InvalidOperationException("Install remediation service is already bound.");
    }
    public bool IsAvailable(string id) => id is "remediation.game.repair_version" or "remediation.loader.repair"
        ? Volatile.Read(ref _install) is not null : Volatile.Read(ref _java) is not null;

    public async Task<RemediationResult> ExecuteAsync(RemediationRequest request, CancellationToken token)
    {
        if (request.Id is not ("remediation.java.select" or "remediation.java.download" or "remediation.java.switch_recommended"
            or "remediation.game.repair_files" or "remediation.game.repair_version" or "remediation.loader.repair"))
            return new(request.Id, false, "unsupported", "此修复操作不可用。");
        string? instance = request.Arguments?.GetValueOrDefault("instanceDirectory");
        if (string.IsNullOrWhiteSpace(instance) || !Path.IsPathFullyQualified(instance))
            return new(request.Id, false, "missing_scope", "请先选择需要修复的实例。");
        instance = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instance));
        var versions = Directory.GetParent(instance);
        if (versions?.Name != "versions" || versions.Parent is null) throw new InvalidDataException("实例目录无效。");
        string root = versions.Parent.FullName;
        var query = new MachineCapabilityQuery(instance, Path.GetFileName(instance), root) { RefreshInstance = true };
        if (request.Id is "remediation.game.repair_version" or "remediation.loader.repair")
        {
            var edit = await MinecraftInstallEditService.ReadAsync(new(root, Path.GetFileName(instance)), token).ConfigureAwait(false);
            var selected = edit.Selection.FirstOrDefault(item => !InstallLoaderFacts.IsAddon(item.Loader));
            var addons = edit.Selection.Where(item => item != selected).Select(item => new MinecraftInstallAddon(item.Loader, item.Version)).ToArray();
            var result = await _install!.InstallAsync(new(root, edit.GameVersion, selected?.Loader, selected?.Version, addons,
                edit.InstanceId, edit.Fingerprint)
            { ForceReinstall = true }, token).ConfigureAwait(false);
            return new(request.Id, result.IsSuccess, result.IsSuccess ? "ok" : "install_failed", result.IsSuccess ? "已完成版本修复。" : result.Error?.Message ?? "版本修复未完成。");
        }
        var primary = await MinecraftPrimaryInstanceScope.ResolveAsync(root, query, SharedStateCache, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("无法读取实例清单。");
        var java = _java ?? throw new InvalidOperationException("Java services unavailable.");
        if (request.Id == "remediation.game.repair_files")
        {
            using var lease = await InstanceRecoveryOperationGate.EnterRestoreAsync(root, token).ConfigureAwait(false);
            primary = await MinecraftPrimaryInstanceScope.ResolveAsync(root, query, SharedStateCache, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("无法读取实例清单。");
            if (store.TryResolve(MinecraftProcessStateComposition.SessionsKey, out var id)
                && store.ReadCollection<MinecraftProcessSnapshot>(id, cancellationToken: token).Items.Any(item => item.State is MinecraftProcessState.Created or MinecraftProcessState.Running
                    && item.InstanceDirectory is { } directory && Nexa.Core.PathIdentity.Comparer.Equals(Directory.GetParent(directory)?.Parent?.FullName, root)))
                return new(request.Id, false, "game_running", "请先结束游戏进程再修复文件。");
            await java.Files.CompleteAsync(root, primary.Instance, primary.Manifests, MinecraftLaunchPlatform.Detect(), "预检文件修复", null, token).ConfigureAwait(false);
            return new(request.Id, true, "ok", "已校验并补全游戏文件。");
        }
        var loader = await InstalledLoaderProjection.ReadAsync(primary, root, query, token).ConfigureAwait(false);
        var requirement = MinecraftLaunchCoordinator.CreateJavaRequirement(primary.Instance, primary.Manifests, loader);
        JavaPreference preference = new AutoSelectJavaPreference();
        if (request.Id == "remediation.java.select")
        {
            string? path = request.Arguments?.GetValueOrDefault("executable");
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return new(request.Id, false, "choose_java", "请选择 Java 可执行文件。");
            preference = new ExistingJavaPreference(path);
        }
        var selection = await new JavaSelectionService(java.Locator).SelectAsync(requirement, preference, token).ConfigureAwait(false);
        if (!selection.Success && request.Id == "remediation.java.download" && selection.SuggestedDownloadComponent is { } component)
        {
            string executable = await java.Installer.InstallAsync(component, java.RuntimeRoot, cancellationToken: token).ConfigureAwait(false);
            java.Locator.Invalidate();
            selection = await new JavaSelectionService(java.Locator).SelectAsync(requirement, new ExistingJavaPreference(executable), token).ConfigureAwait(false);
        }
        if (!selection.Success || selection.SelectedJava is null) return new(request.Id, false, "java_unavailable", "没有找到符合本实例要求的 Java。");
        var saved = await settings.SetAsync(new("java.runtime", SettingsLayer.Instance,
            new(SettingsOverrideMode.Custom, selection.SelectedJava.Installation.JavaExecutablePath), instance), token).ConfigureAwait(false);
        return new(request.Id, saved.IsSuccess, saved.IsSuccess ? "ok" : "settings_failed", saved.IsSuccess ? "已为此实例选择兼容 Java。" : saved.Error?.Message ?? "Java 设置未保存。");
    }
}
