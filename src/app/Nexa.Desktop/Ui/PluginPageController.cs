using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;
using PluginSidecar.Packages;
using PluginSidecar.WasmRuntime;

namespace Nexa.Desktop.Ui;

/// <summary>Protected Host management page and admitted plugin documents. All tree changes occur at frame time.</summary>
internal sealed class PluginPageController : IAsyncDisposable
{
    private readonly XsrUiShell _shell;
    private readonly XsrStateStore _store;
    private readonly DesktopFeedbackService _feedback;
    private readonly NxmlDynamicContent _dynamic;
    private readonly Func<CancellationToken, Task<string?>> _pick;
    private readonly Func<bool> _developer;
    private readonly Func<bool> _engineAvailable;
    private readonly Func<Guid> _engineSession;
    private readonly bool _safeMode;
    private readonly IDisposable _route;
    private readonly PluginConfirmation _confirmation;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<(PluginIdentity Owner, QualifiedId Page)> _navigation = new();
    private readonly ConcurrentQueue<(PluginIdentity Owner, UINotification Notice)> _notices = new();
    private readonly Dictionary<string, (PluginPage State, NxmlDocumentInstance Document)> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<XsrUiEntityId, (string Kind, string Id)> _buttons = [];
    private readonly XsrUiEntityId _body, _fallback;
    private string _stamp = "";
    private readonly Dictionary<string, Revision> _rejected = new(StringComparer.Ordinal);
    private Task? _work;
    private Guid _boundSession;
    private bool _closed;
    private readonly PluginApplication? _application;
    internal PluginApplication Application => _application ?? throw new IOException("Plugin storage unavailable.");
    internal XsrUiEntityId Page { get; }

