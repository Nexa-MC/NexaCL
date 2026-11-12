using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static async Task ExercisePluginLaunch(Func<string, string, AbiValue, ValueTask<AbiReply<AbiValue>>> invoke, EntityId instance, EntityId account, RecordingStartRoute recording)
    {
        var eligibility = await invoke("Accounts", "ReadLaunchEligibility", EntityRequest.Write(new(account))); AssertTrue(eligibility.Success);
        AssertTrue(LaunchEligibility.Read(eligibility.Value).Allowed);
        var profileReply = await invoke("Minecraft.Launch", "ReadEffectiveProfile", EntityRequest.Write(new(instance))); AssertTrue(profileReply.Success);
        var profile = LaunchProfile.Read(profileReply.Value);
        var changed = profile with { Id = new("new-profile"), Name = "Plugin launch", Width = 1280, Height = 720, Arguments = [new(new("example.advanced:argument"), "-Dexample=a b", LaunchArgumentScope.Jvm)] };
        AssertFalse((await invoke("Minecraft.Launch", "SaveProfile", LaunchProfileWrite.Write(new(instance, changed with { Arguments = [changed.Arguments[0] with { Value = "" }] }, profile.Revision)))).Success);
        var saved = await invoke("Minecraft.Launch", "SaveProfile", LaunchProfileWrite.Write(new(instance, changed, profile.Revision))); AssertTrue(saved.Success);
        var savedProfile = LaunchProfile.Read(saved.Value); AssertEqual(1280L, savedProfile.Width); AssertEqual("-Dexample=a b", savedProfile.Arguments.Single().Value);
        AssertFalse((await invoke("Minecraft.Launch", "SaveProfile", LaunchProfileWrite.Write(new(instance, changed, profile.Revision)))).Success);
        var granted = await invoke("Accounts", "RequestLaunchGrant", GrantRequest.Write(new(account, instance))); AssertTrue(granted.Success);
        var grant = LaunchGrant.Read(granted.Value);
        var intent = new LaunchIntent(instance, grant, savedProfile.Id, null);
        AssertFalse((await invoke("Minecraft.Launch", "Prepare", LaunchIntent.Write(intent with { Grant = grant with { Token = Guid.NewGuid() } }))).Success);
        var prepared = await invoke("Minecraft.Launch", "Prepare", LaunchIntent.Write(intent)); AssertTrue(prepared.Success); var plan = LaunchPlan.Read(prepared.Value);
        var validate = await invoke("Minecraft.Launch", "ValidatePlan", PlanRequest.Write(new(plan.Plan.Id, plan.Plan.Revision, null))); AssertTrue(validate.Success);
        AssertTrue(ValidationReport.Read(validate.Value).Valid);
        var watched = await invoke("Minecraft.Launch", "WatchAttempt", WatchRequest.Write(new(plan.Plan.Id, null, Abi100Schemas.LaunchAttempt))); AssertTrue(watched.Success);
        var started = await invoke("Minecraft.Launch", "Start", PlanRequest.Write(new(plan.Plan.Id, plan.Plan.Revision, null))); AssertTrue(started.Success);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var read = await invoke("Minecraft.Launch", "ReadAttempt", EntityRequest.Write(new(plan.Plan.Id))); AssertTrue(read.Success);
            var attempt = LaunchAttempt.Read(read.Value);
            if (attempt.State == OperationState.Completed) { AssertTrue(attempt.Session is not null); break; }
            AssertFalse(attempt.State is OperationState.Failed or OperationState.OutcomeUnknown or OperationState.Cancelled);
            await Task.Delay(5, timeout.Token);
        }
        AssertTrue(recording.LastCommand!.ExpectedAccountRosterRevision.HasValue);
        AssertTrue(recording.LastCommand.ExpectedSettingsRevision.HasValue);
        AssertTrue(recording.LastCommand.LaunchProfileId is not null);
        AssertFalse((await invoke("Minecraft.Launch", "Start", PlanRequest.Write(new(plan.Plan.Id, plan.Plan.Revision, null)))).Success);
        AssertFalse((await invoke("Minecraft.Launch", "Prepare", LaunchIntent.Write(intent))).Success);
        AssertFalse((await invoke("Minecraft.Launch", "Cancel", OperationRequest.Write(new(new("another-operation"))))).Success);
    }
}
