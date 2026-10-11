using Nexa.Services.Settings;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static ValueTask InstanceDevelopmentModeIsLocalAndRecoveryIndependent()
    {
        var (_, settings) = PolicyFixture();
        const string key = "instance.development-mode";
        string first = Path.Combine(Path.GetTempPath(), "dev-mode-a", "versions", "same");
        string second = Path.Combine(Path.GetTempPath(), "dev-mode-b", "versions", "same");
        AssertEqual("false", Effective(settings, key, first).Value.Value);
        AssertFalse(settings.Set(new(key, SettingsLayer.Global, new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
        AssertFalse(settings.Set(new(key, SettingsLayer.Profile, new(SettingsOverrideMode.Custom, "true"), first) { ProfileId = "test" }).IsSuccess);
        var invalidProfile = new Dictionary<string, SettingsOverride> { [key] = new(SettingsOverrideMode.Custom, "true") };
        long revision = settings.Read(new(first)).Value!.Revision;
        AssertFalse(settings.SaveLaunchProfile(new(first, "test", "Test", invalidProfile, new(), revision)).IsSuccess);
        AssertFalse(settings.BeginTemporaryLaunch(new(first, "test", invalidProfile, new(), revision)).IsSuccess);
        AssertTrue(settings.Set(new(key, SettingsLayer.Instance, new(SettingsOverrideMode.Custom, "true"), first)).IsSuccess);
        AssertEqual("true", Effective(settings, key, first).Value.Value);
        AssertEqual("false", Effective(settings, key, second).Value.Value);
        AssertEqual("false", Effective(settings, key).Value.Value);
        AssertFalse(SettingsPolicySchema.ByKey[key].Exportable);
        string baseline = settings.CaptureRecoverySettings(first);
        AssertFalse(baseline.Contains(key, StringComparison.Ordinal));
        AssertTrue(settings.Set(new(key, SettingsLayer.Instance, new(SettingsOverrideMode.Custom, "false"), first)).IsSuccess);
        var preview = settings.PreviewRecoverySettings(first, baseline);
        AssertEqual(0, preview.Errors.Count);
        AssertFalse(preview.Changes.Any(item => item.Key == key));
        return ValueTask.CompletedTask;
    }
}
