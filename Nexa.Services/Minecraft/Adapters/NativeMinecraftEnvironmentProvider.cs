#if NEXA_NATIVE_SERVICES
using Nexa.Native.Minecraft.Client;
using Nexa.Services.Minecraft;

namespace Nexa.Services.Capabilities;

internal sealed class NativeMinecraftEnvironmentProvider(Func<NativeMinecraftClient> client, MinecraftEnvironmentProvider provider, string? root) : IMachineCapabilityProvider
{
    public string Id => provider switch
    {
        MinecraftEnvironmentProvider.Java => MachineInstanceCatalog.JavaProviderId,
        MinecraftEnvironmentProvider.Files => MachineInstanceCatalog.MinecraftProviderId,
        MinecraftEnvironmentProvider.Loader => MachineInstanceCatalog.LoaderProviderId,
        MinecraftEnvironmentProvider.Mods => ModCatalog.ProviderId,
        _ => throw new InvalidDataException("Unknown Minecraft capability provider.")
    };
    public ValueTask<IReadOnlyList<ICapability>> CollectAsync(DateTimeOffset timestamp, CancellationToken cancellationToken)
        => CollectAsync(timestamp, new(), cancellationToken);
    public async ValueTask<IReadOnlyList<ICapability>> CollectAsync(DateTimeOffset timestamp, MachineCapabilityQuery query, CancellationToken cancellationToken)
    {
        var facts = await client().ReadEnvironmentFactsAsync(new(provider, root, query, timestamp), cancellationToken).ConfigureAwait(false);
        var definitions = MachineInstanceCatalog.MergeInto(new CapabilityRegistry([])).Definitions
            .Concat(ModCatalog.Definitions()).Append(ResourceHistoryCatalog.SettingsFingerprint).ToDictionary(item => item.Id, StringComparer.Ordinal);
        return Array.AsReadOnly(facts.Items.Select(item => Decode(item, definitions)).ToArray());
    }
    private static ICapability Decode(MinecraftEnvironmentFact value, Dictionary<string, ICapabilityDefinition> definitions)
    {
        if (!definitions.TryGetValue(value.Id, out var definition)) throw new InvalidDataException("Unknown Minecraft observation definition.");
        return (definition, value.Kind) switch
        {
            (CapabilityDefinition<bool> typed, MinecraftCapabilityValueKind.Boolean) => new Capability<bool>(typed, value.Boolean, value.Availability, value.Confidence, value.Timestamp, value.Source, value.Reason, value.Permission),
            (CapabilityDefinition<int> typed, MinecraftCapabilityValueKind.SmallInteger) => new Capability<int>(typed, value.SmallInteger, value.Availability, value.Confidence, value.Timestamp, value.Source, value.Reason, value.Permission),
            (CapabilityDefinition<long> typed, MinecraftCapabilityValueKind.LargeInteger) => new Capability<long>(typed, value.LargeInteger, value.Availability, value.Confidence, value.Timestamp, value.Source, value.Reason, value.Permission),
            (CapabilityDefinition<string> typed, MinecraftCapabilityValueKind.Text) => new Capability<string>(typed, value.Text, value.Availability, value.Confidence, value.Timestamp, value.Source, value.Reason, value.Permission),
            (CapabilityDefinition<IReadOnlyList<string>> typed, MinecraftCapabilityValueKind.TextList) => new Capability<IReadOnlyList<string>>(typed, value.TextList, value.Availability, value.Confidence, value.Timestamp, value.Source, value.Reason, value.Permission),
            _ => throw new InvalidDataException("Minecraft observation kind does not match its local definition.")
        };
    }
}
#endif
