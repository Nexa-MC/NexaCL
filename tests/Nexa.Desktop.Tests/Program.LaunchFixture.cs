using Nexa.Desktop.Ui;
using Nexa.Services.Accounts;
using Nexa.Services.Composition;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;
using Nexa.Services.Tasks;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{

    private sealed class LaunchPageFixture : IDisposable
    {
        private readonly string _temporaryDirectory;
        private IDisposable? _launchObserverSubscription;
        private readonly bool _ownsMinecraftRuntime;

        public LaunchPageFixture(
            IMinecraftInstanceSource source,
            MinecraftRuntime? minecraft = null,
            bool addProfile = false,
            bool ownsMinecraftRuntime = true,
            HttpClient? accountHttp = null,
            IMicrosoftMinecraftAuthService? microsoft = null,
            IAccountUiEffects? accountEffects = null,
            LegacyProfileImport? imports = null,
            AccountOnboardingOptions? accountOptions = null,
            bool enableSkins = false,
            TimeProvider? timeProvider = null,
            IVersionDirectoryEffects? directoryEffects = null,
            IInstallCatalogSource? installSource = null,
            IXsrStateObserver? stateObserver = null)
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "nexa-desktop-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
            XsrUiRuntimeContext uiRuntime = new();
            XsrCompositeStateObserver storeObservation = new(uiRuntime.StateBridge, stateObserver);
            SettingsSchema schema = LauncherDefaults.CreateSchema();
            string minecraftRoot = Path.Combine(_temporaryDirectory, "minecraft");
            Directory.CreateDirectory(minecraftRoot);
            FoundationHost host = FoundationComposer.Compose(
                new LauncherSettingsJsonPort(Path.Combine(_temporaryDirectory, "settings.json"), schema),
                schema, new LaunchProfileFilePort(Path.Combine(_temporaryDirectory, "profiles.json")),
                observer: storeObservation, declareHostState: LaunchPageState.DeclareState,
                minecraftRootDirectory: minecraftRoot);
            Foundation = FoundationRuntimeComposer.Compose(host);
            Onboarding = AccountOnboardingRuntimeComposer.Compose(host, accountHttp,
                accountOptions ?? new AccountOnboardingOptions("fixture-client", null),
                imports ?? new LegacyProfileImport(() => []), microsoft: microsoft);
            Store = host.StateStore;
            Intents = new DesktopUiIntentSink();
            Shell = NxmlShellComposer.Compose(Store, uiRuntime, intentSink: Intents);
            Feedback = new DesktopFeedbackService(timeProvider);
            FeedbackPresenter = new DesktopFeedbackPresenter(Shell, Intents, Feedback, Store, timeProvider);
            Minecraft = minecraft ?? MinecraftRuntimeComposer.Compose();
            _ownsMinecraftRuntime = minecraft is null || ownsMinecraftRuntime;
            Service = host.Accounts;
            if (addProfile)
            {
                AssertTrue(Service.AddProfile(new LaunchProfile
                {
                    Username = "Player",
                    Kind = LaunchProfileKind.Offline,
                }).IsSuccess);
            }

            Library = MinecraftLibraryRuntimeComposer.Compose(host, minecraftRoot, source);
            InstallCatalog = InstallCatalogRuntimeComposer.Compose(host, installSource ?? new FixtureInstallSource());
            Controller = new LaunchPageController(
                Shell,
                Intents,
                Minecraft,
                Foundation.Commands,
                Store,
                Library,
                Feedback, accountCommands: enableSkins ? Onboarding.Commands : null,
                timeProvider: timeProvider, directoryEffects: directoryEffects, installCatalogCommands: InstallCatalog.Commands, installCatalogQueries: InstallCatalog.Queries, recoveryQueries: Foundation.Queries);
            AccountForm = new AccountFormController(Shell, Intents, Onboarding.Commands, Store,
                Controller.AccountBody, Feedback, accountEffects, host.Logging);
            _launchObserverSubscription = storeObservation.Subscribe(Controller.StateObserver);
            Controller.Attach();
        }

        public InstallCatalogRuntime InstallCatalog { get; }
        public XsrUiShell Shell { get; }
        public string TemporaryDirectory => _temporaryDirectory;
        public FoundationRuntime Foundation { get; }
        public DesktopUiIntentSink Intents { get; }
        public XsrStateStore Store { get; }
        public MinecraftRuntime Minecraft { get; }
        public MinecraftLibraryRuntime Library { get; }
        public AccountService Service { get; }
        public DesktopFeedbackService Feedback { get; }
        public DesktopFeedbackPresenter FeedbackPresenter { get; }
        public LaunchPageController Controller { get; }
        public AccountOnboardingRuntime Onboarding { get; }
        public AccountFormController AccountForm { get; }

        public void Dispose()
        {
            AccountForm.Dispose();
            Onboarding.Dispose();
            _launchObserverSubscription?.Dispose();
            Controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
            InstallCatalog.Dispose();
            Library.Dispose();
            FeedbackPresenter.Dispose();
            Feedback.Dispose();
            if (_ownsMinecraftRuntime)
            {
                Minecraft.Dispose();
            }

            Foundation.Host.Dispose();
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private sealed class ImmediateInstanceSource(IReadOnlyList<MinecraftInstanceDescriptor> instances)
        : IMinecraftInstanceSource
    {
        public ValueTask<IReadOnlyList<MinecraftInstanceDescriptor>> DiscoverAsync(
            string minecraftRootDirectory, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(instances);
    }

    private sealed class ControllableInstanceSource : IMinecraftInstanceSource
    {
        private readonly object _gate = new();
        private readonly List<TaskCompletionSource<XsrResult<IReadOnlyList<MinecraftInstanceDescriptor>>>> _requests = [];
        private readonly List<CancellationToken> _tokens = [];
        private int _returnedCount;

        public int Count { get { lock (_gate) return _requests.Count; } }
        public int ReturnedCount => Volatile.Read(ref _returnedCount);

        public async ValueTask<IReadOnlyList<MinecraftInstanceDescriptor>> DiscoverAsync(
            string minecraftRootDirectory, CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<XsrResult<IReadOnlyList<MinecraftInstanceDescriptor>>> request =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _requests.Add(request);
                _tokens.Add(cancellationToken);
            }

            XsrResult<IReadOnlyList<MinecraftInstanceDescriptor>> result = await request.Task.ConfigureAwait(false);
            _ = Interlocked.Increment(ref _returnedCount);
            return result.Value;
        }

        public CancellationToken TokenAt(int index)
        {
            lock (_gate)
            {
                return _tokens[index];
            }
        }

        public void Complete(int index, IReadOnlyList<MinecraftInstanceDescriptor> instances)
        {
            TaskCompletionSource<XsrResult<IReadOnlyList<MinecraftInstanceDescriptor>>> request;
            lock (_gate)
            {
                request = _requests[index];
            }

            request.SetResult(XsrResult.Success(instances));
        }
    }

    private sealed class RecordingStartRoute
    {
        public MinecraftStartCommand? LastCommand { get; private set; }

        public XsrResult Outcome { get; set; } = XsrResult.Success();

        /// <summary>
        /// Keeps the launch pipeline in flight: success now closes the launching page
        /// immediately, so mid-launch narration tests must not complete the route.
        /// </summary>
        public bool Hang { get; set; }

        public bool? LastDecision { get; set; }
        public int? LastJavaMajor { get; set; }
        public Guid? LastCancelledSession { get; set; }

        public XsrStateStore? ProgressStore { get; set; }

        public string? Stage { get; set; }

        public double StageProgress { get; set; } = -1d;

        public string? Method { get; set; }

        public ValueTask<XsrResult> Handle(
            MinecraftStartCommand command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            if (ProgressStore is not null && !string.IsNullOrWhiteSpace(Stage))
            {
                try
                {
                    // The derived cells are read-only projections: the test publishes the
                    // coherent snapshot truth, exactly like the real pipeline publisher.
                    ProgressStore.Publish(
                        ProgressStore.Resolve(MinecraftLaunchProgressState.SnapshotKey),
                        new MinecraftLaunchProgressSnapshot(
                            Active: true,
                            Stage: Stage,
                            Progress: StageProgress < 0 ? 0d : StageProgress,
                            Method: Method ?? string.Empty,
                            DownloadSpeed: string.Empty,
                            IsLaunched: false,
                            SessionId: null),
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    Console.WriteLine("[diag] recording handler threw: " + exception);
                    throw;
                }
            }

            if (Hang)
            {
                return new ValueTask<XsrResult>(_hang.Task.WaitAsync(cancellationToken));
            }

            return ValueTask.FromResult(Outcome);
        }

        private readonly TaskCompletionSource<XsrResult> _hang = new();
    }

    private sealed class NoopDispatchObserver : IXsrDispatchObserver
    {
        public void OnCompleted(XsrDispatchObservation observation)
        {
        }
    }
}
