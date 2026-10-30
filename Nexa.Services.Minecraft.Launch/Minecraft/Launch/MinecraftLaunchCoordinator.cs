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

/// <summary>
/// Product-level launch orchestration. Callers identify an instance and account; this service
/// owns manifests, inheritance, platform, credentials, Java, settings, planning, and execution.
/// </summary>
public sealed partial class MinecraftLaunchCoordinator
{
    public bool HasPendingFinalization => _executor.HasPendingFinalization;
    public Task WaitForFinalizationAsync(CancellationToken cancellationToken = default)
        => _executor.WaitForFinalizationAsync(cancellationToken);
    public IWorkScheduler? WorkScheduler { get; init; }
    public IJavaRuntimeRegistrationStore? JavaRegistrations { get; init; }
    private bool _safeLaunchRequired;
    public XsrResult SetSafeLaunchSession(MinecraftSafeLaunchSessionCommand command)
    { Volatile.Write(ref _safeLaunchRequired, command.Required); return XsrResult.Success(); }
    public MinecraftSafeLaunchSessionSnapshot ReadSafeLaunchSession(MinecraftSafeLaunchSessionQuery query) => new(Volatile.Read(ref _safeLaunchRequired));

    private static readonly int[] SelectableJavaMajors = [8, 16, 17, 21, 25];
    private static readonly TimeSpan StageHeartbeatInterval = TimeSpan.FromMilliseconds(120);
    private const double StageHeartbeatStep = 0.05d;
    private const double StageHeartbeatCeiling = 0.92d;

    private readonly string _minecraftRootDirectory;
    private readonly string _javaRuntimeRootDirectory;
    private readonly LogService? _log;
    private readonly MinecraftLaunchProgressPublisher? _progress;
    private readonly MinecraftInstanceDiscovery _instances;
    private readonly IAccountLaunchAccess _accounts;
    private readonly SettingsService _settings;
    private readonly SettingsPolicyService? _settingsPolicy;
    private readonly JavaSelectionService _javaSelection;
    private readonly IJavaRuntimeInstaller _javaInstaller;
    private readonly MinecraftLaunchExecutor _executor;
    private readonly MinecraftLaunchFileCompletion? _fileCompletion;
    private readonly MinecraftLaunchPlatform _platform;
    private readonly IAccountLaunchIdentityResolver _identityResolver;
    private readonly string _launcherVersion;
    private readonly IMinecraftWindowProbe _windowProbe;
    private readonly Action<int>? _gameWindowAppeared;
    private readonly object _launchGate = new();
    private CancellationTokenSource? _activeLaunch;
    private sealed record JavaChoice(bool Approve, ResolvedJava? Manual = null, string? Component = null, int? Major = null);
    private TaskCompletionSource<JavaChoice>? _acquisitionDecision;
    private JavaRequirementResolution? _pendingJavaRequirement;
    private readonly IAuthlibInjectorProvider? _authlib;
    private readonly LaunchPreflightGate? _preflight;

    public MinecraftLaunchCoordinator(
        string minecraftRootDirectory,
        string javaRuntimeRootDirectory,
        MinecraftInstanceDiscovery instances,
        IAccountLaunchAccess accounts,
        SettingsService settings,
        JavaSelectionService javaSelection,
        IJavaRuntimeInstaller javaInstaller,
        MinecraftLaunchExecutor executor,
        MinecraftLaunchPlatform? platform = null,
        LogService? log = null,
        MinecraftLaunchProgressPublisher? progress = null,
        IAccountLaunchIdentityResolver? identityResolver = null,
        string? launcherVersion = null,
        IMinecraftWindowProbe? windowProbe = null,
        IAuthlibInjectorProvider? authlib = null,
        Action<int>? gameWindowAppeared = null,
        MinecraftLaunchFileCompletion? fileCompletion = null,
        SettingsPolicyService? settingsPolicy = null,
        LaunchPreflightGate? preflight = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftRootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(javaRuntimeRootDirectory);
        _minecraftRootDirectory = Path.GetFullPath(minecraftRootDirectory);
        _javaRuntimeRootDirectory = Path.GetFullPath(javaRuntimeRootDirectory);
        _log = log;
        _progress = progress;
        _identityResolver = identityResolver ?? accounts;
        _launcherVersion = string.IsNullOrWhiteSpace(launcherVersion) ? "2.0.0" : launcherVersion;
        _windowProbe = windowProbe ?? new MinecraftWindowProbe();
        _gameWindowAppeared = gameWindowAppeared;
        _authlib = authlib;
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _javaSelection = javaSelection ?? throw new ArgumentNullException(nameof(javaSelection));
        _javaInstaller = javaInstaller ?? throw new ArgumentNullException(nameof(javaInstaller));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _fileCompletion = fileCompletion;
        _settingsPolicy = settingsPolicy;
        _preflight = preflight;
        _platform = platform ?? MinecraftLaunchPlatform.Detect();
        if (_platform.OperatingSystem == MinecraftLibraryOperatingSystem.Unknown)
        {
            throw new ArgumentException(
                "The launch platform must identify a concrete Mojang operating system.",
                nameof(platform));
        }
    }

