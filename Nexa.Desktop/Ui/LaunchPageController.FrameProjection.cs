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

internal sealed partial class LaunchPageController
{
    private void OnFramePreparing(object? sender, EventArgs e)
    {
        string copyStamp = HomeCopy("account.title");
        bool homeRefresh = Interlocked.Exchange(ref _homeRefreshRequested, 0) == 1;
        if (copyStamp != _homeCopyStamp || homeRefresh)
        {
            _homeCopyStamp = copyStamp;
            ApplyLaunchHomePresentation();
        }
        bool quiet = _store.TryResolve(Nexa.Services.Scheduling.WorkSchedulingContract.QuietKey, out var quietId)
            && _store.ReadAppliedValue(quietId) is Nexa.Services.Scheduling.WorkQuietSnapshot { IsQuiet: true };
        bool active = !_store.TryResolve(XsrUiShellWindowState.Activity, out var activityId)
            || _store.ReadAppliedValue(activityId) is not XsrUiWindowActivity activity
            || activity is { IsActive: true, IsMinimized: false };
        _shell.Renderer.OptionalMotionSuspended = !active;
        bool hints = active && !quiet && !LaunchBusy && _shell.Stage.Navigation.Current == _launchPage
            && _shell.Tree.GetComponent<XsrUiPager>(_pageEntities["LaunchWidgetPager"])!.PageIndex == 1;
        lock (_hintGate)
        {
            if (!_disposed && _hintTimerRunning != hints)
            {
                _hintTimerRunning = hints;
                _ = _hintTimer?.Change(hints ? TimeSpan.FromSeconds(3) : Timeout.InfiniteTimeSpan,
                    hints ? TimeSpan.FromSeconds(3) : Timeout.InfiniteTimeSpan);
            }
        }
        bool busyChanged = _presentedLaunchBusy != LaunchBusy;
        if (busyChanged) { _presentedLaunchBusy = LaunchBusy; UpdateLaunchButton(); }
        var currentPage = _shell.Stage.Navigation.Current;
        bool pageChanged = _observedPage != currentPage;
        if (pageChanged)
        {
            if (_observedPage == _javaInstallPage) ResetInstallSelection();
            _observedPage = currentPage;
            UpdateTitleBar();
        }
        RefreshWidgetPresentation();
        bool installLocaleChanged = RefreshInstallLocalization();
        bool completed = _installEditRead?.IsCompleted ?? false;
        bool queryChanged = _observedEditRead != _installEditRead || _editReadCompleted != completed;
        _observedEditRead = _installEditRead; _editReadCompleted = completed;
        bool draftChanged = false;
        if (currentPage == _javaInstallPage)
        {
            string draft = _shell.Tree.GetComponent<XsrUiTextInput>(_javaInstallEntities["JavaInstallVersionInput"])!.ReadDraft();
            double offset = _shell.Tree.GetComponent<XsrUiScroll>(_javaInstallEntities[_activeJavaInstallPage])!.OffsetY;
            draftChanged = draft != _observedInstallDraft || offset != _observedInstallOffset
                || _shell.Renderer.Viewport != _observedViewport;
            _observedInstallDraft = draft; _observedInstallOffset = offset; _observedViewport = _shell.Renderer.Viewport;
        }
        bool wake = _projections.Consume() || queryChanged || pageChanged || busyChanged || draftChanged || installLocaleChanged;
        if (Interlocked.Exchange(ref _pendingCloseLaunching, 0) == 1) { CloseLaunchingPage(); wake = true; }
        if (!wake) return;
        ProjectionPasses++;
        ProjectInstallEditor();
        ProjectProcessFeedback();
        Publish(LaunchPageState.LaunchingVisibleKey, LaunchBusy && currentPage != _launchingPage);
        ProjectLibrary();
        RefreshJavaInstallPresentation();
        ProjectInstallCatalog();
        ProjectInstallEditPlan();
        RefreshAccountPresentation();
    }

    private void RefreshWidgetPresentation()
    {
        ReloadLaunchHomeWorkspace();
        XsrUiPager pager = _shell.Tree.GetComponent<XsrUiPager>(_pageEntities["LaunchWidgetPager"])!;
        int index = pager.PageIndex;
        if (index != _presentedWidgetIndex)
        {
            _presentedWidgetIndex = index;
            Publish(LaunchPageState.WidgetAboutLabelKey, HomeCopy(index == 0 ? "widget.about.current" : "widget.about.open"));
            Publish(LaunchPageState.WidgetTriviaLabelKey, HomeCopy(index == 1 ? "widget.trivia.current" : "widget.trivia.open"));
            Publish(LaunchPageState.WidgetEchoLabelKey, HomeCopy(index == 2 ? "widget.echo.current" : "widget.echo.open"));
        }
        double position = Math.Clamp(pager.Position, 0, 2);
        if (position == _indicatorPosition) return;
        _indicatorPosition = position;
        ReloadWidgetDot("About", Math.Max(0, 1 - position));
        ReloadWidgetDot("Trivia", Math.Max(0, 1 - Math.Abs(position - 1)));
        ReloadWidgetDot("Echo", Math.Max(0, position - 1));
    }

    internal XsrUiEntityId VersionSettingsPage
    {
        set => BindBorrowedSubpage(ref _versionSettingsPage, value);
    }
    internal XsrUiEntityId WardrobePage
    {
        set => BindBorrowedSubpage(ref _wardrobePage, value);
    }
    internal XsrUiEntityId BedrockInstallPage
    {
        set { _shell.Tree.Destroy(_bedrockInstallPage); _bedrockInstallPage = value; }
    }
    internal XsrUiEntityId SettingsPage { get; set; }
    internal XsrUiEntityId ResourcesPage { get; set; }


    private int SelectedAccountIndex => _store.ReadAppliedValue(_accountSelectedId) is int index ? index : -1;

    private void Publish<T>(XsrSemanticId key, T value)
    {
        XsrStateId id = _store.Resolve(key);
        if (!Equals(_store.ReadAppliedValue(id), value)) _store.Publish(id, value);
    }

    private string ReadCell(XsrSemanticId key) =>
        _store.Read<string>(_store.Resolve(key)).Value ?? string.Empty;

    private (XsrUiEntityId Page, Dictionary<string, XsrUiEntityId> Entities) LoadLaunchPage()
    {
        XsrUiEntityId page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("LaunchPage.nxml"), _shell.Tree, _store);
        var entities = HomeNodes(page);
        _homeWorkspace = HomeDynamic.Append(entities["LaunchHomeWorkspaceSlot"],
            DesktopMarkup.Dynamic("LaunchInstallHomeWorkspace.d.nxml", HomeWorkspaceData()));
        foreach (var node in HomeNodes(_homeWorkspace)) entities[node.Key] = node.Value;
        foreach (string name in new[] { "About", "Trivia", "Echo" })
        {
            var root = HomeDynamic.Append(entities["Widget" + name + "Slot"],
                DesktopMarkup.Dynamic("LaunchInstallHomeWidget" + name + ".d.nxml", WidgetDotData(name == "About" ? 1 : 0)));
            _homeWidgetDots[name] = root;
            foreach (var node in HomeNodes(root)) entities[node.Key] = node.Value;
        }
        return (page, entities);
    }
}
