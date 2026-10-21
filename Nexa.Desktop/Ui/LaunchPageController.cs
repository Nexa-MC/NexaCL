using System.Globalization;
using Nexa.Services.Accounts;
using Nexa.Services.Composition;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Launch;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>
/// The product launch page: the first vertical slice attached to the shell's content host,
/// replicating the legacy experimental launch home's information architecture — an account
/// card (profile identity / picker), a version card (版本 header, instance picker row,
/// the big accent launch button), and the community about card. It reads its facts from host
/// state cells, routes one-shot outcomes to the shared feedback service, emits the launch intent,
/// and dispatches the product-level Minecraft start command through the composed runtime routers.
/// Navigation intents open explicitly composed pages; unknown or unbound entries are rejected.
/// </summary>
internal sealed partial class LaunchPageController : IDisposable, IAsyncDisposable
{
    private static readonly XsrSemanticId LaunchRoute = XsrSemanticId.Parse("ui.navigation.launch");
    private static readonly XsrSemanticId InstallRoute = XsrSemanticId.Parse("ui.navigation.download");

    private static readonly XsrSemanticId LaunchPrimaryCommand = XsrSemanticId.Parse("ui.launch.primary");

    private static readonly XsrSemanticId LaunchInstancesCommand = XsrSemanticId.Parse("ui.launch.instances");
    private static readonly XsrSemanticId LaunchSettingsCommand = XsrSemanticId.Parse("ui.launch.settings");
    private static readonly XsrSemanticId LaunchModifyCommand = XsrSemanticId.Parse("ui.launch.modify");
    private static readonly XsrSemanticId PageBackCommand = XsrSemanticId.Parse("ui.page.back");
    private static readonly XsrSemanticId WidgetAboutCommand = XsrSemanticId.Parse("ui.launch.widget.about");
    private static readonly XsrSemanticId WidgetTriviaCommand = XsrSemanticId.Parse("ui.launch.widget.trivia");
    private static readonly XsrSemanticId WidgetEchoCommand = XsrSemanticId.Parse("ui.launch.widget.echo");
    private static readonly XsrSemanticId WidgetHintCommand = XsrSemanticId.Parse("ui.launch.hint.refresh");

    private static readonly XsrSemanticId AccountSelectCommand = XsrSemanticId.Parse("ui.account.select");
    private static readonly XsrSemanticId AccountDeleteCommand = XsrSemanticId.Parse("ui.account.delete");
    private static readonly XsrSemanticId AccountSwitchCommand = XsrSemanticId.Parse("ui.account.switch");
    private static readonly XsrSemanticId AccountWardrobeCommand = XsrSemanticId.Parse("ui.account.wardrobe");
    private static readonly XsrSemanticId AccountDismissCommand = XsrSemanticId.Parse("ui.account.dismiss");
    private static readonly XsrSemanticId LaunchCancelCommand = XsrSemanticId.Parse("ui.launch.cancel");

