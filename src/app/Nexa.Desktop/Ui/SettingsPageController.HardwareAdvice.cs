using System.Globalization;
using Nexa.Services.Capabilities;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private MachineCapabilitySnapshot? _hardwareAdviceSource;
    private Task<XsrResult<CapabilityPreflightReport>>? _hardwareAdviceRead;
    private CancellationTokenSource? _hardwareAdviceStop;
    private CapabilityPreflightReport? _hardwareAdviceReport;
    private void RetireHardwareAdvice()
    {
        _hardwareAdviceStop?.Cancel(); _hardwareAdviceStop?.Dispose(); _hardwareAdviceStop = null;
        _hardwareAdviceRead = null; _hardwareAdviceReport = null; _hardwareAdviceSource = null;
    }
    private void UpdateHardwareAdvice()
    {
        if (!_visible || _selected != "platform" || _machine is not { } source) { RetireHardwareAdvice(); return; }
        if (!ReferenceEquals(_hardwareAdviceSource, source))
        {
            RetireHardwareAdvice(); _hardwareAdviceSource = source;
            if (_queries.TryResolve(MachineCapabilityStateContract.PreflightQuery, out var route))
            {
                _hardwareAdviceStop = new();
                _hardwareAdviceRead = _queries.QueryAsync<LaunchPreflightQuery, CapabilityPreflightReport>(route,
                    new(source), cancellationToken: _hardwareAdviceStop.Token).AsTask();
                WakeOnPlatformCompletion(_hardwareAdviceRead);
            }
        }
        if (_hardwareAdviceRead is not { IsCompleted: true } read) return;
        _hardwareAdviceRead = null; _hardwareAdviceStop?.Dispose(); _hardwareAdviceStop = null;
        if (ReferenceEquals(_hardwareAdviceSource, _machine) && PendingQuery.Succeeded(read))
            _hardwareAdviceReport = read.Result.Value;
        BuildSections();
    }
    private void BuildHardwareAdvice()
    {
        if (_machine is not { } source || !_machineScope.HasInstanceScope || MemoryAnalysisUnavailable) return;
        var report = ReferenceEquals(_hardwareAdviceSource, source) ? _hardwareAdviceReport : null;
        var facts = CaptureHardwareAdvice(source, report, _shell.Renderer.LocalizeText);
        _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsHardwareAdvice.d.nxml", new Dictionary<string, string>
        {
            ["shader"] = facts.Shader,
            ["packs"] = facts.ResourcePacks,
            ["distance"] = facts.RenderDistance,
            ["risk"] = facts.Risk,
            ["resourceRisk"] = Flag(facts.ResourceRisk),
            ["safe"] = Flag(!facts.ResourceRisk)
        }));
    }

    internal sealed record HardwareAdviceSnapshot(string Shader, string ResourcePacks, string RenderDistance, string Risk, bool ResourceRisk);
    internal static HardwareAdviceSnapshot CaptureHardwareAdvice(MachineCapabilitySnapshot source, CapabilityPreflightReport? report,
        Func<string, string>? localize = null)
    {
        string L(string key) => localize?.Invoke(PlatformTerm(key)) ?? PlatformTerm(key);
        string Format(string template, Dictionary<string, string> data)
        {
            var node = DesktopMarkup.Dynamic(template, data, localize).Root;
            if (node.Recipe != Nexa.Xsr.Renderer.Nxml.NxmlRuntimeRecipe.Text || node.Content is null || node.Children.Count != 0)
                throw new Nexa.Xsr.Renderer.Nxml.NxmlCompileException("Advice format must be text-only.");
            return node.Localize ? localize?.Invoke(node.Content) ?? node.Content : node.Content;
        }
        string Estimate(string id) => source.Get<long>(id) is { Availability: CapabilityAvailability.Available, Value: >= 0 } value
            ? Format("HardwareAdviceTextEstimate.d.nxml", new Dictionary<string, string>
            { ["mib"] = value.Value.ToString(CultureInfo.InvariantCulture), ["confidence"] = value.Confidence.ToString() }) : L("hardware.estimateUnavailable");
        bool resourceRisk = report?.CollapsedIssues.Any(issue => (issue.Category is "memory" or "gpu" or "resource")
            && issue.Severity >= PreflightSeverity.Warning) == true;
        string shaderInput = source.Get<long>("resource.shader.estimated_gpu_memory") is { Availability: CapabilityAvailability.Available }
            ? L("hardware.shaderInput") : L("hardware.shaderUnknown");
        string shader = Format("HardwareAdviceTextShader.d.nxml", new Dictionary<string, string>
        { ["budget"] = Estimate("estimate.graphics.shader"), ["input"] = shaderInput });
        string enabledPacks = source.Get<IReadOnlyList<string>>("minecraft.settings.resource_packs") is { Availability: CapabilityAvailability.Available, Value: { } enabled }
            ? Format("HardwareAdviceTextPackCount.d.nxml", new Dictionary<string, string> { ["count"] = enabled.Count.ToString(CultureInfo.InvariantCulture) }) : L("hardware.packsUnknown");
        string packs = Format("HardwareAdviceTextPacks.d.nxml", new Dictionary<string, string>
        { ["enabled"] = enabledPacks, ["peak"] = Estimate("estimate.resource.load_peak") });
        string distance = source.Get<int>("minecraft.settings.render_distance") is { Availability: CapabilityAvailability.Available, Value: >= 0 } current
            ? Format("HardwareAdviceTextDistance.d.nxml", new Dictionary<string, string> { ["chunks"] = current.Value.ToString(CultureInfo.InvariantCulture) }) : L("hardware.distanceUnknown");
        string risk = report is null ? L("hardware.riskPending") : report.OverallSeverity == PreflightSeverity.None
            ? L("hardware.riskNone") : Format("HardwareAdviceTextRisk.d.nxml", new Dictionary<string, string>
            {
                ["severity"] = localize?.Invoke(SeverityLabel(report.OverallSeverity)) ?? SeverityLabel(report.OverallSeverity),
                ["codes"] = string.Join(PlatformTerm("separator"), report.CollapsedIssues.Take(8).Select(issue => issue.Code)
                    .Where(code => code.Length <= 128 && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-')))
            });
        return new(shader, packs, distance, risk, resourceRisk);
    }
}