    /// <summary>
    /// Resolves a pending Java acquisition approval: true downloads the runtime, false declines
    /// it. Returns false when no acquisition is awaiting a decision.
    /// </summary>
    public bool DecideJavaAcquisition(bool approve)
    {
        TaskCompletionSource<JavaChoice>? decision;
        lock (_launchGate)
        {
            decision = _acquisitionDecision;
        }

        if (decision is null)
        {
            _log?.Debug("Java", "Acquisition decision ignored: nothing is pending.");
            return false;
        }

        _log?.Info("Java", $"Runtime acquisition decision approve={approve}.");
        return decision.TrySetResult(new(approve));
    }

    public async ValueTask<XsrResult> SelectJavaVersionAsync(int major, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<JavaChoice>? decision;
        JavaRequirementResolution? requirement;
        lock (_launchGate) { decision = _acquisitionDecision; requirement = _pendingJavaRequirement; }
        if (decision is null || requirement is null || major is not (8 or 16 or 17 or 21 or 25))
            return XsrResult.Failure(MinecraftErrors.InvalidRequest("没有待处理的 Java 选择。"));
        var requested = major == 8
            ? new JavaVersionRange(new Version(1, 8), JavaVersionRange.Java8Maximum)
            : new JavaVersionRange(new Version(major, 0), new Version(major, int.MaxValue));
        if (!requirement.Range.TryIntersect(requested, out var range))
            return XsrResult.Failure(MinecraftErrors.JavaUnavailable($"Java {major} 不兼容此游戏版本。"));
        var narrowed = JavaRequirementResolution.Valid(range, major.ToString(CultureInfo.InvariantCulture));
        var selected = await Task.Run(async () => await _javaSelection.SelectAsync(narrowed, new AutoSelectJavaPreference(), cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        var acquisition = JavaRuntimeAcquisitionPlanner.Plan(narrowed);
        if (!selected.Success && !acquisition.CanAutoDownload)
            return XsrResult.Failure(MinecraftErrors.JavaUnavailable("此 Java 版本无法自动下载。"));
        JavaChoice choice = selected.Success && selected.SelectedJava is { } java
            ? new(false, new ResolvedJava(SelectExecutable(java.Installation), java.Installation.MajorVersion))
            : new(true, Component: acquisition.DownloadComponent, Major: major);
        lock (_launchGate)
        {
            if (!ReferenceEquals(_acquisitionDecision, decision) || !decision.TrySetResult(choice))
                return XsrResult.Failure(MinecraftErrors.InvalidRequest("Java 选择已失效。"));
        }
        return XsrResult.Success();
    }

    public async ValueTask<XsrResult> SelectJavaAsync(string path, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<JavaChoice>? decision;
        JavaRequirementResolution? requirement;
        lock (_launchGate) { decision = _acquisitionDecision; requirement = _pendingJavaRequirement; }
        if (decision is null || requirement is null) return XsrResult.Failure(MinecraftErrors.InvalidRequest("no Java choice is pending."));
        JavaSelectionResult selection = await _javaSelection.SelectAsync(requirement, new ExistingJavaPreference(path), cancellationToken).ConfigureAwait(false);
        if (!selection.Success || selection.SelectedJava is not { } java)
            return XsrResult.Failure(MinecraftErrors.JavaUnavailable("所选 Java 不满足此版本要求，请选择兼容的 Java。"));
        lock (_launchGate)
        {
            if (!ReferenceEquals(_acquisitionDecision, decision) || !decision.TrySetResult(new(false,
                new ResolvedJava(SelectExecutable(java.Installation), java.Installation.MajorVersion))))
                return XsrResult.Failure(MinecraftErrors.InvalidRequest("the pending Java choice changed."));
        }
        return XsrResult.Success();
    }

    /// <summary>
    /// Cancels the active launch pipeline between stages. Returns false when no launch is
    /// running; the game process itself is cancelled through the process service instead.
    /// </summary>
    public bool CancelActiveLaunch()
    {
        // Cancel under the same lock that governs registration and disposal: a racing launch
        // completion may null the field and dispose the CTS between a split read and Cancel.
        lock (_launchGate)
        {
            if (_activeLaunch is null)
            {
                _log?.Debug("Launch", "Launch cancellation ignored: no pipeline is running.");
                return false;
            }

            _log?.Info("Launch", "Launch pipeline cancellation requested.");
            _activeLaunch.Cancel();
            return true;
        }
    }

    private static readonly TimeSpan GameWindowPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GameWindowWaitLimit = TimeSpan.FromMinutes(2);
}
