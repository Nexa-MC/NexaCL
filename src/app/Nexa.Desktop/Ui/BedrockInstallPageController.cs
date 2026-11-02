using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>Explicit official-product handoff; Microsoft Store owns installation and entitlement.</summary>
internal sealed class BedrockInstallPageController : IDisposable
{
    private static readonly Uri OfficialProductPage = new(
        "https://www.xbox.com/en-US/games/store/minecraft-for-windows/9NBLGGH2JHXJ");
    private readonly XsrUiShell _shell;
    private readonly DesktopUiIntentSink _intents;
    private readonly DesktopFeedbackService _feedback;
    private readonly Action _openStore;
    private readonly Action<Uri> _openBrowser;
    private readonly bool _supportsStore;
    private readonly NxmlDynamicContent _dynamic;
    private readonly XsrUiEntityId _workspace;
    private readonly Dictionary<string, XsrUiEntityId> _nodes;
    private Status _status;
    private string _presentedStatus;
    private bool _dirty, _disposed, _dispatching;

    private enum Status { ReadyWindows, Unsupported, StoreOpened, WebsiteOpened, StoreFailed, WebsiteFailed }

    internal BedrockInstallPageController(XsrUiShell shell, DesktopUiIntentSink intents,
        XsrStateStore store, DesktopFeedbackService feedback, Action openStore,
        Action<Uri> openBrowser, bool? supportsStore = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(openStore);
        ArgumentNullException.ThrowIfNull(openBrowser);
        _shell = shell; _intents = intents; _feedback = feedback;
        _openStore = openStore; _openBrowser = openBrowser;
        _supportsStore = supportsStore ?? OperatingSystem.IsWindows();
        _status = _supportsStore ? Status.ReadyWindows : Status.Unsupported;
        _dynamic = new(shell.Tree, store);
        var loaded = LoadPage(shell, store, _dynamic, _supportsStore);
        Page = loaded.Page; _workspace = loaded.Workspace; _nodes = loaded.Nodes; _presentedStatus = loaded.Status;
        _intents.IntentEmitted += OnIntent;
        shell.Renderer.FramePreparing += OnFrame;
    }

    internal static XsrUiEntityId LoadPlaceholder(XsrUiShell shell, XsrStateStore store) =>
        LoadPage(shell, store, new(shell.Tree, store), OperatingSystem.IsWindows()).Page;

    private static (XsrUiEntityId Page, XsrUiEntityId Workspace, Dictionary<string, XsrUiEntityId> Nodes, string Status)
        LoadPage(XsrUiShell shell, XsrStateStore store, NxmlDynamicContent content, bool supportsStore)
    {
        var page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("BedrockInstallPage.nxml"), shell.Tree, store);
        try
        {
            string status = shell.Renderer.LocalizeText(StatusCopy(supportsStore ? Status.ReadyWindows : Status.Unsupported));
            var workspace = content.Append(shell.Tree.Children(page).Single(), Workspace(supportsStore, status));
            Dictionary<string, XsrUiEntityId> nodes = [];
            shell.Tree.Walk(page, entity => { nodes[shell.Tree.Name(entity)] = entity; return true; });
            return (page, workspace, nodes, status);
        }
        catch
        {
            shell.Tree.Destroy(page);
            throw;
        }
    }

    internal XsrUiEntityId Page { get; }

    private bool IsCurrent => !_disposed && _shell.Tree.IsAlive(Page)
        && _shell.Stage.Navigation.Current == Page;

    private bool Authorized(XsrUiEntityId source, string key)
    {
        if (source != _nodes[key] || !_shell.Tree.IsAlive(source)
            || _shell.Tree.GetComponent<XsrUiInput>(source) is not { Enabled: true, Clickable: true }) return false;
        for (var node = source; node.IsAssigned && _shell.Tree.IsAlive(node); node = _shell.Tree.Parent(node))
        {
            if (_shell.Tree.GetComponent<XsrUiElement>(node)?.IsVisible == false) return false;
            if (node == Page) return true;
        }
        return false;
    }

    private void OnIntent(object? sender, DesktopUiIntentEventArgs args)
    {
        if (!IsCurrent || _dispatching) return;
        string command = args.Intent.Command.Value;
        bool store = command == "ui.bedrock.install.store";
        if (store ? !_supportsStore || !Authorized(args.Intent.Source, "BedrockInstallStore")
            : command != "ui.bedrock.install.website" || !Authorized(args.Intent.Source, "BedrockInstallWebsite")) return;
        _dispatching = true;
        try
        {
            // Both effects are synchronous, fixed product handoffs. They report no install progress.
            if (store) _openStore(); else _openBrowser(OfficialProductPage);
            if (!IsCurrent) return;
            _status = store ? Status.StoreOpened : Status.WebsiteOpened;
            _feedback.Info(StatusCopy(_status));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            if (!IsCurrent) return;
            _status = store ? Status.StoreFailed : Status.WebsiteFailed;
            _feedback.Error(StatusCopy(_status));
        }
        finally
        {
            _dispatching = false;
            if (!_disposed) _dirty = true;
        }
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        if (!IsCurrent) return;
        string status = _shell.Renderer.LocalizeText(StatusCopy(_status));
        if (_dirty || status != _presentedStatus) Present(status);
    }

    private static string StatusCopy(Status status)
    {
        var text = DesktopMarkup.Dynamic("LaunchInstallBedrockStatus" + status + ".d.nxml").Root;
        if (text.Recipe != NxmlRuntimeRecipe.Text || text.Content is null || text.Bindings.Count != 0 || text.Children.Count != 0)
            throw new NxmlCompileException("Bedrock status assets must produce literal text only.");
        return text.Content;
    }

    private static NxmlHostIr Workspace(bool supportsStore, string status) => DesktopMarkup.Dynamic("LaunchInstallBedrockWorkspace.d.nxml",
        new Dictionary<string, string>
        {
            ["status"] = status,
            ["storeEnabled"] = supportsStore ? "true" : "false",
        });

    private void Present(string status)
    {
        _dynamic.Reload(_workspace, Workspace(_supportsStore, status));
        _presentedStatus = status;
        _dirty = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _intents.IntentEmitted -= OnIntent;
        _shell.Renderer.FramePreparing -= OnFrame;
        if (_shell.Tree.IsAlive(Page)) _shell.Tree.Destroy(Page);
        _nodes.Clear();
    }
}
