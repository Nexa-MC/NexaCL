using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Capabilities;
using Nexa.Services.Logging;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private async ValueTask<CapabilityPreflightReport> InstanceDiagnostics(HostInvocation c, EntityId instance, CancellationToken token)
    {
        var item = FindInstance(c, instance);
        var facts = await Query<MachineCapabilityQuery, MachineCapabilitySnapshot>(foundationQueries, MachineCapabilityStateContract.SnapshotQuery,
            new(item.Instance.DirectoryPath, item.Instance.Id, item.Snapshot.RootDirectory) { RefreshInstance = true }, token).ConfigureAwait(false);
        return await Query<LaunchPreflightQuery, CapabilityPreflightReport>(foundationQueries, MachineCapabilityStateContract.PreflightQuery, new(facts), token).ConfigureAwait(false);
    }
    private static QualifiedId FindingId(CapabilityPreflightIssue issue) => new("nexa:findings/" + issue.Code);
    private static string ScopedRepair(CapabilityPreflightIssue issue)
    {
        // These handlers require an exact instance and perform their own running-game and file checks.
        string[] supported = ["remediation.game.repair_files", "remediation.game.repair_version", "remediation.loader.repair", "remediation.java.switch_recommended", "remediation.java.download"];
        return issue.Remediations.FirstOrDefault(supported.Contains) ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
    }
    private IEnumerable<HostCapabilityRoute> DiagnosticRoutes => [
        HostRoutes.Typed<EntityRequest, DiagnosticAnalysis>("Minecraft.Diagnostics", "Analyze", async (c, v) =>
        {
            var report = await InstanceDiagnostics(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            if (report.Issues.Count > 128) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var reports = report.Issues.Select(i => new DiagnosticReport(FindingId(i).Value,
                new(LogRedactor.Redact(i.Title + ": " + i.Description), []), i.Severity switch
                {
                    PreflightSeverity.Blocked or PreflightSeverity.Critical => DiagnosticSeverity.Error,
                    PreflightSeverity.Warning => DiagnosticSeverity.Warning,
                    _ => DiagnosticSeverity.Info
                }, c.Options.CorrelationId, c.Options.OperationId)).ToImmutableArray();
            return new(v.Id, reports, report.Issues.Select(i => Issue(i.Code, LogRedactor.Redact(i.Description), v.Id.Value,
                i.Severity == PreflightSeverity.Blocked)).ToImmutableArray());
        }),
        HostRoutes.Typed<RepairRequest, MutationPlan>("Minecraft.Diagnostics", "PreviewRepair", async (c, v) =>
        {
            var instance = FindInstance(c, v.Instance);
            if (instance.Summary.Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
            if (v.Findings.Length is < 1 or > 16 || v.Findings.Distinct().Count() != v.Findings.Length) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var settings = await ReadSettings(instance.Instance.DirectoryPath, c.Options.Cancellation).ConfigureAwait(false);
            var report = await InstanceDiagnostics(c, v.Instance, c.Options.Cancellation).ConfigureAwait(false);
            var selected = v.Findings.Select(id => report.Issues.SingleOrDefault(i => FindingId(i) == id) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID)).ToArray();
            var repairs = selected.Select(ScopedRepair).Distinct(StringComparer.Ordinal).ToArray();
            async ValueTask Validate(CancellationToken token)
            {
                if (FindInstance(c, v.Instance).Summary.Revision != v.ExpectedRevision || (await ReadSettings(instance.Instance.DirectoryPath, token).ConfigureAwait(false)).Revision != settings.Revision)
                    throw new AbiHostException(AbiErrors.CONFLICT);
                var current = await InstanceDiagnostics(c, v.Instance, token).ConfigureAwait(false);
                foreach (var finding in selected)
                {
                    var now = current.Issues.SingleOrDefault(i => FindingId(i) == FindingId(finding));
                    if (now is null || ScopedRepair(now) != ScopedRepair(finding)) throw new AbiHostException(AbiErrors.CONFLICT);
                }
            }
            return Plan(c, "diagnostics/repair", RepairRequest.Write(v), "Repair " + instance.Summary.Name + ": " + string.Join(", ", repairs), Validate, async token =>
            {
                foreach (string repair in repairs)
                    await Command(foundationCommands, MachineCapabilityStateContract.RemediationCommand,
                        new RemediationRequest(repair, new Dictionary<string, string>(StringComparer.Ordinal) { ["instanceDirectory"] = instance.Instance.DirectoryPath }, true), token).ConfigureAwait(false);
                return Empty.Write(new());
            });
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Minecraft.Diagnostics", "ExecuteRepair", (c, v) => ValueTask.FromResult(ExecutePlan(c, v,
            "diagnostics/repair", "nexa.minecraft.diagnostics.write", Abi100Schemas.Empty)))
    ];
}