    internal PluginPageController(XsrUiShell shell, DesktopUiIntentSink intents, XsrStateStore store, DesktopFeedbackService feedback,
        string storage, IEnumerable<HostCapabilityRoute> business,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<IWasmPluginMachine>> machine,
        Func<CancellationToken, Task<string?>> pick, Func<bool> developer, Func<bool> engineAvailable, Func<Guid> engineSession, bool safeMode)
    {
        _fallback = shell.Stage.Navigation.Current;
        _shell = shell; _store = store; _feedback = feedback; _pick = pick; _developer = developer;
        _engineAvailable = engineAvailable; _engineSession = engineSession; _safeMode = safeMode;
        _confirmation = new(feedback); _dynamic = new(shell.Tree, store);
        try
        {
            _application = new(storage, machine, business, _confirmation,
            (owner, notice) => { if (_notices.Count < 32) _notices.Enqueue((owner, notice)); else throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE); },
            (owner, page) => { if (_navigation.Count < 16) _navigation.Enqueue((owner, page)); else throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE); }, ValidateSource);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { feedback.Warn(DesktopOverlayText.Raw("plugin.storage-unavailable")); }
        Page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("PluginPage.nxml"), shell.Tree, store);
        _body = shell.Tree.Children(Page).Single();
        _route = intents.Route(Intent); shell.Renderer.FramePreparing += Frame;
    }
    private static void ValidateSource(string source)
    {
        var document = DesktopMarkup.Load(source.AsMemory());
        if (document.Root.Name != "nxml-page") throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        Validate(document.Root);
        static void Validate(NxmlElement node)
        {
            if (node.Name == "Image" || node.Attributes.Any(a => a.Value.Kind == NxmlValueKind.StateBinding ||
                a.Name is "Command" or "ScrollCommand" && (a.Value.Kind != NxmlValueKind.Literal || a.Value.Text != "ui.plugin.action")))
                throw new AbiHostException(AbiErrors.CAPABILITY_DENIED);
            foreach (var child in node.Children) Validate(child);
        }
    }
    private static NxmlPreparedDocument Compile(PluginPage page)
    {
        Dictionary<string, string> data = [];
        if (page.Model is AbiValue.Map map)
            foreach (var field in map.Value)
                data[field.Key] = field.Value switch
                {
                    AbiValue.Utf8String text => text.Value,
                    AbiValue.Bool flag => flag.Value ? "true" : "false",
                    AbiValue.Int64 number => number.Value.ToString(CultureInfo.InvariantCulture),
                    AbiValue.UInt64 number => number.Value.ToString(CultureInfo.InvariantCulture),
                    AbiValue.Float64 number => number.Value.ToString("R", CultureInfo.InvariantCulture),
                    AbiValue.Null => "",
                    _ => throw new AbiHostException(AbiErrors.SCHEMA_INVALID)
                };
        var program = new NxmlExtensionProgram("page.nxml", new Dictionary<string, string> { ["page.nxml"] = page.Source }, styles: DesktopMarkup.SharedStyles);
        var document = program.Compile(data);
        if (document.Root.Root.Role != XsrUiSemanticRole.Page) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        Visit(document.Root.Root);
        foreach (var fragment in document.Fragments) Visit(fragment.Content.Root);
        return document;
        static void Visit(NxmlIrNode node)
        {
            // Plugin data may not bind private Host state, execute product commands, or fetch arbitrary images/files.
            if (node.Bindings.Count != 0 || node.Command is { } command && command.Value != "ui.plugin.action" || node.ScrollCommand is not null ||
                node.Recipe == NxmlRuntimeRecipe.Image) throw new AbiHostException(AbiErrors.CAPABILITY_DENIED);
            foreach (var child in node.Children) Visit(child);
        }
    }
    private bool Intent(DesktopUiIntent intent)
    {
        if (_closed) return false;
        foreach (var item in _pages.Values)
        {
            if (!item.Document.Owns(intent.Source)) continue;
            // Consume all guest intents, including forged product commands.
            if (_shell.Stage.Navigation.Current != item.Document.Root || !Enabled(intent.Source, item.Document.Root) || _work is { IsCompleted: false }) return true;
            if (intent.Command.Value == "ui.plugin.action")
            {
                string action = _shell.Renderer.ReadIntentArgument(intent.Source);
                if (action.StartsWith(item.State.Owner.PluginId + ":", StringComparison.Ordinal)) Start(() => InvokeAction(item.State, action));
            }
            return true;
        }
        if (_shell.Stage.Navigation.Current != Page || !_buttons.TryGetValue(intent.Source, out var button) || !Enabled(intent.Source, Page)) return false;
        if (intent.Command.Value != "ui.plugins.manage") return true;
        if (_work is { IsCompleted: false }) return true;
        switch (button.Kind)
        {
            case "import": if (!_safeMode && _developer() && _engineAvailable()) { _boundSession = _engineSession(); Start(Import); } break;
            case "stop": Start(() => Application.StopAsync(button.Id, _stop.Token)); break;
            case "page": if (_pages.TryGetValue(button.Id, out var page)) OpenPage(page.Document.Root); break;
        }
        return true;
    }
    private void OpenPage(XsrUiEntityId page)
    {
        if (_shell.Stage.Navigation.Current == page) return;
        if (_pages.Values.Any(p => p.Document.Root == _shell.Stage.Navigation.Current)) _shell.Stage.Navigation.Replace(page);
        else _shell.Stage.Navigation.Push(page);
    }
    private void ClosePage(XsrUiEntityId page)
    {
        if (_shell.Stage.Navigation.Current != page) return;
        if (!_shell.Stage.Navigation.Pop()) _shell.Stage.Navigation.Replace(Page);
    }
    private bool Enabled(XsrUiEntityId node, XsrUiEntityId root)
    {
        if (!_shell.Tree.IsAlive(node) || _shell.Tree.GetComponent<XsrUiInput>(node) is not { Enabled: true, Clickable: true }) return false;
        for (var current = node; current.IsAssigned && _shell.Tree.IsAlive(current); current = _shell.Tree.Parent(current))
        { if (_shell.Tree.GetComponent<XsrUiElement>(current)?.IsVisible == false) return false; if (current == root) return true; }
        return false;
    }
    private string Copy(string key) => _shell.Renderer.LocalizeText(DesktopOverlayText.Raw(key));

    private void Start(Func<Task> action) { _work = Observe(action); _stamp = ""; }
    private async Task Observe(Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { if (!_closed) _feedback.Error(Copy("plugin.error") + ": " + error.Message); }
    }
    private async Task InvokeAction(PluginPage page, string action)
    {
        var operation = Abi100Catalog.Operations.Single(o => o.Group == "Actions" && o.Name == "Invoke");
        var request = new ActionRequest(new(action), new(new("nexa:ui"), new(page.Page.Id.Value), "plugin"),
            page.Model is AbiValue.Null ? Empty.Write(new()) : page.Model, null, Guid.NewGuid().ToString("N"));
        var result = await Application.Runtime.InvokeAsync(page.Owner, operation.ContractId, operation.MethodId, ActionRequest.Write(request),
            new(Guid.NewGuid(), TimeSpan.FromSeconds(30), _stop.Token) { IdempotencyKey = request.IdempotencyKey }).ConfigureAwait(false);
        if (!result.Success) throw new AbiHostException(result.Error!.Code);
    }
    private async Task Import()
    {
        var path = await _pick(_stop.Token).ConfigureAwait(false); if (path is null) return;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (file.Length is < 1 or > 25 * 1024 * 1024) throw new InvalidDataException("Plugin package size is invalid.");
        byte[] bytes = new byte[(int)file.Length]; await file.ReadExactlyAsync(bytes, _stop.Token).ConfigureAwait(false);
        try
        {
            PluginManifest manifest;
            using (var archive = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read))
            {
                var entry = archive.GetEntry("plugin.json");
                if (entry is null || entry.Length > 65536) throw new InvalidDataException("Plugin manifest missing or too large.");
                using var stream = entry.Open(); manifest = JsonSerializer.Deserialize(stream, PackageJsonContext.Default.PluginManifest) ?? throw new InvalidDataException();
            }
            using var package = VerifiedPluginPackage.Parse(bytes, manifest.PluginId, manifest.PluginVersion);
            string message = manifest.PluginId + " " + manifest.PluginVersion + "\n\n" + DesktopOverlayText.Raw("plugin.unsigned") + "\n\n" + DesktopOverlayText.Raw("plugin.permissions") + ":\n" + string.Join('\n', manifest.RequestedPermissions);
            if (!await _confirmation.AskAsync(DesktopOverlayText.Raw("plugin.developer-load"), message, _stop.Token).ConfigureAwait(false)) return;
            if (!_developer() || _safeMode || !_engineAvailable()) throw new InvalidOperationException("Plugin loading is unavailable.");
            await Application.LoadAsync(package, manifest.RequestedPermissions, _stop.Token).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private void Frame(object? sender, EventArgs args)
    {
        if (_closed) return;
        if (_application is null)
        {
            if (_shell.Stage.Navigation.Current == Page && _stamp != "storage-failed")
            {
                _stamp = "storage-failed";
                _dynamic.Append(_body, DesktopMarkup.Dynamic("PluginWorkspace.d.nxml", new Dictionary<string, string>
                { ["status"] = Copy("plugin.storage-unavailable"), ["import"] = "false" }));
            }
            return;
        }
        if (_boundSession != Guid.Empty && _engineSession() != _boundSession && _work is not { IsCompleted: false })
        { _boundSession = Guid.Empty; Start(async () => { foreach (var plugin in Application.Runtime.Active) await Application.StopAsync(plugin.PluginId, _stop.Token).ConfigureAwait(false); }); }
        var snapshot = Application.Snapshot();
        var live = snapshot.Pages.Select(p => p.Page.Id.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _pages.Keys.Where(key => !live.Contains(key)).ToArray())
        {
            var page = _pages[key];
            ClosePage(page.Document.Root);
            page.Document.Dispose(); _pages.Remove(key); _rejected.Remove(key);
        }
        foreach (var page in snapshot.Pages)
        {
            if (_rejected.GetValueOrDefault(page.Page.Id.Value) == page.Revision) continue;
            try
            {
                if (_pages.TryGetValue(page.Page.Id.Value, out var existing))
                { if (existing.State.Revision != page.Revision) { existing.Document.Reload(Compile(page)); _pages[page.Page.Id.Value] = (page, existing.Document); } }
                else _pages.Add(page.Page.Id.Value, (page, new(_shell.Tree, _store, Compile(page))));
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            {
                _rejected[page.Page.Id.Value] = page.Revision;
                if (_pages.Remove(page.Page.Id.Value, out var rejected))
                {
                    ClosePage(rejected.Document.Root);
                    rejected.Document.Dispose();
                }
                _feedback.Warn(Copy("plugin.page-rejected") + ": " + page.Page.Id.Value);
            }
        }
        while (_notices.TryDequeue(out var notice))
            if (Application.Runtime.Active.Contains(notice.Owner)) _feedback.Info(notice.Owner.PluginId + ": " + notice.Notice.Title.MessageKey + "\n" + notice.Notice.Body.MessageKey);
        while (_navigation.TryDequeue(out var target))
            if (_pages.TryGetValue(target.Page.Value, out var page) && page.State.Owner == target.Owner) OpenPage(page.Document.Root);
        if (_shell.Stage.Navigation.Current != Page) return;
        string status = _safeMode ? Copy("plugin.safe-mode") : _engineAvailable() ? Copy("plugin.engine-ready") : Copy("plugin.engine-unavailable");
        bool import = !_safeMode && _developer() && _engineAvailable() && _work is not { IsCompleted: false };
        string stamp = status + import + string.Join('|', snapshot.Plugins.Select(p => p.Id + p.State + p.Error)) + string.Join('|', _pages.Keys) + (_work is { IsCompleted: false });
        if (stamp == _stamp) return; _stamp = stamp;
        _buttons.Clear(); foreach (var child in _shell.Tree.Children(_body).ToArray()) _shell.Tree.Destroy(child);
        var workspace = _dynamic.Append(_body, DesktopMarkup.Dynamic("PluginWorkspace.d.nxml", new Dictionary<string, string> { ["status"] = status, ["import"] = import ? "true" : "false" }));
        var nodes = Names(workspace); _buttons[nodes["PluginImport"]] = ("import", "");
        foreach (var plugin in snapshot.Plugins)
        {
            var row = _dynamic.Append(nodes["PluginRows"], DesktopMarkup.Dynamic("PluginRow.d.nxml", new Dictionary<string, string>
            { ["name"] = plugin.Id + " " + plugin.Version, ["state"] = plugin.State + (plugin.Error is null ? "" : " · " + plugin.Error), ["active"] = plugin.State == "Active" && _work is not { IsCompleted: false } ? "true" : "false" }));
            _buttons[Names(row)["PluginStop"]] = ("stop", plugin.Id);
        }
        foreach (var page in _pages.Values)
        {
            var row = _dynamic.Append(nodes["PluginRows"], DesktopMarkup.Dynamic("PluginLink.d.nxml", new Dictionary<string, string> { ["name"] = page.State.Title }));
            _buttons[Names(row)["PluginOpenPage"]] = ("page", page.State.Page.Id.Value);
        }
    }
    private Dictionary<string, XsrUiEntityId> Names(XsrUiEntityId root)
    { Dictionary<string, XsrUiEntityId> names = []; _shell.Tree.Walk(root, node => { names[_shell.Tree.Name(node)] = node; return true; }); return names; }
    public async ValueTask DisposeAsync()
    {
        if (_closed) return; _closed = true; _stop.Cancel(); _route.Dispose(); _shell.Renderer.FramePreparing -= Frame;
        if (_work is not null) await _work.ConfigureAwait(false);
        if (_application is not null) await _application.DisposeAsync().ConfigureAwait(false);
        if ((_shell.Stage.Navigation.Current == Page || _pages.Values.Any(p => p.Document.Root == _shell.Stage.Navigation.Current)) && _shell.Tree.IsAlive(_fallback))
        {
            while (_shell.Stage.Navigation.Depth > 1) _shell.Stage.Navigation.Pop();
            _shell.Stage.Navigation.Replace(_fallback);
        }
        foreach (var page in _pages.Values) page.Document.Dispose();
        if (_shell.Tree.IsAlive(Page)) _shell.Tree.Destroy(Page); _stop.Dispose();
    }
}