    private static readonly XsrSemanticId InstallJavaCommand = XsrSemanticId.Parse("ui.install.java");
    private static readonly XsrSemanticId InstallBedrockCommand = XsrSemanticId.Parse("ui.install.bedrock");
    private static readonly XsrSemanticId InstallMinecraftPageCommand = XsrSemanticId.Parse("ui.install.page.minecraft");
    private static readonly XsrSemanticId InstallForgePageCommand = XsrSemanticId.Parse("ui.install.page.forge");
    private static readonly XsrSemanticId InstallCleanroomPageCommand = XsrSemanticId.Parse("ui.install.page.cleanroom");
    private static readonly XsrSemanticId InstallNeoForgePageCommand = XsrSemanticId.Parse("ui.install.page.neoforge");
    private static readonly XsrSemanticId InstallFabricPageCommand = XsrSemanticId.Parse("ui.install.page.fabric");
    private static readonly XsrSemanticId InstallLegacyFabricPageCommand = XsrSemanticId.Parse("ui.install.page.legacy-fabric");
    private static readonly XsrSemanticId InstallFabricApiPageCommand = XsrSemanticId.Parse("ui.install.page.fabric-api");
    private static readonly XsrSemanticId InstallQuiltPageCommand = XsrSemanticId.Parse("ui.install.page.quilt");
    private static readonly XsrSemanticId InstallQslPageCommand = XsrSemanticId.Parse("ui.install.page.qsl");
    private static readonly XsrSemanticId InstallLabyModPageCommand = XsrSemanticId.Parse("ui.install.page.labymod");
    private static readonly XsrSemanticId InstallOptiFinePageCommand = XsrSemanticId.Parse("ui.install.page.optifine");
    private static readonly XsrSemanticId InstallLiteLoaderPageCommand = XsrSemanticId.Parse("ui.install.page.liteloader");
    private static readonly XsrSemanticId InstallStartCommand = XsrSemanticId.Parse("ui.install.start");
    private static readonly XsrSemanticId InstallVersion1211Command = XsrSemanticId.Parse("ui.install.version.1.21.1");
    private static readonly XsrSemanticId InstallVersion1206Command = XsrSemanticId.Parse("ui.install.version.1.20.6");
    private static readonly XsrSemanticId InstallVersion1201Command = XsrSemanticId.Parse("ui.install.version.1.20.1");
    private static readonly XsrSemanticId InstallLoaderFabricCommand = XsrSemanticId.Parse("ui.install.loader.fabric");
    // No visible button emits this any more, but the catalog flow keeps it as the explicit
    // "back to vanilla" selection reset (version picks and tests still drive it).
    private static readonly XsrSemanticId InstallLoaderVanillaCommand = XsrSemanticId.Parse("ui.install.loader.vanilla");
    private static readonly XsrSemanticId InstallLoaderForgeCommand = XsrSemanticId.Parse("ui.install.loader.forge");
    private static readonly XsrSemanticId InstallLoaderNeoForgeCommand = XsrSemanticId.Parse("ui.install.loader.neoforge");
    private static readonly XsrSemanticId InstallLoaderQuiltCommand = XsrSemanticId.Parse("ui.install.loader.quilt");
    private static readonly XsrSemanticId InstallLoaderOptiFineCommand = XsrSemanticId.Parse("ui.install.loader.optifine");
    private static readonly XsrSemanticId InstallLoaderCleanroomCommand = XsrSemanticId.Parse("ui.install.loader.cleanroom");
    private static readonly XsrSemanticId InstallLoaderLiteLoaderCommand = XsrSemanticId.Parse("ui.install.loader.liteloader");
    private static readonly XsrSemanticId InstallLoaderLegacyFabricCommand = XsrSemanticId.Parse("ui.install.loader.legacy-fabric");
    private static readonly XsrSemanticId InstallLoaderLabyModCommand = XsrSemanticId.Parse("ui.install.loader.labymod");
    private static readonly XsrSemanticId InstallFabricApiAddonCommand = XsrSemanticId.Parse("ui.install.addon.fabric-api");
    private static readonly XsrSemanticId InstallQslAddonCommand = XsrSemanticId.Parse("ui.install.addon.qsl");

    private static readonly XsrSemanticId DownloadNavigationId = XsrSemanticId.Parse("navigation.download");

