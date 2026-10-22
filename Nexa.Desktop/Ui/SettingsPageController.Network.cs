using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId ProxyMode = XsrSemanticId.Parse("ui.settings.proxy.mode");
    private static readonly XsrSemanticId ProxyProtocol = XsrSemanticId.Parse("ui.settings.proxy.protocol");
    private static readonly XsrSemanticId ProxyApply = XsrSemanticId.Parse("ui.settings.proxy.apply");
    private static readonly XsrSemanticId ProxyReload = XsrSemanticId.Parse("ui.settings.proxy.reload");
    private static readonly string[] ProxyKeys = ["network.proxy-address", "network.proxy-user", "network.proxy-password"];
    private readonly Dictionary<string, XsrUiEntityId> _proxyInputs = [];
    private readonly Dictionary<XsrUiEntityId, string> _proxyModes = [];
    private readonly Dictionary<string, string> _proxyCommitted = [];
    private XsrUiEntityId _proxyWorkspace, _proxyTrack, _proxyProtocolTrack;
    private readonly Dictionary<XsrUiEntityId, string> _proxyProtocols = [];
    private string _proxyDraftProtocol = "http";
    private string _proxyDraftMode = "1";
    private string _proxyCommittedMode = "1";
    private long _proxyRevision = -1;
    private bool _proxyInitialized;
    private Task<XsrResult>? _proxyCommit;
    private Dictionary<string, string>? _proxySavedDrafts;

    private static bool IsProxyIntent(XsrSemanticId command) => command == ProxyMode || command == ProxyProtocol || command == ProxyApply || command == ProxyReload;

    private void ResetProxyPreferences(bool preserveDraft = false)
    {
        _proxySavedDrafts = preserveDraft && _proxyInitialized && _selected == "network"
            ? _proxyInputs.Where(pair => _shell.Tree.IsAlive(pair.Value)).ToDictionary(pair => pair.Key,
                pair => _shell.Tree.GetComponent<XsrUiTextInput>(pair.Value)!.ReadDraft()) : null;
        _proxyInputs.Clear(); _proxyModes.Clear(); _proxyProtocols.Clear(); _proxyWorkspace = _proxyTrack = default;
        if (_proxySavedDrafts is null)
        { _proxyCommitted.Clear(); _proxyInitialized = false; _proxyRevision = -1; }
    }

    private void BuildProxyPreferences()
    {
        var root = _proxyWorkspace = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsCustomProxy.d.nxml", ProxyProjection()));
        var names = new Dictionary<string, XsrUiEntityId>(StringComparer.Ordinal);
        _shell.Tree.Walk(root, entity => { names[_shell.Tree.Name(entity)] = entity; return true; });
        _proxyTrack = names["SettingsProxyMode"]; _proxyProtocolTrack = names["SettingsProxyProtocol"];
        foreach (string value in new[] { "1", "0", "2" })
        {
            var option = names["SettingsProxyMode." + value];
            _proxyModes[option] = value;
        }
        foreach (string protocol in new[] { "http", "https", "socks4", "socks5", "pac" })
        {
            var option = names["SettingsProxyProtocol." + protocol];
            _proxyProtocols[option] = protocol;
        }
        foreach (string key in ProxyKeys)
        {
            var input = names["SettingsInput." + key];
            _proxyInputs[key] = input;
        }
        if (_proxySavedDrafts is not null)
        {
            foreach (var (key, input) in _proxyInputs)
                _shell.Renderer.SetTextInputValue(input, _proxySavedDrafts.GetValueOrDefault(key, ""));
            _proxySavedDrafts = null;
        }
        UpdateProxyPreferences();
    }

    private bool ProxyHasDraft => _proxyDraftMode != _proxyCommittedMode || _proxyInputs.Any(pair =>
        _shell.Tree.IsAlive(pair.Value) && ProxyStoredValue(pair.Key, _shell.Tree.GetComponent<XsrUiTextInput>(pair.Value)!.ReadDraft()) != _proxyCommitted.GetValueOrDefault(pair.Key, ""));

    private void UpdateProxyPreferences()
    {
        if (_proxyCommit is { IsCompleted: true } completed)
        {
            _proxyCommit = null;
            if (PendingQuery.Succeeded(completed))
            {
                _proxyInitialized = false; _revision = -1;
                return;
            }
            _feedback.Error(CustomFormat("ProxySaveFailure", new() { ["error"] = completed.IsCompletedSuccessfully ? completed.Result.Error?.Message ?? "" : CustomCopy("network.proxyFailed") }), localizeMessage: false);
        }
        if (_values is null || !_proxyTrack.IsAssigned || !_shell.Tree.IsAlive(_proxyTrack)) return;
        if (!_proxyInitialized || _values.Revision != _proxyRevision && !ProxyHasDraft)
        {
            _proxyDraftMode = _proxyCommittedMode = _values.Values.Single(item => item.Key == "network.proxy-mode").Value.Value ?? "1";
            foreach (var (key, input) in _proxyInputs)
            {
                string value = _values.Values.Single(item => item.Key == key).Value.Value ?? "";
                _proxyCommitted[key] = value;
                if (key == "network.proxy-address")
                {
                    _proxyDraftProtocol = value.StartsWith("pac+", StringComparison.OrdinalIgnoreCase) ? "pac"
                        : Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ? endpoint.Scheme : "http";
                    if (_proxyDraftProtocol == "pac") value = value[4..];
                }
                _shell.Renderer.SetTextInputValue(input, value);
            }
            _proxyRevision = _values.Revision; _proxyInitialized = true;
        }
        string addressDraft = _shell.Tree.GetComponent<XsrUiTextInput>(_proxyInputs["network.proxy-address"])!.ReadDraft();
        if (_proxyDraftProtocol != "pac" && Uri.TryCreate(addressDraft, UriKind.Absolute, out var draftUri)
            && draftUri.Scheme is "http" or "https" or "socks4" or "socks5") _proxyDraftProtocol = draftUri.Scheme;
        foreach (var (option, protocol) in _proxyProtocols)
        {
            bool selected = protocol == _proxyDraftProtocol;
            if (selected) _shell.Tree.GetComponent<XsrUiSegmentedTrack>(_proxyProtocolTrack)!.Selected = option;
        }
        foreach (var (option, value) in _proxyModes)
        {
            bool selected = value == _proxyDraftMode;
            if (selected) _shell.Tree.GetComponent<XsrUiSegmentedTrack>(_proxyTrack)!.Selected = option;
        }
        _dynamic.Reload(_proxyWorkspace, DesktopMarkup.Dynamic("SettingsCustomProxy.d.nxml", ProxyProjection()));
    }

    private Dictionary<string, string> ProxyProjection()
    {
        Dictionary<string, string> data = new() { ["manual"] = Flag(_proxyDraftMode == "2"), ["password"] = Flag(_proxyDraftProtocol != "socks4"), ["enabled"] = Flag(_writing is null) };
        foreach (string value in new[] { "1", "0", "2" })
        {
            bool selected = value == _proxyDraftMode; data["mode" + value] = Flag(selected);
            data["mode" + value + "Style"] = selected ? "settings-proxy-option-selected settings-proxy-mode" : "settings-proxy-option settings-proxy-mode";
        }
        foreach (string protocol in new[] { "http", "https", "socks4", "socks5", "pac" })
        {
            bool selected = protocol == _proxyDraftProtocol; data["protocol" + protocol] = Flag(selected);
            data["protocol" + protocol + "Style"] = selected ? "settings-proxy-option-selected settings-proxy-protocol" : "settings-proxy-option settings-proxy-protocol";
        }
        return data;
    }

    private void HandleProxyIntent(DesktopUiIntent intent)
    {
        if (_selected != "network" || _instanceDirectory is not null || _writing is not null || !_proxyInitialized
            || !_shell.Tree.IsAlive(intent.Source) || !_shell.Tree.IsAlive(_proxyWorkspace)
            || _shell.Tree.GetComponent<XsrUiInput>(intent.Source)?.Enabled != true) return;
        for (var node = intent.Source; node.IsAssigned; node = _shell.Tree.Parent(node))
            if (_shell.Tree.GetComponent<XsrUiElement>(node)?.IsVisible == false) return;
        if (intent.Command == ProxyMode && _proxyModes.TryGetValue(intent.Source, out var mode))
        { _proxyDraftMode = mode; UpdateProxyPreferences(); return; }
        if (intent.Command == ProxyProtocol && _proxyProtocols.TryGetValue(intent.Source, out var protocol))
        {
            var input = _proxyInputs["network.proxy-address"];
            string draft = _shell.Tree.GetComponent<XsrUiTextInput>(input)!.ReadDraft();
            int separator = draft.IndexOf("://", StringComparison.Ordinal);
            string tail = separator < 0 ? draft : draft[(separator + 3)..];
            string scheme = protocol == "pac" ? (draft.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "http" : "https") : protocol;
            _proxyDraftProtocol = protocol;
            _shell.Renderer.SetTextInputValue(input, scheme + "://" + tail);
            UpdateProxyPreferences(); return;
        }
        if (intent.Command == ProxyReload && intent.Source == CatalogNodes(_proxyWorkspace)["SettingsProxyReload"])
        { _proxyInitialized = false; UpdateProxyPreferences(); return; }
        if (intent.Command != ProxyApply || intent.Source != CatalogNodes(_proxyWorkspace)["SettingsProxyApply"]
            || !_commands.TryResolve(SettingsPolicyContract.BatchCommand, out var route)) return;
        List<SettingsMutation> changes = [new("network.proxy-mode", SettingsLayer.Global, new(SettingsOverrideMode.Custom, _proxyDraftMode))];
        foreach (var (key, input) in _proxyInputs)
            changes.Add(new(key, SettingsLayer.Global, new(SettingsOverrideMode.Custom, ProxyStoredValue(key, _shell.Tree.GetComponent<XsrUiTextInput>(input)!.ReadDraft()))));
        _proxyCommit = _writing = SaveProxyAsync(route, new(changes.AsReadOnly(), _proxyRevision));
    }

    private string ProxyStoredValue(string key, string draft) => key == "network.proxy-address" && string.IsNullOrWhiteSpace(draft) ? ""
        : key == "network.proxy-address" && _proxyDraftProtocol == "pac"
        && !draft.StartsWith("pac+", StringComparison.OrdinalIgnoreCase) ? "pac+" + draft : draft;

    private async Task<XsrResult> SaveProxyAsync(XsrCommandId route, SettingsBatchCommand command)
    {
        var result = await _commands.Dispatch(route, command).Completion.ConfigureAwait(false);
        return result;
    }
}
