using System.Globalization;
using Nexa.Services.Accounts;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{
    private NxmlDynamicContent? _homeDynamic;
    private NxmlDynamicContent HomeDynamic => _homeDynamic ??= new(_shell.Tree, _store);
    private XsrUiEntityId _homeWorkspace;
    private XsrUiEntityId _homeLaunchingWorkspace;
    private string _homeAvatarSource = LaunchProfilePresentation.Avatar(string.Empty);
    private string _homeCopyStamp = string.Empty;
    private int _homeRefreshRequested;
    private bool _homeLaunchingHintVisible = true;
    private readonly Dictionary<string, XsrUiEntityId> _homeWidgetDots = [];
    private readonly Dictionary<int, LaunchProfileView> _homeProfiles = [];
    private readonly Dictionary<int, XsrUiEntityId> _homeProfileAvatars = [];
    private readonly Dictionary<XsrUiEntityId, Dictionary<string, string>> _homeProjections = [];

    private sealed record HomeWidgetMetrics(double InactiveHeight, double ActiveHeight,
        XsrUiColor InactivePaint, XsrUiColor ActivePaint);
    private static readonly Lazy<HomeWidgetMetrics> HomeMetrics = new(() =>
    {
        var nodes = DesktopMarkup.Load("LaunchInstallHomeMetrics.nxml").Root.Children.ToDictionary(node => node.Key!);
        var inactive = nodes["WidgetInactive"];
        var active = nodes["WidgetActive"];
        if (inactive.Height is not > 0 || active.Height is not > 0 || active.Height.GetValueOrDefault() < inactive.Height.GetValueOrDefault() || inactive.Visual is null || active.Visual is null)
            throw new NxmlCompileException("Launch widget metrics are incomplete.");
        return new(inactive.Height.Value, active.Height!.Value, inactive.Visual.Value.Background, active.Visual.Value.Background);
    });
    private static readonly Lazy<Dictionary<string, string>> HomeTerms = new(() =>
    {
        var nodes = DesktopMarkup.Load("LaunchInstallHomeTerms.nxml").Root.Children;
        if (nodes.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
            || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("Launch home terms must be keyed literal text.");
        return nodes.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });

    private string? HomeFind(string key) => HomeTerms.Value.TryGetValue(key, out var value) ? _shell.Renderer.LocalizeText(value) : null;
    private string HomeCopy(string key) => _shell.Renderer.LocalizeText(HomeTerms.Value[key]);
    private string HomeFormat(string name, Dictionary<string, string> data)
    {
        var node = DesktopMarkup.Dynamic("LaunchInstallHomeText" + name + ".d.nxml", data,
            _shell.Renderer.LocalizeText).Root;
        if (node.Recipe != NxmlRuntimeRecipe.Text || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0)
            throw new NxmlCompileException("Launch home formats must produce literal text only.");
        return node.Content;
    }

    private Dictionary<string, XsrUiEntityId> HomeNodes(XsrUiEntityId root)
    {
        Dictionary<string, XsrUiEntityId> result = [];
        _shell.Tree.Walk(root, entity => { result[_shell.Tree.Name(entity)] = entity; return true; });
        return result;
    }

    private Dictionary<string, string> HomeWorkspaceData() => new()
    {
        ["avatar"] = _homeAvatarSource,
    };

    private void ReloadLaunchHomeWorkspace()
    {
        if (!_homeWorkspace.IsAssigned) return;
        ReloadHome(_homeWorkspace, "LaunchInstallHomeWorkspace.d.nxml", HomeWorkspaceData());
    }

    private void ReloadHome(XsrUiEntityId root, string asset, Dictionary<string, string> data)
    {
        if (_homeProjections.TryGetValue(root, out var before) && before.Count == data.Count
            && before.All(pair => data.GetValueOrDefault(pair.Key) == pair.Value)) return;
        HomeDynamic.Reload(root, DesktopMarkup.Dynamic(asset, data, _shell.Renderer.LocalizeText));
        _homeProjections[root] = data;
    }

    private static Dictionary<string, string> WidgetDotData(double activation)
    {
        var metrics = HomeMetrics.Value;
        static byte Blend(byte from, byte to, double value) => (byte)Math.Round(from + (to - from) * value);
        byte red = Blend(metrics.InactivePaint.Red, metrics.ActivePaint.Red, activation);
        byte green = Blend(metrics.InactivePaint.Green, metrics.ActivePaint.Green, activation);
        byte blue = Blend(metrics.InactivePaint.Blue, metrics.ActivePaint.Blue, activation);
        byte alpha = Blend(metrics.InactivePaint.Alpha, metrics.ActivePaint.Alpha, activation);
        return new()
        {
            ["height"] = (metrics.InactiveHeight + (metrics.ActiveHeight - metrics.InactiveHeight) * activation)
                .ToString(CultureInfo.InvariantCulture),
            ["paint"] = $"#{red:X2}{green:X2}{blue:X2}{alpha:X2}",
        };
    }

    private void ReloadWidgetDot(string name, double activation) => ReloadHome(_homeWidgetDots[name],
        "LaunchInstallHomeWidget" + name + ".d.nxml", WidgetDotData(activation));

    private string HomeProfileKind(LaunchProfileKind kind) => HomeCopy(kind switch
    {
        LaunchProfileKind.Microsoft => "account.microsoft",
        LaunchProfileKind.ThirdParty => "account.third-party",
        LaunchProfileKind.Offline => "account.offline",
        LaunchProfileKind.LittleSkin => "account.little-skin",
        LaunchProfileKind.NCloud => "account.ncloud",
        _ => "account.profile",
    });

    private string HomeProfileDescription(LaunchProfileView profile)
    {
        if (profile.Kind == LaunchProfileKind.ThirdParty)
        {
            string server = profile.AuthServer.Trim();
            if (!server.Contains("://", StringComparison.Ordinal)) server = "https://" + server;
            if (Uri.TryCreate(server, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                return HomeFormat("ThirdParty", new() { ["host"] = uri.Host });
        }
        if (!string.IsNullOrWhiteSpace(profile.Info)) return profile.Info;
        return HomeProfileKind(profile.Kind);
    }

    private Dictionary<string, string> HomeProfileData(LaunchProfileView profile)
    {
        bool selected = profile.Index == SelectedAccountIndex;
        string suffix = ":" + profile.Index.ToString(CultureInfo.InvariantCulture);
        return new()
        {
            ["row"] = "account-row" + suffix,
            ["label"] = HomeFormat("SelectProfile", new() { ["name"] = profile.Username, ["kind"] = HomeProfileKind(profile.Kind) }),
            ["selected"] = selected ? "true" : "false",
            ["style"] = HomeTerms.Value[selected ? "profile.style.selected" : "profile.style.default"],
            ["avatar_key"] = "ProfileAvatar" + suffix,
            ["avatar"] = LaunchProfilePresentation.Avatar(profile.Uuid),
            ["name_key"] = "ProfileName" + suffix,
            ["name"] = profile.Username,
            ["name_style"] = HomeTerms.Value[selected ? "profile.name-style.selected" : "profile.name-style.default"],
            ["detail_key"] = "ProfileDetail" + suffix,
            ["detail"] = HomeProfileDescription(profile),
            ["delete_key"] = "ProfileDelete" + suffix,
        };
    }

    private void RefreshLaunchHomePresentation() => Interlocked.Exchange(ref _homeRefreshRequested, 1);

    private void ApplyLaunchHomePresentation()
    {
        _presentedWidgetIndex = -1;
        foreach (var root in _accountRowEntities.Values) _homeProjections.Remove(root);
        StyleAccountRows();
        RefreshAccountPresentation();
        RefreshWidgetPresentation();
        ProjectLibrary();
        UpdateLaunchButton();
        RefreshLaunchingDisplay();
        RefreshAcquisitionPrompt();
        _preflightAttempt = null;
        RefreshPreflightPrompt();
    }

    private (XsrUiEntityId Page, Dictionary<string, XsrUiEntityId> Entities) LoadLaunchingPage()
    {
        XsrUiEntityId page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("LaunchingPage.nxml"), _shell.Tree, _store);
        var entities = HomeNodes(page);
        _homeLaunchingWorkspace = HomeDynamic.Append(entities["LaunchingWorkspaceSlot"],
            DesktopMarkup.Dynamic("LaunchInstallHomeLaunchingWorkspace.d.nxml", new Dictionary<string, string>
            { ["hint_visible"] = _homeLaunchingHintVisible ? "true" : "false" }));
        foreach (var node in HomeNodes(_homeLaunchingWorkspace)) entities[node.Key] = node.Value;
        return (page, entities);
    }

    private void SetLaunchingHintVisible(bool visible)
    {
        if (_disposed || _homeLaunchingHintVisible == visible) return;
        _homeLaunchingHintVisible = visible;
        if (_homeLaunchingWorkspace.IsAssigned)
            ReloadHome(_homeLaunchingWorkspace, "LaunchInstallHomeLaunchingWorkspace.d.nxml", new()
            { ["hint_visible"] = visible ? "true" : "false" });
    }
}