    private static readonly JavaInstallSubpage[] JavaInstallSubpages =
    [
        new("JavaMinecraftPage", "JavaMinecraftTab", InstallMinecraftPageCommand),
        new("JavaForgePage", "JavaForgeTab", InstallForgePageCommand, "Forge"),
        new("JavaCleanroomPage", "JavaCleanroomTab", InstallCleanroomPageCommand, "Cleanroom"),
        new("JavaNeoForgePage", "JavaNeoForgeTab", InstallNeoForgePageCommand, "NeoForge"),
        new("JavaFabricPage", "JavaFabricTab", InstallFabricPageCommand, "Fabric"),
        new("JavaLegacyFabricPage", "JavaLegacyFabricTab", InstallLegacyFabricPageCommand, "Legacy Fabric"),
        new("JavaFabricApiPage", "JavaFabricApiTab", InstallFabricApiPageCommand, RequiresLoader: "Fabric"),
        new("JavaOptiFabricPage", "JavaOptiFabricTab", XsrSemanticId.Parse("ui.install.page.optifabric"), RequiresLoader: "Fabric"),
        new("JavaQuiltPage", "JavaQuiltTab", InstallQuiltPageCommand, "Quilt"),
        new("JavaQslPage", "JavaQslTab", InstallQslPageCommand, RequiresLoader: "Quilt"),
        new("JavaLabyModPage", "JavaLabyModTab", InstallLabyModPageCommand, "LabyMod"),
        new("JavaOptiFinePage", "JavaOptiFineTab", InstallOptiFinePageCommand, "OptiFine"),
        new("JavaLiteLoaderPage", "JavaLiteLoaderTab", InstallLiteLoaderPageCommand, "LiteLoader"),
    ];

    private static readonly string[] JavaInstallLoaderKeys =
    [
        "JavaLoaderForge", "JavaLoaderCleanroom", "JavaLoaderNeoForge", "JavaLoaderFabric",
        "JavaLoaderLegacyFabric", "JavaLoaderQuilt", "JavaLoaderLabyMod", "JavaLoaderOptiFine", "JavaLoaderLiteLoader",
    ];

    private Task _launchRequest = Task.CompletedTask;
    private MinecraftLaunchProgressSnapshot LaunchSnapshot =>
        _store.ReadAppliedValue(_launchProgressId) as MinecraftLaunchProgressSnapshot
        ?? MinecraftLaunchProgressSnapshot.Empty;
    private bool LaunchBusy => !_launchRequest.IsCompleted || LaunchSnapshot is { Active: true, IsLaunched: false };
    private int _pendingCloseLaunching;
    private Guid? _javaAcquisitionDialog;
    private Guid? _preflightDialog;
    private Guid? _preflightAttempt;
    private int _pickingJava;
    private bool _launchingViaKeyboard;
    private XsrUiEntityId _launchingPage;
    private Dictionary<string, XsrUiEntityId> _launchingEntities = [];

    /// <summary>The composition root attaches this observer to the shared store fan-out.</summary>
    public IXsrStateObserver StateObserver { get; }
    private readonly XsrUiShell _shell;
    private readonly DesktopUiIntentSink _intents;
    private readonly MinecraftRuntime _minecraft;
    private readonly XsrCommandRouter _foundationCommands;
    private readonly XsrCommandRouter? _accountCommands;
    private long _skinRevision = -1;
    private readonly XsrStateStore _store;
    private readonly DesktopFeedbackService _feedback;
    private readonly XsrCommandRouter _libraryCommands;
    internal VersionSelectionController Versions { get; }
    private readonly XsrUiEntityId _launchPage;
    private readonly XsrUiEntityId _versionListPage;
    private XsrUiEntityId _versionSettingsPage;
    private XsrUiEntityId _wardrobePage;
    private readonly XsrUiEntityId _installPage;
    private readonly XsrUiEntityId _javaInstallPage;
    private XsrUiEntityId _bedrockInstallPage;
    private readonly Dictionary<string, XsrUiEntityId> _javaInstallEntities;
    private int _presentedJavaInstallPage = -1;
    private string _activeJavaInstallPage = "JavaMinecraftPage";
    private readonly Dictionary<string, XsrUiEntityId> _titleEntities = [];
    private XsrUiEntityId _titleTransitionPage;
    private int _titleTransitionEpoch;
    private int _titleNavigationDepth = 1;
    private readonly Stack<XsrUiEntityId> _returnFocus = [];
    private readonly Dictionary<string, XsrUiEntityId> _pageEntities;
    private readonly Dictionary<int, XsrUiEntityId> _accountRowEntities = [];
    private readonly Dictionary<XsrUiEntityId, int> _accountRowIndexes = [];
    private long _accountRosterRevision = -1;
    private int _presentedAccountIndex = -2;
    private bool? _presentedAccountPicker;
    private bool _accountKeyboardFocus;
    private int _presentedWidgetIndex = -1;
    private double _indicatorPosition = double.NaN;
    private int _hintIndex = Random.Shared.Next(LaunchWidgetHints.BuiltIn.Count);
    private readonly object _hintGate = new();
    private readonly TimeProvider _timeProvider;
    private readonly Nexa.Services.RegionalPolicy _region;
    private ITimer? _hintTimer;
    private bool _hintTimerRunning;
    private readonly object _refreshGate = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Task _refreshTask = Task.CompletedTask;
    private Task _shutdownTask = Task.CompletedTask;
    private bool _attached;
    private bool _disposed;

