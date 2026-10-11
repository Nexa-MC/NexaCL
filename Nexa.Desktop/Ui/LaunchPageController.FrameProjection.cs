using System.Globalization;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Services.Accounts;
using Nexa.Services.Composition;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;

using Nexa.Services.Minecraft.Launch;

using Nexa.Xsr.Renderer;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{
    private void OnFramePreparing(object? sender, EventArgs e)
    {
        bool quiet = _store.TryResolve(Nexa.Services.Scheduling.WorkSchedulingContract.QuietKey, out var quietId)
            && _store.ReadAppliedValue(quietId) is Nexa.Services.Scheduling.WorkQuietSnapshot { IsQuiet: true };
        bool active = !_store.TryResolve(XsrUiShellWindowState.Activity, out var activityId)
            || _store.ReadAppliedValue(activityId) is not XsrUiWindowActivity activity
            || activity is { IsActive: true, IsMinimized: false };
        _shell.Renderer.OptionalMotionSuspended = quiet || !active;
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
        bool wake = _projections.Consume() || queryChanged || pageChanged || busyChanged || draftChanged;
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
        XsrUiPager pager = _shell.Tree.GetComponent<XsrUiPager>(_pageEntities["LaunchWidgetPager"])!;
        int index = pager.PageIndex;
        if (index != _presentedWidgetIndex)
        {
            _presentedWidgetIndex = index;
            Publish(LaunchPageState.WidgetAboutLabelKey, index == 0 ? "关于 Nexa，当前卡片" : "查看关于 Nexa");
            Publish(LaunchPageState.WidgetTriviaLabelKey, index == 1 ? "你知道吗，当前卡片" : "查看你知道吗");
            Publish(LaunchPageState.WidgetEchoLabelKey, index == 2 ? "回声洞，当前卡片" : "查看回声洞");
        }
        double position = Math.Clamp(pager.Position, 0, 2);
        if (position == _indicatorPosition) return;
        _indicatorPosition = position;
        UpdateWidgetDot("WidgetAboutDot", "WidgetAboutIndicator", Math.Max(0, 1 - position));
        UpdateWidgetDot("WidgetTriviaDot", "WidgetTriviaIndicator", Math.Max(0, 1 - Math.Abs(position - 1)));
        UpdateWidgetDot("WidgetEchoDot", "WidgetEchoIndicator", Math.Max(0, position - 1));
    }

    private void UpdateWidgetDot(string key, string buttonKey, double activation)
    {
        XsrUiEntityId entity = _pageEntities[key];
        _shell.Tree.GetComponent<XsrUiElement>(entity)!.Height = 6 + 10 * activation;
        XsrUiEntityId button = _pageEntities[buttonKey];
        _shell.Tree.GetComponent<XsrUiElement>(button)!.Height = 6 + 10 * activation;
        _shell.Tree.GetComponent<XsrUiVisualStyle>(entity)!.Background = new XsrUiColor(11, 91, 203,
            (byte)Math.Round(64 + 191 * activation));
        _shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
        _shell.Tree.MarkDirty(button, XsrUiDirtyKinds.Layout);
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
        NxmlDocument document = NxmlParser.Parse(ReadEmbeddedResource("Ui.LaunchPage.nxml"));
        NxmlHostIr ir = DesktopMarkup.Compile(document);
        XsrUiEntityId host = _shell.Tree.Create("launch-page-host");
        XsrUiEntityId page = NxmlUiLoader.Load(ir, _shell.Tree, _store, host);
        _shell.Tree.Detach(page);
        _shell.Tree.Destroy(host);

        Dictionary<string, XsrUiEntityId> entities = [];
        _shell.Tree.Walk(
            page,
            entity =>
            {
                string key = _shell.Tree.Name(entity);
                if (key.Length > 0)
                {
                    entities[key] = entity;
                }

                return true;
            });
        StyleLaunchPage(page, entities);
        return (page, entities);
    }
}
