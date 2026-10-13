using System.Globalization;
using Nexa.Services.Logging;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private DesktopContentWorkspaceRoutes? _diagnosticAi;
    private bool? _diagnosticAiWorkspaceEnabled;
    private Guid _diagnosticAiResultDialog;
    private XsrUiEntityId _diagnosticAiWorkspace, _diagnosticAiEditor;
    private sealed record DiagnosticAiConfirmation(DiagnosticAiRequest Request, DiagnosticRawPreview Preview);
    private readonly Dictionary<string, XsrUiEntityId> _diagnosticAiInputs = [];
    private readonly Dictionary<string, string> _diagnosticAiDraft = new(StringComparer.Ordinal)
    { ["endpoint"] = "", ["model"] = "", ["key"] = "", ["inputBudget"] = "32768", ["outputBudget"] = "1024" };

    internal void ConfigureDiagnosticAi(bool enabled) => _diagnosticAi = enabled ? new(_queries, _commands) : null;

    private bool DiagnosticAiEnabled => _values?.Values.FirstOrDefault(x => x.Key == "diagnostics.ai.enabled") is { ValidationError: null, Value.Value: "true" };

    private void BuildDiagnosticAiWorkspace()
    {
        _diagnosticAiInputs.Clear(); _diagnosticAiWorkspace = _diagnosticAiEditor = default; if (_diagnosticAi is null) return;
        _diagnosticAiWorkspaceEnabled = DiagnosticAiEnabled;
        var group = _diagnosticAiWorkspace = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsDiagnosticAi.d.nxml"));
        var nodes = CatalogNodes(group);
        foreach (string id in new[] { "global.privacy.f07ee7c87d0e", "global.privacy.c1b151abf947" })
            if (_catalog?.Entries.FirstOrDefault(x => x.Id == id) is { } entry) BuildRow(nodes["DiagnosticAi.Policy"], entry);
        RefreshDiagnosticAiWorkspace();
    }

    private void RefreshDiagnosticAiWorkspace()
    {
        if (!_shell.Tree.IsAlive(_diagnosticAiWorkspace)) return;
        bool changed = _diagnosticAiWorkspaceEnabled != DiagnosticAiEnabled;
        if (changed)
        {
            CaptureDiagnosticAiDraft();
            foreach (var name in new[] { "ContentWorkspace.AiFacts", "ContentWorkspace.AiRaw", "ContentWorkspace.AiRepair" })
                if (CatalogNodes(_diagnosticAiWorkspace).TryGetValue(name, out var retired)) _contentWorkspaceActions.Remove(retired);
            _diagnosticAiInputs.Clear();
            if (!DiagnosticAiEnabled) ClearDiagnosticAiSecret();
        }
        var slot = CatalogNodes(_diagnosticAiWorkspace)["DiagnosticAi.Editor"];
        var fragment = DiagnosticAiEnabled
            ? DesktopMarkup.Dynamic("SettingsDiagnosticAiEditor.d.nxml", new Dictionary<string, string> { ["enabled"] = Flag(!ContentWorkspaceBusy) })
            : DesktopMarkup.Dynamic("SettingsDiagnosticAiDisabled.d.nxml");
        if (changed || !_shell.Tree.IsAlive(_diagnosticAiEditor))
        {
            _diagnosticAiEditor = _dynamic.Replace(slot, fragment);
            _diagnosticAiWorkspaceEnabled = DiagnosticAiEnabled;
            if (DiagnosticAiEnabled)
            {
                var controls = CatalogNodes(_diagnosticAiEditor);
                foreach (string key in _diagnosticAiDraft.Keys)
                {
                    var input = controls["DiagnosticAi." + key];
                    _shell.Renderer.SetTextInputValue(input, _diagnosticAiDraft[key]); _diagnosticAiInputs[key] = input;
                }
            }
        }
        else _dynamic.Reload(_diagnosticAiEditor, fragment);
        if (!DiagnosticAiEnabled) return;
        var actions = CatalogNodes(_diagnosticAiEditor);
        RegisterWorkspaceAction(actions, "AiFacts", "ai-facts"); RegisterWorkspaceAction(actions, "AiRaw", "ai-raw"); RegisterWorkspaceAction(actions, "AiRepair", "prune");
    }

    private void ClearDiagnosticAiSecret()
    {
        _diagnosticAiDraft["key"] = "";
        if (_diagnosticAiInputs.TryGetValue("key", out var input) && _shell.Tree.IsAlive(input))
            _shell.Renderer.SetTextInputValue(input, "");
    }

    private void CaptureDiagnosticAiDraft()
    {
        foreach (var (key, entity) in _diagnosticAiInputs)
            if (_shell.Tree.IsAlive(entity) && _shell.Tree.GetComponent<XsrUiTextInput>(entity) is { } input)
                _diagnosticAiDraft[key] = input.ReadDraft();
    }

    private DiagnosticAiRequest? ReadDiagnosticAiRequest(string kind)
    {
        if (!DiagnosticAiEnabled) { _feedback.Error(CustomCopy("ai.disabled")); return null; }
        if (!Uri.TryCreate(_diagnosticAiDraft["endpoint"], UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps
            || !int.TryParse(_diagnosticAiDraft["inputBudget"], out int input) || input is < 1 or > 128 * 1024
            || !int.TryParse(_diagnosticAiDraft["outputBudget"], out int output) || output is < 1 or > 4096
            || string.IsNullOrWhiteSpace(_diagnosticAiDraft["model"]))
        { _feedback.Error(CustomCopy("ai.invalidRequest")); return null; }
        string? reasoning = _values?.Values.FirstOrDefault(x => x.Key == "diagnostics.ai.reasoning" && x.ValidationError is null)?.Value.Value;
        if (reasoning is not ("provider" or "low" or "medium" or "high")) { _feedback.Error(CustomCopy("ai.invalidReasoning")); return null; }
        return new(endpoint, _diagnosticAiDraft["model"], _diagnosticAiDraft["key"],
            kind == "ai-raw" ? DiagnosticAiDataScope.RedactedRawText : DiagnosticAiDataScope.StructuredFacts, "", input, output)
        { Reasoning = reasoning switch { "low" => DiagnosticAiReasoning.Low, "medium" => DiagnosticAiReasoning.Medium, "high" => DiagnosticAiReasoning.High, _ => DiagnosticAiReasoning.Provider } };
    }

    private async Task<ContentWorkspaceResult> PreviewDiagnosticAiAsync(DiagnosticAiRequest request, string? raw, CancellationToken token)
    {
        var preview = request.DataScope == DiagnosticAiDataScope.RedactedRawText ? await _contentBackups!.PreviewRawAsync(raw ?? "", token).ConfigureAwait(false)
            : await _contentBackups!.PreviewFactsAsync(await _diagnosticHistory!.ReadAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
        if (preview.Utf8Bytes > request.MaximumInputBytes) throw new IOException(CustomRaw("ai.previewBudget"));
        request = request with { PreviewRevision = preview.Revision };
        return new(AiConfirmation: new(request, preview), Apply: async cancellation =>
            {
                var suggestion = await _diagnosticAi!.SuggestAsync(request, preview, request.DataScope, cancellation).ConfigureAwait(false);
                return new(AiSuggestion: suggestion);
            });
    }

    // Called only by the UI-frame dispatcher; worker code never invokes the renderer localizer.
    private string FormatDiagnosticAiConfirmation(DiagnosticAiConfirmation confirmation)
    {
        var request = confirmation.Request; var preview = confirmation.Preview;
        return CustomFormat("AiConfirmation", new()
        {
            ["endpoint"] = request.Endpoint.ToString(),
            ["scope"] = CustomCopy(request.DataScope == DiagnosticAiDataScope.RedactedRawText ? "ai.raw" : "ai.facts"),
            ["model"] = request.Model,
            ["reasoning"] = request.Reasoning.ToString(),
            ["bytes"] = preview.Utf8Bytes.ToString(CultureInfo.CurrentCulture),
            ["tokens"] = request.MaximumOutputTokens.ToString(CultureInfo.CurrentCulture),
            ["preview"] = preview.RedactedText,
        });
    }

    private void PresentDiagnosticAiSuggestion(DiagnosticAiSuggestion suggestion)
    {
        string message = CustomFormat("AiSuggestion", new() { ["suggestion"] = suggestion.Text });
        _contentWorkspaceMessage = message;
        _diagnosticAiResultDialog = _feedback.ShowMessageDialog("settings.diagnostic-ai-result", CustomCopy("ai.title"), message, CustomCopy("ai.acknowledge"), localizeMessage: false);
    }
}