    public LaunchPageController(
        XsrUiShell shell,
        DesktopUiIntentSink intents,
        MinecraftRuntime minecraft,
        XsrCommandRouter foundationCommands,
        XsrStateStore store,
        MinecraftLibraryRuntime library,
        DesktopFeedbackService feedback,
        XsrCommandRouter? accountCommands = null,
        TimeProvider? timeProvider = null,
        IVersionDirectoryEffects? directoryEffects = null,
        XsrCommandRouter? installCatalogCommands = null, XsrQueryRouter? installCatalogQueries = null,
        XsrCommandRouter? installRunCommands = null, XsrQueryRouter? recoveryQueries = null,
        Nexa.Services.RegionalPolicy? regionalPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(minecraft);
        ArgumentNullException.ThrowIfNull(foundationCommands);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(feedback);
        _shell = shell;
        _intents = intents;
        _minecraft = minecraft;
        _foundationCommands = foundationCommands;
        _accountCommands = accountCommands;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _region = regionalPolicy ?? Nexa.Services.RegionalPolicy.Current;
        _installCatalogCommands = installCatalogCommands;
        _installCatalogQueries = installCatalogQueries;
        _installRunCommands = installRunCommands;
        _recoveryQueries = recoveryQueries;
        _store = store;
        _launchProgressId = _store.Resolve(MinecraftLaunchProgressState.SnapshotKey);
        _accountProfilesId = _store.Resolve(AccountStateContract.ProfilesKey);
        _accountSelectedId = _store.Resolve(AccountStateContract.SelectedKey);
        _libraryId = _store.Resolve(MinecraftLibraryContract.StateKey);
        _installCatalogId = _store.Resolve(InstallCatalogStateContract.StateKey);
        _projections = new UiProjectionSignal(_store, _intents);

        _feedback = feedback;
        StateObserver = new LaunchingStateObserver(this);
        _libraryCommands = library.Commands;
        (_launchPage, _pageEntities) = LoadLaunchPage();
        Versions = new VersionSelectionController(shell, intents, library.Commands, store, feedback, directoryEffects);
        _versionListPage = Versions.Page;
        (_installPage, _) = LoadInstallPage();
        (_javaInstallPage, _javaInstallEntities) = LoadJavaInstallPage();
        InitializeInstallCatalog();
        UpdateJavaInstallSubpageVisibility();
        _bedrockInstallPage = LoadBedrockInstallPage();
        InitializeJavaLauncherGuide();
        (_launchingPage, _launchingEntities) = LoadLaunchingPage();
        _shell.Tree.Walk(_shell.TitleBar, entity =>
        {
            _titleEntities[_shell.Tree.Name(entity)] = entity;
            return true;
        });
    }

