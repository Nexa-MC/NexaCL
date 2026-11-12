using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Launch;

public sealed partial class MinecraftLaunchCoordinator
{
    public Func<CancellationToken, ValueTask<XsrResult>>? BeforeProcessStart { get; init; }
    private bool _attemptChild;
    private long? _expectedAccountRosterRevision;
    private long? _expectedSettingsRevision;
    private string? _authorizedProfileId;
    private long? _pluginMemoryMiB, _pluginWidth, _pluginHeight;
    private int _launchDisposed;
    private Guid _attemptId;
    private Action<MinecraftProcessSnapshot>? _sessionEnded;
    private readonly Dictionary<Guid, MinecraftLaunchCoordinator> _attempts = [];
    private readonly Dictionary<string, Guid> _instanceAttempts = new(Nexa.Core.PathIdentity.Comparer);

    private async ValueTask<XsrResult> StartConcurrentAsync(string instance, int account, string root, string? server, CancellationToken token, long? expectedAccountRosterRevision = null, long? expectedSettingsRevision = null, string? profileId = null, long? pluginMemoryMiB = null, long? pluginWidth = null, long? pluginHeight = null)
    {
        if (!MinecraftVersionPaths.IsSafeReference(instance) || !Path.IsPathFullyQualified(root))
            return XsrResult.Failure(MinecraftErrors.InvalidRequest("Invalid instance launch target."));
        root = Nexa.Core.PathIdentity.Normalize(root);
        string key = Path.Combine(root, "versions", instance);
        string javaRoot = Nexa.Core.PathIdentity.Comparer.Equals(root, _minecraftRootDirectory) ? _javaRuntimeRootDirectory : Path.Combine(root, "runtime");
        MinecraftLaunchCoordinator child;
        Guid id = Guid.NewGuid();
        lock (_launchGate)
        {
            if (Volatile.Read(ref _launchDisposed) != 0) return XsrResult.Failure(MinecraftErrors.InvalidRequest("Launcher is shutting down."));
            if (_instanceAttempts.ContainsKey(key)) return XsrResult.Failure(MinecraftErrors.LaunchAlreadyActive());
            if (_attempts.Count >= 16) return XsrResult.Failure(MinecraftErrors.InvalidRequest("At most 16 game instances may be launched at once."));
            child = new(root, javaRoot, _instances, _accounts, _settings, _javaSelection, _javaInstaller, _executor,
                _platform, _log, _progress?.CreateAttempt(id), _identityResolver, _launcherVersion, _windowProbe, _authlib, _gameWindowAppeared, _fileCompletion,
                preflight: _preflight)
            {
                _attemptChild = true,
                _expectedAccountRosterRevision = expectedAccountRosterRevision,
                _expectedSettingsRevision = expectedSettingsRevision,
                _authorizedProfileId = profileId,
                _pluginMemoryMiB = pluginMemoryMiB,
                _pluginWidth = pluginWidth,
                _pluginHeight = pluginHeight,
                _attemptId = id,
                _safeLaunchRequired = Volatile.Read(ref _safeLaunchRequired),
                WorkScheduler = WorkScheduler,
                JavaRegistrations = JavaRegistrations,
                JavaRegistrationPort = JavaRegistrationPort,
                SettingsPolicies = SettingsPolicies,
                SettingsSnapshots = SettingsSnapshots,
                BeforeProcessStart = BeforeProcessStart
            };
            child._sessionEnded = _ => RetireAttempt(id, key, child);
            _attempts.Add(id, child); _instanceAttempts.Add(key, id);
        }
        bool succeeded = false;
        try
        {
            var result = await child.StartCoreAsync(instance, account, root, server, token).ConfigureAwait(false);
            succeeded = result.IsSuccess; return result;
        }
        finally { if (!succeeded) RetireAttempt(id, key, child); }
    }

    private void RetireAttempt(Guid id, string key, MinecraftLaunchCoordinator child)
    {
        lock (_launchGate)
        {
            if (!_attempts.Remove(id)) return;
            if (_instanceAttempts.GetValueOrDefault(key) == id) _instanceAttempts.Remove(key);
        }
        child.Dispose();
    }
    private MinecraftLaunchCoordinator? FindAttempt(Guid? id, bool acquisition)
    {
        lock (_launchGate)
        {
            if (id is { } target) return _attempts.GetValueOrDefault(target);
            var pending = _attempts.Values.Where(c => acquisition ? c._acquisitionDecision is not null : c._activeLaunch is not null).ToArray();
            return pending.FirstOrDefault(c => c._attemptId == _progress?.FocusedAttempt) ?? pending.FirstOrDefault();
        }
    }
    private void DisposeAttempts()
    {
        MinecraftLaunchCoordinator[] children;
        lock (_launchGate) { children = _attempts.Values.ToArray(); _attempts.Clear(); _instanceAttempts.Clear(); }
        foreach (var child in children) { child.CancelActiveLaunch(); child.Dispose(); }
    }
}
