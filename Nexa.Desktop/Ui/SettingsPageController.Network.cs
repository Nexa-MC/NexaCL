using Nexa.Services.Settings;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;
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
    private XsrUiEntityId _proxyTrack, _proxyProtocolTrack, _proxyManual, _proxyProtocolHint;
    private readonly Dictionary<XsrUiEntityId, string> _proxyProtocols = [];
    private static readonly XsrUiVisualStyleSnapshot ProxyOption = DesktopMarkup.Visual("settings-proxy-option").Snapshot();
    private static readonly XsrUiVisualStyleSnapshot ProxySelectedOption = DesktopMarkup.Visual("settings-proxy-option-selected").Snapshot();
    private string? _proxyHintTemplate;
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
        _proxyInputs.Clear(); _proxyModes.Clear(); _proxyProtocols.Clear(); _proxyTrack = default;
        if (_proxySavedDrafts is null)
        { _proxyCommitted.Clear(); _proxyInitialized = false; _proxyRevision = -1; }
    }

    private void BuildProxyPreferences()
    {
        var root = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsProxy.d.nxml"));
        var names = new Dictionary<string, XsrUiEntityId>(StringComparer.Ordinal);
        _shell.Tree.Walk(root, entity => { names[_shell.Tree.Name(entity)] = entity; return true; });
        _proxyTrack = names["SettingsProxyMode"]; _proxyProtocolTrack = names["SettingsProxyProtocol"];
        _proxyManual = names["SettingsProxyManual"]; _proxyProtocolHint = names["SettingsProxyHint"];
        _proxyHintTemplate = null;
        foreach (var (track, thumb) in new[] { (_proxyTrack, names["SettingsProxyModeThumb"]), (_proxyProtocolTrack, names["SettingsProxyProtocolThumb"]) })
        {
            _shell.Tree.SetComponent(track, new XsrUiSegmentedTrack(thumb));
            _shell.Tree.SetComponent(thumb, new XsrUiTransition());
        }
        foreach (string value in new[] { "1", "0", "2" })
        {
            var option = names["SettingsProxyMode." + value];
            _proxyModes[option] = value; _shell.Tree.SetComponent(option, new XsrUiSelection());
        }
        foreach (string protocol in new[] { "http", "https", "socks4", "socks5", "pac" })
        {
            var option = names["SettingsProxyProtocol." + protocol];
            _proxyProtocols[option] = protocol; _shell.Tree.SetComponent(option, new XsrUiSelection());
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
        DesktopElementVisibility.Set(_shell.Tree, _proxyManual, _proxyDraftMode == "2");
        string addressDraft = _shell.Tree.GetComponent<XsrUiTextInput>(_proxyInputs["network.proxy-address"])!.ReadDraft();
        if (_proxyDraftProtocol != "pac" && Uri.TryCreate(addressDraft, UriKind.Absolute, out var draftUri)
            && draftUri.Scheme is "http" or "https" or "socks4" or "socks5") _proxyDraftProtocol = draftUri.Scheme;
        foreach (var (option, protocol) in _proxyProtocols)
        {
            bool selected = protocol == _proxyDraftProtocol;
            _shell.Tree.GetComponent<XsrUiSelection>(option)!.IsSelected = selected;
            if (selected) _shell.Tree.GetComponent<XsrUiSegmentedTrack>(_proxyProtocolTrack)!.Selected = option;
            ApplyProxyOptionVisual(option, selected);
        }
        string hintTemplate = _proxyDraftProtocol switch
        {
            "socks4" => "SettingsProxyHintSocks4.d.nxml",
            "pac" => "SettingsProxyHintPac.d.nxml",
            _ => "SettingsProxyHintDefault.d.nxml",
        };
        if (_proxyHintTemplate != hintTemplate)
        {
            _dynamic.Replace(_proxyProtocolHint, DesktopMarkup.Dynamic(hintTemplate));
            _proxyHintTemplate = hintTemplate;
        }
        var password = _proxyInputs["network.proxy-password"];
        DesktopElementVisibility.Set(_shell.Tree, _shell.Tree.Parent(password), _proxyDraftProtocol is not ("socks4"));
        foreach (var (option, value) in _proxyModes)
        {
            bool selected = value == _proxyDraftMode;
            _shell.Tree.GetComponent<XsrUiSelection>(option)!.IsSelected = selected;
            if (selected) _shell.Tree.GetComponent<XsrUiSegmentedTrack>(_proxyTrack)!.Selected = option;
            ApplyProxyOptionVisual(option, selected);
        }
    }

    private void ApplyProxyOptionVisual(XsrUiEntityId option, bool selected)
    {
        if (_shell.Tree.GetComponent<XsrUiVisualStyle>(option)?.Snapshot() == (selected ? ProxySelectedOption : ProxyOption)) return;
        _shell.Tree.SetComponent(option, DesktopMarkup.Visual(selected ? "settings-proxy-option-selected" : "settings-proxy-option"));
    }

    private void HandleProxyIntent(DesktopUiIntent intent)
    {
        if (_selected != "network" || _instanceDirectory is not null || _writing is not null || !_proxyInitialized
            || !_shell.Tree.IsAlive(intent.Source)) return;
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
        if (intent.Command == ProxyReload && _shell.Tree.Name(intent.Source) == "SettingsProxyReload")
        { _proxyInitialized = false; UpdateProxyPreferences(); return; }
        if (intent.Command != ProxyApply || _shell.Tree.Name(intent.Source) != "SettingsProxyApply"
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
        if (!_disposed && !result.IsSuccess) _feedback.Error("代理配置未保存：" + result.Error?.Message);
        return result;
    }
}