    /// <summary>Subscribes to renderer intents and shows the initial launch page.</summary>
    public void Attach()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_attached)
        {
            return;
        }

        _attached = true;
        _intents.IntentEmitted += OnIntentEmitted;
        _shell.Renderer.FramePreparing += OnFramePreparing;
        _shell.StyleChanged += OnShellStyleChanged;
        ShowLaunch();
        Publish(LaunchPageState.ProfileNameKey, HomeCopy("account.none"));
        Publish(LaunchPageState.InstanceSummaryKey, HomeCopy("launch.scanning"));
        Publish(LaunchPageState.SelectedInstanceKey, string.Empty);
        Publish(LaunchPageState.ActionLabelKey, HomeCopy("launch.download"));
        RefreshAccountPresentation();
        if (_store.TryResolve(XsrSemanticId.Parse("UiLaunchWidgetPage"), out XsrStateId widgetPage)
            && _store.ReadAppliedValue(widgetPage) is int savedPage)
            _shell.Renderer.RebasePagerPage(_pageEntities["LaunchWidgetPager"], Math.Clamp(savedPage, 0, 2));
        RefreshWidgetPresentation();
        Publish(LaunchPageState.WidgetHintKey, LaunchWidgetHints.BuiltIn[_hintIndex]);
        _hintTimer = _timeProvider.CreateTimer(_ => AdvanceHint(automatic: true), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _ = QueueRefresh();
    }

    /// <summary>Completes when the in-flight instance scan has published its facts.</summary>
    public Task WaitUntilIdle()
    {
        lock (_refreshGate)
        {
            return Task.WhenAll(_refreshTask, _installCatalogTask, _installPrefetchTask, _installEditRead ?? Task.CompletedTask);
        }
    }

    internal XsrUiEntityId AccountBody => _pageEntities["AccountBody"];

    /// <summary>
    /// Re-queries the installed instances and re-commits the version card facts. Exposed for
    /// tests so the asynchronous scan can be awaited deterministically.
    /// </summary>
    public Task RefreshInstancesAsync() => QueueRefresh();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Task save = Task.CompletedTask;
        if (_foundationCommands.TryResolve(FoundationRouteIds.SettingsSet, out XsrCommandId saveWidget))
            save = _foundationCommands.Dispatch(saveWidget, new SettingsSetCommand("UiLaunchWidgetPage",
                _shell.Tree.GetComponent<XsrUiPager>(_pageEntities["LaunchWidgetPager"])!.PageIndex.ToString(CultureInfo.InvariantCulture))).Completion;
        lock (_hintGate) { _disposed = true; _hintTimerRunning = false; _hintTimer?.Dispose(); }
        _projections.Dispose();
        if (_attached)
        {
            _intents.IntentEmitted -= OnIntentEmitted;
            _shell.Renderer.FramePreparing -= OnFramePreparing;
            _shell.StyleChanged -= OnShellStyleChanged;
            _attached = false;
        }

        _lifetimeCancellation.Cancel();
        Versions.Dispose();
        _processLogPage?.Dispose();
        _processLayer?.Dispose();
        _processDocks.Clear();
        if (_javaChoicePage.IsAssigned) _shell.Tree.Destroy(_javaChoicePage);
        if (_javaLauncherGuidePage.IsAssigned) _shell.Tree.Destroy(_javaLauncherGuidePage);
        DismissAcquisitionDialog();

        _shutdownTask = CompleteShutdownAsync(save);
    }

    private async Task CompleteShutdownAsync(Task save)
    {
        try { await Task.WhenAll(save, WaitUntilIdle(), _launchRequest).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        finally { _lifetimeCancellation.Dispose(); }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(_shutdownTask);
    }

    private Task QueueRefresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_refreshGate)
        {
            _refreshTask = RefreshLibraryAsync();
            return _refreshTask;
        }
    }

    private async Task RefreshLibraryAsync()
    {
        if (!_libraryCommands.TryResolve(MinecraftLibraryRoutes.Refresh, out XsrCommandId id)) return;
        _ = await _libraryCommands.Dispatch(id, new MinecraftLibraryRefreshCommand(), cancellationToken: _lifetimeCancellation.Token).Completion.ConfigureAwait(false);
        ProjectLibrary();
    }

    private void ProjectLibrary()
    {
        if (_disposed || _store.ReadAppliedValue(_libraryId) is not MinecraftLibrarySnapshot snapshot) return;
        Publish(LaunchPageState.SelectedInstanceKey, snapshot.SelectedInstance?.Id ?? "");
        Publish(LaunchPageState.InstanceSummaryKey, snapshot.SelectedInstance is { } selected
            ? string.IsNullOrWhiteSpace(selected.Metadata.DisplayName) ? selected.Id : selected.Metadata.DisplayName
            : snapshot.IsProvisional ? HomeCopy("launch.validating-cache") : snapshot.IsLoading ? HomeCopy("launch.scanning") : HomeCopy("launch.no-instances"));
        Publish(LaunchPageState.InstanceDirectoryKey, snapshot.RootDirectory);
        UpdateLaunchButton();
    }

    /// <summary>
    /// Publishes primary-action facts only. Downloading does not require an account; launching
    /// requires a selected profile. Safe from an instance worker as well as the render thread.
    /// </summary>
    private void UpdateLaunchButton()
    {
        bool hasProfile = ReadProfiles().Any(profile => profile.Index == SelectedAccountIndex);
        bool hasInstance = !string.IsNullOrWhiteSpace(ReadCell(LaunchPageState.SelectedInstanceKey));
        string label;
        bool enabled;
        if (_store.ReadAppliedValue(_libraryId) is MinecraftLibrarySnapshot { IsProvisional: true })
        {
            label = HomeCopy("launch.provisional");
            enabled = false;
        }
        else if (!hasInstance)
        {
            label = HomeCopy("launch.download");
            enabled = true;
        }
        else if (!hasProfile)
        {
            label = HomeCopy("launch.no-profile");
            enabled = false;
        }
        else if (!SelectedProfileCanLaunch())
        {
            // The account capability logs these kinds in successfully, so the action says
            // honestly that they cannot start a game yet. UpdateLaunchButton is a per-frame
            // projection: it must stay side-effect free — a toast here would re-raise the
            // feedback Changed event every frame and spin a render loop forever.
            label = HomeCopy("launch.unsupported");
            enabled = false;
        }
        else
        {
            label = HomeCopy("launch.start");
            enabled = true;
        }

        bool busy = LaunchBusy && (LaunchSnapshot.InstanceId is null || LaunchSnapshot.InstanceId == ReadCell(LaunchPageState.SelectedInstanceKey))
            && (LaunchSnapshot.MinecraftRootDirectory is null || Nexa.Core.PathIdentity.Comparer.Equals(LaunchSnapshot.MinecraftRootDirectory, ReadCell(LaunchPageState.InstanceDirectoryKey)));
        if (busy) { label = HomeCopy("launch.busy-action"); enabled = false; }
        Publish(LaunchPageState.ActionBusyKey, busy);
        Publish(LaunchPageState.ActionLabelKey, label);
        Publish(LaunchPageState.ActionEnabledKey, enabled);
        Publish(LaunchPageState.InstanceAvailableKey, hasInstance);
    }

    private bool SelectedProfileCanLaunch()
    {
        LaunchProfileView? profile = ReadProfiles().FirstOrDefault(candidate => candidate.Index == SelectedAccountIndex);
        return profile is not { } selected
            || selected.Kind is LaunchProfileKind.Offline or LaunchProfileKind.Microsoft or LaunchProfileKind.LittleSkin or LaunchProfileKind.ThirdParty;
    }

    private readonly UiProjectionSignal _projections;
    private readonly XsrStateId _launchProgressId;
    private readonly XsrStateId _accountProfilesId;
    private readonly XsrStateId _accountSelectedId;
    private readonly XsrStateId _libraryId;
    private readonly XsrStateId _installCatalogId;
    private XsrUiEntityId _observedPage;
    private Task? _observedEditRead;
    private bool _editReadCompleted;
    private string? _observedInstallDraft;
    private double _observedInstallOffset;
    private XsrUiSize _observedViewport;
    private bool _presentedLaunchBusy;
    internal int ProjectionPasses { get; private set; }
}
