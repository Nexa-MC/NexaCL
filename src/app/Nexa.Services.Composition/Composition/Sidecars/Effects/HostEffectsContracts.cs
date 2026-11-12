using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostEmptyRequest;
public sealed record HostAccountRequest(int Index, long ExpectedRosterRevision);
public sealed record HostAccountEligibility(bool Allowed, bool CheckSession);
public sealed record HostJavaInspectRequest(string Executable);
public sealed record HostJavaSelectRequest(string? InstanceDirectory, bool VersionLocal);
public sealed record HostJavaRequirementsRequest(string InstanceDirectory);
public sealed record HostJavaCandidate(JavaRuntimeCandidate? Candidate);
public sealed record HostPowerState(bool OnBattery, int? BatteryPercent, bool Saving);
public sealed record HostPreflightRequest(string InstanceDirectory, string InstanceId, string MinecraftRootDirectory);
public sealed record HostSettingsDefinitions(IReadOnlyList<SettingsPolicyDefinition> Definitions, string InstanceJavaRuntime);
public sealed record HostSettingsValidateRequest(string Key, SettingsOverride Value);
public sealed record HostSettingsValidation(bool Valid, string? Error);

public sealed record HostPreflightIssue(string Code, string Title, string Description, PreflightSeverity Severity,
    string Category, PreflightCertainty Certainty, bool HardConstraint, IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Causes, IReadOnlyList<string> Remediations, bool CanBypass, bool Suppressible);
public sealed record HostPreflightReport(IReadOnlyList<HostPreflightIssue> Issues, IReadOnlyList<HostPreflightIssue> CollapsedIssues, PreflightSeverity OverallSeverity);

public sealed record HostWorldSnapshotsRequest(string InstanceDirectory, string WorldName);
public sealed record HostWorldSnapshots(IReadOnlyList<InstanceWorldSnapshot> Items);
