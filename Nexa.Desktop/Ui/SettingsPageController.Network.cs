using Nexa.Services.Settings;
using Nexa.UI.Next;
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
        var group = FormGroup(_sections, "SettingsProxy", "代理");
        Text(group, "应用后对新请求生效。", 11, Muted, 24);
        var toolbar = Stack(group, "SettingsProxyToolbar", XsrUiOrientation.Horizontal, 8);
        _proxyTrack = Stack(toolbar, "SettingsProxyMode", XsrUiOrientation.Horizontal, 0);
        _shell.Tree.SetComponent(_proxyTrack, new XsrUiSemantic(XsrUiSemanticRole.RadioGroup, "代理模式草稿"));
        Style(_proxyTrack, new(241, 245, 250), Ink, 9);
        var thumb = Element(_proxyTrack, "SettingsProxyModeThumb", XsrUiSemanticRole.None, null);
        _shell.Tree.GetComponent<XsrUiElement>(thumb)!.IsVisible = false;
        Style(thumb, White, Ink, 7);
        _shell.Tree.SetComponent(thumb, new XsrUiTransition());
        _shell.Tree.SetComponent(_proxyTrack, new XsrUiSegmentedTrack(thumb));
        foreach (var (value, label) in new[] { ("1", "系统代理"), ("0", "无代理"), ("2", "手动代理") })
        {
            var option = RadioOption(_proxyTrack, "SettingsProxyMode." + value, label, ProxyMode, 84);
            _proxyModes[option] = value;
        }
        _proxyManual = Stack(group, "SettingsProxyManual", XsrUiOrientation.Vertical, 6);
        _proxyProtocolTrack = Stack(_proxyManual, "SettingsProxyProtocol", XsrUiOrientation.Horizontal, 0);
        _shell.Tree.SetComponent(_proxyProtocolTrack, new XsrUiSemantic(XsrUiSemanticRole.RadioGroup, "代理协议"));
        Style(_proxyProtocolTrack, new(241, 245, 250), Ink, 9);
        var protocolThumb = Element(_proxyProtocolTrack, "SettingsProxyProtocolThumb", XsrUiSemanticRole.None, null);
        _shell.Tree.GetComponent<XsrUiElement>(protocolThumb)!.IsVisible = false;
        Style(protocolThumb, White, Ink, 7); _shell.Tree.SetComponent(protocolThumb, new XsrUiTransition());
        _shell.Tree.SetComponent(_proxyProtocolTrack, new XsrUiSegmentedTrack(protocolThumb));
        foreach (string protocol in new[] { "http", "https", "socks4", "socks5", "pac" })
        {
            var option = RadioOption(_proxyProtocolTrack, "SettingsProxyProtocol." + protocol, protocol.ToUpperInvariant(), ProxyProtocol, 66);
            _proxyProtocols[option] = protocol;
        }
        _proxyProtocolHint = Text(_proxyManual, "", 11, Muted, 24);
        foreach (string key in ProxyKeys)
        {
            string label = key switch { "network.proxy-address" => "地址", "network.proxy-user" => "用户名", _ => "密码" };
            var row = Stack(_proxyManual, "SettingsProxyRow." + key, XsrUiOrientation.Horizontal, 12);
            var caption = Text(row, label, 13, Ink, 34);
            _shell.Tree.GetComponent<XsrUiElement>(caption)!.Width = 84;
            var input = Element(row, "SettingsInput." + key, XsrUiSemanticRole.TextInput, label, height: 34);
            _shell.Tree.GetComponent<XsrUiElement>(input)!.Weight = 1;
            _shell.Tree.GetComponent<XsrUiElement>(input)!.Padding = new(10, 0, 10, 0);
            _shell.Tree.SetComponent(input, new XsrUiTextInput
            {
                IsPassword = key == "network.proxy-password",
                MaximumLength = key == "network.proxy-address" ? 2048 : 1024,
                Placeholder = key == "network.proxy-address" ? "http://localhost:8080" : label
            });
            _shell.Tree.SetComponent(input, new XsrUiInput { Focusable = true, Clickable = true });
            Style(input, new(245, 246, 248), Ink, 9);
            _proxyInputs[key] = input;
        }
        ActionButton(toolbar, "SettingsProxyApply", "应用代理", ProxyApply, 84);
        ActionButton(toolbar, "SettingsProxyReload", "重新读取", ProxyReload, 84);
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
            Style(option, XsrUiColor.Transparent, selected ? Blue : Ink, 7, 12, selected ? 600 : 450);
            _shell.Tree.GetComponent<XsrUiVisualStyle>(option)!.TextAlignment = XsrUiTextAlignment.Center;
        }
        string hint = _proxyDraftProtocol switch
        {
            "socks4" => "SOCKS4 仅支持用户标识，不支持密码认证。",
            "pac" => "填写 PAC 的 HTTP 或 HTTPS 地址；认证信息用于脚本选中的代理。",
            _ => "用户名和密码可留空。",
        };
        var hintText = _shell.Tree.GetComponent<XsrUiText>(_proxyProtocolHint)!;
        if (hintText.Content != hint) { hintText.Content = hint; _shell.Tree.MarkDirty(_proxyProtocolHint, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint); }
        var password = _proxyInputs["network.proxy-password"];
        DesktopElementVisibility.Set(_shell.Tree, _shell.Tree.Parent(password), _proxyDraftProtocol is not ("socks4"));
        foreach (var (option, value) in _proxyModes)
        {
            bool selected = value == _proxyDraftMode;
            _shell.Tree.GetComponent<XsrUiSelection>(option)!.IsSelected = selected;
            if (selected) _shell.Tree.GetComponent<XsrUiSegmentedTrack>(_proxyTrack)!.Selected = option;
            Style(option, XsrUiColor.Transparent, selected ? Blue : Ink, 7, 12, selected ? 600 : 450);
            _shell.Tree.GetComponent<XsrUiVisualStyle>(option)!.TextAlignment = XsrUiTextAlignment.Center;
        }
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
