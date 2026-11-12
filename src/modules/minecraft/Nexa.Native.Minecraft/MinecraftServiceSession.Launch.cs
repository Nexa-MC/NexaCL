using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private MinecraftLaunchCoordinator? _launch;
    private MinecraftLaunchExecutor? _launchExecutor;
    private JvmHostService? _jvmHost;
    private MinecraftLaunchFileCompletion? _completion;
    private AuthlibInjectorProvider? _authlib;
    private HttpClient? _launchHttp;
    private LaunchPreflightGate? _preflight;
    private readonly MinecraftLaunchPlanDiagnostics _launchDiagnostics = new();
    private readonly object _finalizationGate = new();
    private void PublishFinalization()
    {
        lock (_finalizationGate) _store.Publish(_store.Resolve(MinecraftLaunchProgressState.FinalizationKey), _launchExecutor?.HasPendingFinalization == true);
    }
    private async Task<byte[]> ExecuteLaunchAsync(int operation, ServiceWireReader reader, CancellationToken token)
    {
        using var writer = new ServiceWireWriter();
        if (operation == MinecraftServiceProtocol.LaunchConfigure)
        {
            string root = reader.String() ?? throw new InvalidDataException("Missing Minecraft launch root.");
            string version = reader.String() ?? "2.0.0"; reader.End();
            if (!Path.IsPathFullyQualified(root) || version.Length > 128) throw new InvalidDataException("Invalid launch configuration.");
            await _initialization.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing), this);
                if (_launch is not null) throw new InvalidOperationException("Minecraft launch is already configured.");
                _launchHttp = CreateInstallationHttp(true); _launchHttp.Timeout = TimeSpan.FromMinutes(2);
                _authlib = new(_launchHttp, _sessionDownloads);
                _completion = new(_sessionDownloads) { Transport = _launchHttp, SettingsPolicies = _settings };
                Remediations.BindJava(_javaLocator!, _javaInstaller!, _javaRuntimeRoot!, _completion);
                _preflight = new(_store, EvaluatePreflightAsync);
                _jvmHost = new(_processes!, recovery: new InstanceRecoveryService(_settings, _store) { WorkScheduler = _work }) { ObservationPort = new Observations(this) };
                _launchExecutor = new(_jvmHost) { Diagnostics = _launchDiagnostics };
                _launchExecutor.FinalizationChanged += PublishFinalization;
                PublishFinalization();
                _launch = new(root, _javaRuntimeRoot!, _instances!, new RemoteMinecraftAccounts(_endpoint), new CapturedSettingsValues([]),
                    new JavaSelectionService(_javaLocator!), _javaInstaller!, _launchExecutor,
                    progress: new MinecraftLaunchProgressPublisher(_store), launcherVersion: version,
                    windowProbe: new RemoteMinecraftWindow(_endpoint, _publications), authlib: _authlib, fileCompletion: _completion, preflight: _preflight)
                { SettingsSnapshots = _settings, SettingsPolicies = _settings, WorkScheduler = _work, JavaRegistrationPort = new RemoteJavaRegistry(_endpoint), BeforeProcessStart = BeforeProcessStartAsync };
            }
            finally { _initialization.Release(); }
            return writer.Finish();
        }
        if (operation == MinecraftServiceProtocol.LaunchDiagnostics)
        {
            string directory = reader.String() ?? ""; reader.End(); writer.MinecraftLaunchPlanDiagnosticSnapshot(_launchDiagnostics.Read(new(directory))); return writer.Finish();
        }
        if (operation == MinecraftServiceProtocol.LaunchFinalize)
        {
            reader.End(); if (_launchExecutor is { } executor) await executor.WaitForFinalizationAsync(token).ConfigureAwait(false); PublishFinalization(); return writer.Finish();
        }
        var launch = _launch ?? throw new InvalidOperationException("Minecraft launch is not configured.");
        switch (operation)
        {
            case MinecraftServiceProtocol.LaunchAttemptControl:
                {
                    int action = reader.Int32(); Guid attempt = reader.Guid();
                    switch (action)
                    {
                        case 0: reader.Boolean(); reader.End(); writer.Boolean(launch.CancelActiveLaunch(attempt)); break;
                        case 1: bool accepted = reader.Boolean(); reader.End(); writer.Boolean(launch.DecideJavaAcquisition(accepted, attempt)); break;
                        case 2: int versionMajor = reader.Int32(); reader.End(); writer.XsrError((await launch.SelectJavaVersionAsync(versionMajor, attempt, token).ConfigureAwait(false)).Error); break;
                        case 3: string executable = reader.String() ?? ""; reader.End(); writer.XsrError((await launch.SelectJavaAsync(executable, attempt, token).ConfigureAwait(false)).Error); break;
                        default: throw new InvalidDataException("Invalid launch attempt operation.");
                    }
                    break;
                }
            case MinecraftServiceProtocol.LaunchContributed:
                {
                    var command = reader.MinecraftStartCommand() ?? throw new InvalidDataException("Missing contributed launch request."); reader.End();
                    if (command.ExpectedAccountRosterRevision is not { } roster || command.MinecraftRootDirectory is not { } root) throw new InvalidDataException("Missing launch authority.");
                    writer.XsrError((await launch.StartContributedAsync(command.InstanceId, command.AccountIndex, root, command.ServerAddress, roster,
                        command.ExpectedSettingsRevision, command.LaunchProfileId, command.PluginMemoryMiB, command.PluginWidth, command.PluginHeight, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.LaunchAuthorized:
                {
                    string instance = reader.String() ?? ""; int account = reader.Int32(); string root = reader.String() ?? ""; string? address = reader.String();
                    long revision = reader.Int64(); long? settingsRevision = reader.Boolean() ? reader.Int64() : null; string? profileId = reader.String(); reader.End();
                    writer.XsrError((await launch.StartAuthorizedAsync(instance, account, root, address, revision, settingsRevision, profileId, token).ConfigureAwait(false)).Error); break;
                }
            case MinecraftServiceProtocol.LaunchStart:
                string startInstance = reader.String() ?? ""; int startAccount = reader.Int32(); string startRoot = reader.String() ?? "";
                string? server = reader.String(); reader.End();
                var started = server is null ? await launch.StartAsync(startInstance, startAccount, startRoot, token).ConfigureAwait(false)
                    : await launch.StartServerAsync(startInstance, startAccount, startRoot, server, token).ConfigureAwait(false);
                writer.XsrError(started.Error); break;
            case MinecraftServiceProtocol.LaunchCancel:
                reader.End(); writer.Boolean(launch.CancelActiveLaunch()); break;
            case MinecraftServiceProtocol.LaunchJavaApprove:
                bool approve = reader.Boolean(); reader.End(); writer.Boolean(launch.DecideJavaAcquisition(approve)); break;
            case MinecraftServiceProtocol.LaunchJavaMajor:
                int major = reader.Int32(); reader.End(); writer.XsrError((await launch.SelectJavaVersionAsync(major, token).ConfigureAwait(false)).Error); break;
            case MinecraftServiceProtocol.LaunchJavaPath:
                string path = reader.String() ?? ""; reader.End(); writer.XsrError((await launch.SelectJavaAsync(path, token).ConfigureAwait(false)).Error); break;
            case MinecraftServiceProtocol.LaunchPreflightDecide:
                var decision = reader.LaunchPreflightDecision() ?? throw new InvalidDataException("Missing preflight decision."); reader.End(); writer.Boolean(_preflight!.Decide(decision)); break;
            case MinecraftServiceProtocol.LaunchSafe:
                bool required = reader.Boolean(); reader.End(); writer.XsrError(launch.SetSafeLaunchSession(new(required)).Error); break;
            case MinecraftServiceProtocol.LaunchSafeRead:
                reader.End(); writer.MinecraftSafeLaunchSessionSnapshot(launch.ReadSafeLaunchSession(new())); break;
            default: throw new InvalidDataException("Unknown Minecraft launch operation.");
        }
        return writer.Finish();
    }
}
