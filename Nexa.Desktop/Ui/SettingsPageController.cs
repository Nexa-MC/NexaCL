using System.Collections.Concurrent;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>Settings presentation only. Value validation, availability and writes use sealed Service routes.</summary>
internal sealed partial class SettingsPageController : IDisposable
{
    private static readonly XsrSemanticId Select = XsrSemanticId.Parse("ui.settings.section");
    private static readonly XsrSemanticId Edit = XsrSemanticId.Parse("ui.settings.edit");
    private static readonly XsrSemanticId Choice = XsrSemanticId.Parse("ui.settings.choice");
    private static readonly XsrSemanticId Inherit = XsrSemanticId.Parse("ui.settings.inherit");
    private readonly Func<string?>? _instanceDirectory;
    private string? _instance;
    private readonly Dictionary<XsrUiEntityId, string> _inheritButtons = [];
    private IReadOnlyList<SettingsCatalogPage> Pages => _instanceDirectory is null ? _catalog!.GlobalPages : ManagementPages;
    private readonly XsrUiShell _shell;
    private readonly NxmlDynamicContent _dynamic;
    private readonly DesktopUiIntentSink _intents;
    private readonly XsrQueryRouter _queries;
    private readonly XsrCommandRouter _commands;
    private readonly XsrStateStore _store;
    private readonly Nexa.Services.RegionalPolicy _region;
    private readonly DesktopFeedbackService _feedback;
    private readonly ConcurrentQueue<DesktopUiIntent> _pending = new();
    private readonly Dictionary<XsrUiEntityId, string> _navigation = [];
    private readonly Dictionary<XsrUiEntityId, Editor> _editors = [];
    private readonly Dictionary<XsrUiEntityId, (Editor Editor, string Value)> _choices = [];
    private readonly Dictionary<string, double> _scrollPositions = [];
    private readonly XsrUiEntityId _navigationRoot, _pager, _saveStatus;
    private XsrUiEntityId _sections;
    private readonly Dictionary<string, XsrUiEntityId> _pages = [];
    private Task<XsrResult<SettingsCatalogSnapshot>>? _catalogReading;
    private readonly XsrStateId _revisionId;
    private SettingsCatalogSnapshot? _catalog;
    private SettingsEffectiveSnapshot? _values;
    private Task<XsrResult<SettingsEffectiveSnapshot>>? _reading;
    private Task<XsrResult>? _writing;
    private string _selected = "general";
    private bool _developer, _disposed;
    private bool _visible;
    private long _revision = -1;
    private sealed record Editor(SettingsCatalogEntry Entry, XsrUiEntityId Input, XsrUiEntityId Button, XsrUiEntityId Thumb);
    private static readonly XsrUiColor Ink = DesktopMarkup.Visual("palette-SettingsPageController-Ink").Foreground, Muted = DesktopMarkup.Visual("palette-SettingsPageController-Muted").Foreground, Blue = DesktopMarkup.Visual("palette-SettingsPageController-Blue").Foreground, White = DesktopMarkup.Visual("palette-SettingsPageController-White").Foreground, Line = DesktopMarkup.Visual("palette-SettingsPageController-Line").Foreground;

