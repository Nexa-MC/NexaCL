using System.Text.Json.Nodes;
using Nexa.Xsr;

namespace Nexa.Services.Settings;

public sealed partial class SettingsPolicyService : ISettingsRenameTransactions
{
    public ValueTask<XsrResult<SettingsRenameReceipt>> PrepareRenameAsync(string source, string destination, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var plan = PrepareRenameSettings(source, destination);
            return ValueTask.FromResult(XsrResult.Success(new SettingsRenameReceipt(plan.Source, plan.Destination,
                SettingsRenameIdentity.Capture(plan.Value, plan.Profiles))));
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { return ValueTask.FromResult(XsrResult.Failure<SettingsRenameReceipt>(Invalid(error.Message))); }
    }
    public ValueTask<XsrResult> ApplyRenameAsync(SettingsRenameReceipt receipt, bool reverse, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ApplyRenameIdentity(receipt, reverse));
    }
    private XsrResult ApplyRenameIdentity(SettingsRenameReceipt receipt, bool reverse)
    {
        try
        {
            lock (_profileGate)
            {
                if (receipt.Identity.Length != 64 || !receipt.Identity.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid rename settings identity.");
                var snapshot = _settings.ReadBatch();
                if (_settings.LoadError is not null) throw new IOException("无法读取实例设置。");
                string from = InstanceKey(receipt.Source)!, to = InstanceKey(receipt.Destination)!;
                if (from == to) return XsrResult.Success();
                if (_temporaryProfiles.ContainsKey(from) || _temporaryProfiles.ContainsKey(to))
                    throw new IOException("实例在改名期间启用了临时启动覆盖，请先撤销后重试。");
                var document = ReadDocument(snapshot.Values); var instances = (JsonObject)document["instances"]!; var profiles = ProfileInstances(document);
                bool before = instances[to] is null && profiles[to] is null && Matches(instances[from], profiles[from], receipt.Identity);
                bool after = instances[from] is null && profiles[from] is null && Matches(instances[to], profiles[to], receipt.Identity);
                if (reverse ? before : after) return XsrResult.Success();
                if (!(reverse ? after : before)) throw new IOException("实例设置在改名期间发生变化，未覆盖后续修改。");
                string existing = reverse ? to : from, target = reverse ? from : to;
                var value = instances[existing]?.DeepClone(); var profileValue = profiles[existing]?.DeepClone();
                instances.Remove(from); instances.Remove(to); profiles.Remove(from); profiles.Remove(to);
                if (value is not null) instances[target] = value;
                if (profileValue is not null) profiles[target] = profileValue;
                return _settings.SetRawValues(new Dictionary<string, string>
                { [SettingsPolicySchema.StorageKey] = document.ToJsonString() }, snapshot.Revision);
            }
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { return XsrResult.Failure(Invalid(error.Message)); }
    }
    private static bool Matches(JsonNode? values, JsonNode? profiles, string identity) =>
        string.Equals(SettingsRenameIdentity.Capture(values?.ToJsonString(), profiles?.ToJsonString()), identity, StringComparison.OrdinalIgnoreCase);
}