    public SettingsPageController(XsrUiShell shell, DesktopUiIntentSink intents, XsrQueryRouter queries, XsrCommandRouter commands,
        XsrStateStore store, DesktopFeedbackService feedback, Func<string?>? instanceDirectory = null,
        Nexa.Services.RegionalPolicy? regionalPolicy = null)
    {
        _shell = shell; _intents = intents; _queries = queries; _commands = commands; _store = store; _feedback = feedback;
        _region = regionalPolicy ?? Nexa.Services.RegionalPolicy.Current;
        _dynamic = new(shell.Tree, store);
        _instanceDirectory = instanceDirectory;
        if (instanceDirectory is not null) _selected = "overview";
        _revisionId = store.Resolve(SettingsPolicyContract.RevisionKey);
        Page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load(instanceDirectory is null ? "SettingsPage.nxml" : "VersionSettingsPage.nxml"), shell.Tree, store);
        var names = new Dictionary<string, XsrUiEntityId>();
        shell.Tree.Walk(Page, entity => { names[shell.Tree.Name(entity)] = entity; return true; });
        _navigationRoot = names["SettingsNavigation"]; _pager = names["SettingsPager"];
        _saveStatus = _dynamic.Append(names["SettingsSaveStatusSlot"], DesktopMarkup.Dynamic("SettingsSaveStatus.d.nxml",
            new Dictionary<string, string> { ["message"] = "" }));
        _intents.IntentEmitted += OnIntent;
        _shell.Renderer.FramePreparing += OnFrame;
    }

    internal XsrUiEntityId Page { get; }
    internal string SelectedSection => _selected;
    internal bool TelemetryRequired { get; set; }

    private void OnIntent(object? sender, DesktopUiIntentEventArgs args)
    {
        if (_shell.Stage.Navigation.Current != Page) return;
        if (args.Intent.Command == ThemeAction || IsContentSelectionIntent(args.Intent.Command) || IsRuntimeTraceIntent(args.Intent.Command) || IsRuntimeDiagnosticsIntent(args.Intent.Command) || args.Intent.Command == IdentityAction || args.Intent.Command == NetworkProbeStart || args.Intent.Command == NetworkTraceRefresh || args.Intent.Command == ContentWorkspaceAction || args.Intent.Command == LaunchProfileAction || IsDeveloperDiagnosticsIntent(args.Intent.Command) || IsHookIntent(args.Intent.Command) || IsProxyIntent(args.Intent.Command) || IsStoragePreferencesIntent(args.Intent.Command) || IsJavaInventoryIntent(args.Intent.Command) || IsSettingsTransferIntent(args.Intent.Command) || IsUpdateIntent(args.Intent.Command) || args.Intent.Command == ScreenshotPreviewAction || args.Intent.Command == ManagementAction || args.Intent.Command == Inherit || args.Intent.Command == Select || args.Intent.Command == Edit || args.Intent.Command == Choice || args.Intent.Command == ArgumentAdd || args.Intent.Command == ArgumentRemove || args.Intent.Command == RefreshPlatform) _pending.Enqueue(args.Intent);
        else if (args.Intent.Command == RemediationExecuted) OnPlatformRemediation(sender, args);
    }
    private void OnFrame(object? sender, EventArgs args)
    {
        if (_disposed) return;
        bool visible = _shell.Stage.Navigation.Current.Equals(Page);
        if (visible != _visible)
        {
            if (!visible)
            {
                RetireThemeScope();
                CancelContentIntegrity();
                CancelIdentityRead(); CancelManagementRead(); CancelOfflineReadiness(); CancelWorldSnapshots(); CancelOnlineContent(); CancelModRemovalPreview(); CancelSettingsTransfer(); CancelJavaInventory(); CancelDiskLogExport(); CancelDiagnosticExport(); CancelStoragePreferences(); CancelContentWorkspace();
                ReleaseContentGraph();
                RetireHardwareAdvice(); RetireDeveloperDiagnostics(); RetireRuntimeDiagnostics(); RetireRuntimeTrace(); CancelNetworkProbe(); CancelAdvancedWorkspace();
            }
            _visible = visible;
            if (visible && _catalog is not null && DeveloperDiagnosticsVisible) BuildSections();
            _shell.Tree.MarkDirty(_shell.Content, XsrUiDirtyKinds.Layout);
        }
        bool deferFactUpdates = visible && !_pending.IsEmpty;
        if (!deferFactUpdates) UpdateIndependentFacts();
        if (!visible) return;
        string? instance = _instanceDirectory?.Invoke();
        if (_instanceDirectory is not null && string.IsNullOrWhiteSpace(instance))
        {
            CancelContentIntegrity();
            bool hadInstance = _instance is not null;
            PresentSaveStatus("");
            CancelSettingsTransfer(); CancelJavaInventory();
            CancelIdentityRead(); CancelManagementRead(); CancelOfflineReadiness(); CancelWorldSnapshots();
            CancelOnlineContent(); CancelModRemovalPreview(); CancelInstanceDiagnostics();
            CancelAdvancedLaunchDiagnostics(); ReleaseContentGraph();
            _pending.Clear(); _instance = null; _values = null; _reading = null; _revision = -1;
            if (hadInstance)
            {
                _selected = "overview";
                if (_pages.TryGetValue(_selected, out var overview)) _sections = overview;
                UpdateNavigation();
            }
            if (hadInstance && _catalog is not null) BuildSections();
            return;
        }
        if (instance != _instance)
        {
            CancelContentIntegrity();
            CancelSettingsTransfer(); CancelJavaInventory();
            PresentSaveStatus("");
            _instance = instance; _reading = null; _values = null; _revision = -1;
            ResetManagement();
            if (_catalog is not null) BuildSections(navigating: true);
        }
        if (!deferFactUpdates)
        {
            if (_instanceDirectory is null) UpdatePlatformCapabilities();
            UpdateHardwareAdvice();
        }
        if (_catalog is null)
        {
            if (!_queries.TryResolve(SettingsPolicyContract.CatalogQuery, out var route)) return;
            _catalogReading ??= _queries.QueryAsync<SettingsCatalogQuery, SettingsCatalogSnapshot>(route, new(true)).AsTask();
            if (!_catalogReading.IsCompleted) return;
            if (!new PendingQuery<SettingsCatalogSnapshot>(_catalogReading).TryRead(out var result)) { _catalogReading = null; return; }
            _catalogReading = null;
            if (!result.IsSuccess) return;
            _catalog = result.Value!;
            BuildNavigation(); BuildSections();
        }
        if (_writing is { IsCompleted: true } completedWrite)
        {
            PresentSaveStatus(completedWrite.IsCompletedSuccessfully && completedWrite.Result.IsSuccess ? CatalogCopy("catalog.copy.1bd91a7d0c53c936") : CatalogCopy("catalog.copy.7bfb3516d712be1e"));
            _writing = null;
        }
        long revision = _store.Read<long>(_revisionId).Value;
        if (_reading is null && _revision != revision && _queries.TryResolve(SettingsPolicyContract.EffectiveQuery, out var read))
            _reading = _queries.QueryAsync<SettingsEffectiveQuery, SettingsEffectiveSnapshot>(read, new(_instance)).AsTask();
        if (_reading is { IsCompleted: true } reading)
        {
            _reading = null;
            if (PendingQuery.Succeeded(reading))
            {
                if (_instanceDirectory is not null && _selected == "recovery" && _revision >= 0 && _revision != reading.Result.Value!.Revision)
                {
                    CancelManagementRead(); CancelOfflineReadiness(); CancelWorldSnapshots();
                }
                bool previousDevelopmentMode = InstanceDevelopmentMode;
                _values = reading.Result.Value!; _revision = _values.Revision;
                if (previousDevelopmentMode != InstanceDevelopmentMode) RebuildDevelopmentMode();
                bool developer = _values.Values.First(item => item.Key == "developer.enabled").Value.Value == "true";
                if (developer != _developer) { _developer = developer; BuildSections(); }
                UpdateEditors();
            }
            else { _revision = revision; _feedback.Error(CatalogCopy("catalog.copy.bb5313044acd31b3")); }
        }
        UpdateHookInheritance();

        UpdateLaunchProfileAdmission();
        while (_pending.TryDequeue(out var intent))
        {
            if (intent.Command == ThemeAction) { HandleThemeIntent(intent); continue; }
            if (IsContentSelectionIntent(intent.Command)) { HandleContentSelectionIntent(intent); continue; }
            if (intent.Command == IdentityAction && _identityWrite is null && _identityActions.TryGetValue(intent.Source, out var identityAction)) { identityAction(); continue; }
            if (intent.Command == LaunchProfileAction) { HandleLaunchProfileIntent(intent); continue; }
            if (IsDeveloperDiagnosticsIntent(intent.Command)) { HandleDeveloperDiagnosticsIntent(intent); continue; }
            if (IsHookIntent(intent.Command)) { HandleHookIntent(intent); continue; }
            if (IsProxyIntent(intent.Command)) { HandleProxyIntent(intent); continue; }
            if (IsRuntimeDiagnosticsIntent(intent.Command)) { HandleRuntimeDiagnosticsIntent(intent); continue; }
            if (IsRuntimeTraceIntent(intent.Command)) { HandleRuntimeTraceIntent(intent); continue; }
            if (intent.Command == NetworkProbeStart) { StartNetworkProbe(intent); continue; }
            if (intent.Command == NetworkTraceRefresh) { ReadNetworkDiagnostics(); continue; }
            if (intent.Command == ContentWorkspaceAction) { HandleContentWorkspace(intent); continue; }
            if (IsStoragePreferencesIntent(intent.Command)) { HandleStoragePreferences(intent.Command, intent.Source); continue; }
            if (IsJavaInventoryIntent(intent.Command)) { HandleJavaInventory(intent.Command, intent.Source); continue; }
            if (IsSettingsTransferIntent(intent.Command)) { HandleSettingsTransfer(intent.Command, intent.Source); continue; }
            if (intent.Command == ScreenshotPreviewAction) { HandleScreenshotPreviewIntent(intent); continue; }
            if (intent.Command == ManagementAction && _managementActions.TryGetValue(intent.Source, out var action)) { action(); continue; }
            if (IsUpdateIntent(intent.Command)) { HandleUpdateIntent(intent); continue; }
            if (intent.Command == Inherit && _inheritButtons.TryGetValue(intent.Source, out var inheritKey)
                && _writing is null && _commands.TryResolve(SettingsPolicyContract.SetCommand, out var inheritRoute))
            {
                _writing = SaveAsync(inheritRoute, new(inheritKey, SettingsLayer.Instance, new(SettingsOverrideMode.Inherit), _instance));
                TrackHookInheritance(inheritKey, _writing);
                continue;
            }
            if (intent.Command == RefreshPlatform) { RefreshPlatformCapabilities(); continue; }
            if (intent.Command == Select && _navigation.TryGetValue(intent.Source, out string? page) && page != _selected)
            {
                SwitchPage(page);
                _shell.Renderer.SelectPagerPage(_pager, Pages.ToList().FindIndex(item => item.Id == page));
            }
            else if (intent.Command == Edit && _editors.TryGetValue(intent.Source, out var editor) && _writing is null)
            {
                Save(editor);
            }
            else if (intent.Command == ArgumentAdd || intent.Command == ArgumentRemove) HandleArgumentIntent(intent);
            else if (intent.Command == Choice && _choices.TryGetValue(intent.Source, out var choice))
            {
                if (_writing is null)
                {
                    Save(choice.Editor, choice.Value);
                }
            }
        }
        if (_writing is not null) PresentSaveStatus(CatalogCopy("catalog.copy.6bdb4435095e5d28"));
        UpdateLaunchProfiles();
        if (deferFactUpdates)
        {
            UpdateIndependentFacts();
            if (_instanceDirectory is null) UpdatePlatformCapabilities();
            UpdateHardwareAdvice();
        }
        UpdateManagement();
        UpdateRecoveryStorage();
        UpdateOfflineReadiness();
        UpdateWorldSnapshots(); UpdateIdentity();
        UpdateOnlineContent(); UpdateContentIntegrity();
        UpdateModRemovalPreview();
        UpdateContentGraph(); UpdateServers(); UpdateExport();
        UpdateSettingsTransfer(); UpdateJavaInventory();
        UpdateProxyPreferences(); UpdateDiskLogActions(); UpdateDiagnosticExportControls();
        UpdateThemeSettings();
        int index = _shell.Tree.GetComponent<XsrUiPager>(_pager)!.PageIndex;
        if (index >= 0 && index < Pages.Count && Pages[index].Id != _selected)
            SwitchPage(Pages[index].Id);
        UpdateContentWindow();
        var pager = _shell.Tree.GetComponent<XsrUiPager>(_pager)!;
        if (!pager.IsDragging && Math.Abs(pager.Position - pager.PageIndex) < 0.001)
            foreach (var page in _pages.Values)
                if (page != _sections)
                    foreach (var child in _shell.Tree.Children(page).ToArray()) _shell.Tree.Destroy(child);
    }

    private void UpdateIndependentFacts()
    {
        UpdateReleaseCheck(); UpdateStoragePreferences(); UpdateContentWorkspace();
        UpdateNetworkDiagnostics(); UpdateGeneralRuntimeStates(); UpdateRuntimeTrace(); UpdateAdvancedWorkspace();
    }

    private void SwitchPage(string page)
    {
        if (!Pages.Any(item => item.Id == page)) return;
        RetireThemeScope();
        RetireContentScope();
        CancelWorldDetails();
        CloseScreenshotPreview(restoreFocus: false); CancelScreenshotThumbnails();
        CancelContentIntegrity();
        RetireHardwareAdvice(); RetireDeveloperDiagnostics(); RetireRuntimeDiagnostics(); RetireRuntimeTrace(); CancelNetworkProbe(); CancelAdvancedWorkspace();
        CancelIdentityRead(); CancelSettingsTransfer(); CancelJavaInventory(); CancelDiskLogExport(); CancelStoragePreferences(); CancelContentWorkspace();
        CancelOfflineReadiness(); CancelWorldSnapshots(); CancelInstanceDiagnostics(); CancelAdvancedLaunchDiagnostics(); CancelFileWorkspace();
        _scrollPositions[_selected] = _shell.Tree.GetComponent<XsrUiScroll>(_sections)!.OffsetY;
        if (_selected == "contentgraph")
        {
            if (_managementRead is not null) CancelManagementRead();
            ReleaseContentGraph();
        }

        _contentDetail = null; _contentFilter = "";
        _selected = page; _sections = _pages[page];
        if (_instanceDirectory is not null && page == "recovery")
        {
            CancelManagementRead(); CancelOfflineReadiness(); CancelWorldSnapshots();
            if (_management is { } management) _management = management with { RecoveryStorage = null, RecoveryComparison = null };
        }
        if (_instanceDirectory is not null && page == "trash") CancelManagementRead();
        if (_instanceDirectory is not null && page == "contentgraph")
        {
            ResetContentGraph();
            CancelManagementRead(); CancelOfflineReadiness(); CancelWorldSnapshots();
            if (_management is { } management) _management = management with { ContentGraph = null };
        }
        BuildSections(navigating: true); UpdateNavigation(); UpdateEditors();
    }

    private void BuildSections(bool navigating = false)
    {
        if (!navigating && (TryReloadThemeSettings() || TryReloadContentManagement() || TryReloadRecoveryStorage()))
        {
            UpdateEditors();
            return;
        }
        ResetRuntimeDiagnosticControls();
        ResetRuntimeTraceControls();
        ResetInstanceDiagnosticControls();
        CaptureWorkspaceLine();
        ResetInstanceToolControls();
        CaptureLaunchProfileDrafts(navigating);
        var identityDraft = CaptureIdentityDraft(navigating);
        var hookDrafts = CaptureHookDrafts(navigating);
        ResetProxyPreferences(preserveDraft: !navigating);
        string? focus = !navigating && _shell.Tree.IsAlive(_shell.Renderer.Focused) ? _shell.Tree.Name(_shell.Renderer.Focused) : null;
        var drafts = !navigating ? _editors.Values.Where(item => item.Input.IsAssigned).ToDictionary(item => item.Entry.Id,
            item => _shell.Tree.GetComponent<XsrUiTextInput>(item.Input)!.ReadDraft()) : [];
        if (!navigating) _scrollPositions[_selected] = _shell.Tree.GetComponent<XsrUiScroll>(_sections)!.OffsetY;
        _dynamic.Clear(_sections);
        ResetThemeControls();
        ResetDeveloperDiagnosticControls();
        _catalogViews.Clear(); _inheritRows.Clear(); _autoModeViews.Clear();
        _javaChoices.Clear(); _javaChoiceViews.Clear();
        _javaInventoryGroup = default;
        _editors.Clear(); _inheritButtons.Clear(); _selectors.Clear(); _autoModes.Clear(); _argumentEditors.Clear(); _argumentActions.Clear(); _choices.Clear();
        _hookEditors.Clear(); _hookActions.Clear();
        ResetLaunchProfileControls(); ResetIdentityControls();
        _managementActions.Clear(); _diagnosticControls.Clear(); _diagnosticViews.Clear(); _contentSearch = default; _contentList = default; _contentWindowStart = -1;
        _graphSearch = _graphBody = default; _graphActions.Clear();
        if (_instanceDirectory is not null && _instance is null)
        {
            _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsInstanceMissing.d.nxml"));
            return;
        }
        if (_instanceDirectory is not null && _selected != "game")
        {
            BuildManagementSection();
            RestoreIdentityDraft(identityDraft, focus);
            UpdateEditors();
            if (focus == "ManagementContentSearch" && _contentSearch.IsAssigned) _shell.Renderer.Focus(_contentSearch, showIndicator: false);
            return;
        }
        if (_selected == "platform") { BuildPlatformCapabilities(); return; }
        if (_selected == "about") { BuildAboutPage(); return; }
        if (_selected == "advanced") BuildUpdateCard();
        if (_instanceDirectory is null && _selected == "storage") BuildStoragePreferences();
        bool transfer = _instanceDirectory is null && _selected == "storage" || _instanceDirectory is not null && _selected == "game";
        if (_selected == "privacy")
        {
            _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsPrivacyNotice.d.nxml"));
        }
        var entries = _catalog!.Entries.Where(item => item.Scope == "global"
            && (item.Page == _selected || _instanceDirectory is not null && _selected == "game" && item.Page == "java")
            && (_instanceDirectory is null || item.Definition?.InstanceOverride == true) && !item.IsRuntimeDetail && (_developer || !item.DeveloperOnly)).ToArray();
        var available = entries.Where(item => item.Kind == SettingsCatalogEntryKind.Setting
            && (item.Definition is not null || item.Availability != SettingsCapabilityAvailability.Available)
            && (_region.IsMainlandChina || item.SettingKey is not ("network.game-source" or "network.resource-source" or "network.provider-mirror"))
            && item.SettingKey is not ("network.proxy-mode" or "network.proxy-address" or "network.proxy-user" or "network.proxy-password"
                or "appearance.theme-pack-id" or "appearance.launcher-icon-mode" or "appearance.launcher-icon-path")).ToArray();
        if (available.Length == 0 && _selected != "advanced" && !transfer)
        {
            _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsCategoryEmpty.d.nxml", new Dictionary<string, string>
            { ["label"] = Pages.First(page => page.Id == _selected).Label }));
        }
        if (_instanceDirectory is not null && _selected == "game") BuildLaunchProfiles();
        foreach (var section in available.GroupBy(item => (Section: item.DeveloperOnly ? CatalogCopy("catalog.copy.38084d301e3f1a31") : _instanceDirectory is not null ? InstanceSettingGroup(item) : item.Section, item.DeveloperOnly))
            .OrderBy(group => _instanceDirectory is null ? 0 : group.Key.Section switch { var copy when copy == CatalogCopy("catalog.copy.8cdf1da0d4c7e55e") => 0, var copy when copy == CatalogCopy("catalog.copy.2720b4221e5e4ca3") => 1, var copy when copy == CatalogCopy("catalog.copy.2388cbc6a486af9f") => 2, _ => 3 }))
        {
            var cardContent = AppendSettingsForm(_sections, "SettingsGroup." + section.First().Id, DisplayLabel(section.Key.Section));
            var rows = section.ToArray();
            for (int i = 0; i < rows.Length; i++)
            {
                BuildRow(cardContent, rows[i]);
                if (i + 1 < rows.Length)
                    _dynamic.Append(cardContent, DesktopMarkup.Dynamic("SettingsCatalogDivider.d.nxml"));
            }
            if (_instanceDirectory is not null && section.Key.Section == CatalogCopy("catalog.copy.2720b4221e5e4ca3")) BuildJavaInventory(cardContent);
        }
        if (_instanceDirectory is null && _selected == "network") { BuildProxyPreferences(); BuildNetworkDiagnostics(); }
        if (_instanceDirectory is null && _selected == "privacy") { BuildDiskLogActions(); BuildPrivacyDiagnosticExport(); BuildContentTrustStates(); }
        if (_instanceDirectory is null && _selected == "storage") BuildContentWorkspace();
        if (transfer) BuildSettingsTransfer();
        BuildJavaInventory();

        BuildGeneralRuntimeStates();
        BuildDeveloperDiagnostics();
        BuildRuntimeDiagnostics();
        BuildRuntimeTraceDiagnostics();
        BuildAdvancedWorkspace();
        BuildThemeSettings();
        var scroll = _shell.Tree.GetComponent<XsrUiScroll>(_sections)!;
        scroll.OffsetY = _scrollPositions.GetValueOrDefault(_selected);
        _shell.Tree.GetComponent<XsrUiTransition>(_sections)!.Key = _selected + ":" + _developer;
        _shell.Tree.MarkDirty(_sections, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
        UpdateEditors();
        RestoreHookDrafts(hookDrafts);
        foreach (var editor in _editors.Values)
        {
            if (editor.Input.IsAssigned && drafts.TryGetValue(editor.Entry.Id, out var draft)) _shell.Renderer.SetTextInputValue(editor.Input, draft);
            var target = editor.Input.IsAssigned && _shell.Tree.Name(editor.Input) == focus ? editor.Input : _shell.Tree.Name(editor.Button) == focus ? editor.Button : default;
            if (target.IsAssigned) _shell.Renderer.Focus(target);
        }
        if (focus is not null)
            _shell.Tree.Walk(_sections, entity => { if (_shell.Tree.Name(entity) == focus) _shell.Renderer.Focus(entity); return true; });
    }

    private static string? SettingHint(string? key) => key switch
    {
        "general.region" => CatalogCopy("catalog.copy.776e862b6d0e9031"),
        "updates.channel" => CatalogCopy("catalog.copy.eed8e45d95df9f6b"),
        "updates.auto-check" => CatalogCopy("catalog.copy.50cfbc4a9decb781"),
        "diagnostics.log-level" => CatalogCopy("catalog.copy.3fb6e525b2a48120"),
        "diagnostics.log-lines" => CatalogCopy("catalog.copy.ca8c03c6f3e906cf"),
        "diagnostics.disk-log-days" => CatalogCopy("catalog.copy.c30fd87eb5fd49a2"),
        "game.process-priority" => CatalogCopy("catalog.copy.67ff7775ee009846"),
        "game.title" => CatalogCopy("catalog.copy.0b68f43e8196aeff"),
        "game.default-isolation" => CatalogCopy("catalog.copy.d772866356f942b9"),
        "game.launcher-visibility" => CatalogCopy("catalog.copy.43cb3d790f2aa8fb"),
        "game.jvm" => CatalogCopy("catalog.copy.d9363eeb833c6cfc"),
        "install.inherit-vanilla" => CatalogCopy("catalog.copy.8525fed239dc4e36"),
        "game.arguments" => CatalogCopy("catalog.copy.5dc1231308729d53"),
        "game.wrapper" => CatalogCopy("catalog.copy.855b5d4a658b6f02"),
        "game.pre-launch" => CatalogCopy("catalog.copy.b3b5b1c0f702ed1e"),
        "game.pre-launch-wait" => CatalogCopy("catalog.copy.f4896949ed55c37f"),
        "game.memory" => CatalogCopy("catalog.copy.501986da6c15d7d4"),
        "java.auto-install" => CatalogCopy("catalog.copy.b2c48db2fd181406"),
        "java.vendor" => CatalogCopy("catalog.copy.a87cf2ecb1567b67"),
        "network.file-concurrency" => CatalogCopy("catalog.copy.edf875f11bedb518"),
        "network.file-retry" => CatalogCopy("catalog.copy.62c58790dd3a97db"),
        "network.game-source" => CatalogCopy("catalog.copy.487f9059ca0adc09"),
        "network.bandwidth-kib" => CatalogCopy("catalog.copy.84d4da5cad300809"),
        "network.ip-stack" => CatalogCopy("catalog.copy.e6e2c696df92cabf"),
        "network.doh" => CatalogCopy("catalog.copy.a644428ae54619b0"),
        "game.server" => CatalogCopy("catalog.copy.38941425982294bf"),
        "game.auto-repair" => CatalogCopy("catalog.copy.6f4a69151ae79608"),
        "appearance.animations-disabled" => CatalogCopy("catalog.copy.5ca216328e84d4c6"),
        "appearance.animation-fps" => CatalogCopy("catalog.copy.3ead916bdc23c0e6"),
        "appearance.theme-mode" => CatalogCopy("catalog.copy.56b6daeaafec549c"),
        "appearance.accent" => CatalogCopy("catalog.copy.63ca81911d9ec19a"),
        _ => null,
    };

    private void UpdateEditors()
    {
        foreach (var editor in _editors.Values)
        {
            var value = _values?.Values.FirstOrDefault(item => item.Key == editor.Entry.SettingKey);
            if (value is null) continue;
            string raw = value.Value.Value ?? "";
            if (editor.Input.IsAssigned && value.Value.Mode != SettingsOverrideMode.Auto && !_shell.Renderer.Focused.Equals(editor.Input))
                _shell.Renderer.SetTextInputValue(editor.Input, raw);
        }
        foreach (var (button, key) in _inheritButtons)
        {
            var value = _values?.Values.FirstOrDefault(item => item.Key == key);
            string label = value?.Source >= SettingsLayer.Instance ? CatalogCopy("catalog.copy.5bd845d9a77abc81") : CatalogCopy("catalog.copy.6d82d484e54cf457");
            ReloadCatalog(_inheritRows[button], ("inheritLabel", label));
        }
        UpdateNavigationWidths(); UpdateShiftSelectors(); UpdateAutomaticModes(); UpdateArgumentEditors(); UpdateHookEditors();
    }

    private void Save(Editor editor, string? selectedValue = null)
    {
        if (!_region.IsMainlandChina && editor.Entry.SettingKey is ("network.game-source" or "network.resource-source" or "network.provider-mirror")) return;
        if (!_commands.TryResolve(SettingsPolicyContract.SetCommand, out var route) || _values is null) return;
        if (_hookEditors.TryGetValue(editor.Button, out var hook))
        {
            _writing = SaveAsync(route, new(editor.Entry.SettingKey!, _instanceDirectory is null ? SettingsLayer.Global : SettingsLayer.Instance,
                new(SettingsOverrideMode.Custom, ReadHookDraft(hook)), _instance));
            return;
        }
        if (_argumentEditors.TryGetValue(editor.Button, out var arguments))
        {
            _writing = SaveAsync(route, new(editor.Entry.SettingKey!, (_instanceDirectory is null ? SettingsLayer.Global : SettingsLayer.Instance),
                new(SettingsOverrideMode.Custom, string.Join("\n", ReadArgumentDrafts(arguments).Where(value => !string.IsNullOrWhiteSpace(value)))), _instance));
            return;
        }
        var current = _values.Values.First(item => item.Key == editor.Entry.SettingKey).Value.Value;
        if (selectedValue == "__manual__")
        {
            selectedValue = _shell.Tree.GetComponent<XsrUiTextInput>(editor.Input)!.ReadDraft();
            if (string.IsNullOrWhiteSpace(selectedValue))
            {
                if (editor.Entry.Definition!.Kind == SettingsValueKind.Number) selectedValue = editor.Entry.Definition.DefaultValue;
                else { _feedback.Info(CatalogCopy("catalog.copy.ce0001a83383889b")); return; }
            }
        }
        string raw = selectedValue ?? (editor.Input.IsAssigned ? _shell.Tree.GetComponent<XsrUiTextInput>(editor.Input)!.ReadDraft()
            : editor.Entry.Definition!.Kind == SettingsValueKind.Boolean ? (current == "true" ? "false" : "true") : (current == "fullscreen" ? "windowed" : "fullscreen"));
        var value = editor.Entry.Definition!.SupportsAuto && selectedValue == "auto"
            || editor.Entry.SettingKey == "java.runtime" && string.IsNullOrWhiteSpace(raw)
            ? new SettingsOverride(SettingsOverrideMode.Auto) : new(SettingsOverrideMode.Custom, raw);
        _writing = SaveAsync(route, new(editor.Entry.SettingKey!, (_instanceDirectory is null ? SettingsLayer.Global : SettingsLayer.Instance), value, _instance));
    }
    private async Task<XsrResult> SaveAsync(XsrCommandId route, SettingsMutation mutation)
    {
        var result = mutation.Layer == SettingsLayer.Instance && _selected == "game"
            ? await SaveLaunchLayerAsync(mutation).ConfigureAwait(false)
            : await _commands.Dispatch(route, mutation).Completion.ConfigureAwait(false);
        if (!_disposed && !result.IsSuccess)
            _feedback.Error(ProductPresentationText.Format("SettingsSaveFailure", _shell.Renderer.LocalizeText,
                ("error", result.Error?.Message ?? string.Empty)), localizeMessage: false);
        return result;
    }

    private void PresentSaveStatus(string message)
    {
        var text = _shell.Tree.GetComponent<XsrUiText>(_saveStatus)!;
        if (text.Content == message) return;
        _dynamic.Reload(_saveStatus, DesktopMarkup.Dynamic("SettingsSaveStatus.d.nxml", new Dictionary<string, string> { ["message"] = message }));
    }

    private static string DisplayLabel(string label) => label switch
    {
        "[ Off / On ]" => CatalogCopy("catalog.copy.6b02f7e0e085f52a"),
        "Launcher Logo" => CatalogCopy("catalog.copy.36769ec63d49e00b"),
        var copy when copy == CatalogCopy("catalog.copy.9233607fdac6c264") => CatalogCopy("catalog.copy.46f801c4139378ac"),
        "GPU / Renderer" => CatalogCopy("catalog.copy.0fa12acfea2ecfbd"),
        "Game Resource Handoff" => CatalogCopy("catalog.copy.2bab706cf8cc7ab1"),
        var copy when copy == CatalogCopy("catalog.copy.0443f45b075ec245") => CatalogCopy("catalog.copy.ba6b025bb675ecd2"),
        "Launch Preflight" => CatalogCopy("catalog.copy.a0b515fc30208fc1"),
        "Compatibility Check" => CatalogCopy("catalog.copy.a67cd968c4001cb5"),
        var copy when copy == CatalogCopy("catalog.copy.72732d2ac2f6de64") => CatalogCopy("catalog.copy.556a3c13b6874b0c"),
        "JVM Arguments" => CatalogCopy("catalog.copy.7700380bf9bd7354"),
        "Game Arguments" => CatalogCopy("catalog.copy.0eb838056acb6db9"),
        "Wrapper Command" => CatalogCopy("catalog.copy.edff333007c478ce"),
        "Pre-launch Command" => CatalogCopy("catalog.copy.797cebbb03b3e395"),
        "Runtime List" => CatalogCopy("catalog.copy.43f3bda877293c34"),
        "Version" => CatalogCopy("catalog.copy.5f76b2bf82dd2c5d"),
        "Vendor" => CatalogCopy("catalog.copy.fe843aba10f36459"),
        "Architecture" => CatalogCopy("catalog.copy.8b784b6288058b41"),
        "Path" => CatalogCopy("catalog.copy.77e1ea5c5688eea4"),
        "Status" => CatalogCopy("catalog.copy.6320b4a8722a851f"),
        "Snapshot" => CatalogCopy("catalog.copy.699ea2523018c8ac"),
        "Cache" => CatalogCopy("catalog.copy.e3ca04a9cb133d2f"),
        "Portable Mode" => CatalogCopy("catalog.copy.487bc7550da2c9e2"),
        "Cloud Account" => CatalogCopy("catalog.copy.a986f94e5e1e4ee2"),
        "Plan" => CatalogCopy("catalog.copy.40f6d142cebc1585"),
        "Minecraft Options" => CatalogCopy("catalog.copy.8533c8b6c8766c9f"),
        "Server List" => CatalogCopy("catalog.copy.682d9bdda5aff766"),
        "Resource Packs" => CatalogCopy("catalog.copy.6518cf1fcb303a64"),
        "Command History" => CatalogCopy("catalog.copy.6057c11467c0730b"),
        "Creative Hotbars" => CatalogCopy("catalog.copy.16d36cbb4d2b5579"),
        "Screenshots" => CatalogCopy("catalog.copy.c95dc99afe57b285"),
        "Instance Metadata" => CatalogCopy("catalog.copy.92b742d80072559b"),
        "Worlds" => CatalogCopy("catalog.copy.33650a369521ec29"),
        "Config" => CatalogCopy("catalog.copy.1d27f02ed278ebc9"),
        "Thin Backup" => CatalogCopy("catalog.copy.60cc98c271c231cc"),
        "Crash Report" => CatalogCopy("catalog.copy.96cb3d88d4858754"),
        "Diagnostic Data" => CatalogCopy("catalog.copy.7e88429f466fbc40"),
        "Crash Analyzer" => CatalogCopy("catalog.copy.3aaa26113647e845"),
        "Release Notes" => CatalogCopy("catalog.copy.de6811e73d403172"),
        "Signature Verification" => CatalogCopy("catalog.copy.fffd360456a394a1"),
        "Atomic Update" => CatalogCopy("catalog.copy.f0986442c697567a"),
        "Last Known Good" => CatalogCopy("catalog.copy.82f301e2ba0228ab"),
        "Auto Rollback" => CatalogCopy("catalog.copy.7a8bee740f9d0e06"),
        "Launcher Safe Mode" => CatalogCopy("catalog.copy.613fac39aaad0c75"),
        _ => label,
    };
    public void Dispose()
    {
        RetireThemeScope();
        CancelContentIntegrity();
        RetireHardwareAdvice(); RetireDeveloperDiagnostics(); RetireRuntimeDiagnostics(); RetireRuntimeTrace(); CancelNetworkProbe(); CancelAdvancedWorkspace();
        _disposed = true; CancelIdentityRead(); CancelSettingsTransfer(); CancelJavaInventory(); CancelDiskLogExport(); CancelDiagnosticExport(); CancelStoragePreferences(); CancelContentWorkspace(); CancelManagementRead(); CancelOfflineReadiness(); CancelWorldSnapshots(); CancelOnlineContent(); CancelModRemovalPreview(); _updateStop.Cancel(); _updateCheckStop?.Dispose(); _intents.IntentEmitted -= OnIntent; _shell.Renderer.FramePreparing -= OnFrame;
    }
}
