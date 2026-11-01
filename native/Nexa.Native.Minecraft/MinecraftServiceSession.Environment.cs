using Nexa.Native.Services.Wire;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private static async Task<byte[]> MigrationReceiptAsync(int operation, ServiceWireReader reader, CancellationToken token)
    {
        string path = reader.String() ?? throw new InvalidDataException("Missing receipt path."); byte[] bytes = reader.Buffer(); reader.End();
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Migration receipt exceeds its byte budget.");
        if (operation == Nexa.Native.Minecraft.Services.MinecraftServiceProtocol.MigrationReceiptAuthorize)
            await Nexa.Services.Minecraft.Management.RecoveryRecordAuthority.AuthorizeAsync(path, bytes, token).ConfigureAwait(false);
        else { token.ThrowIfCancellationRequested(); Nexa.Services.Minecraft.Management.RecoveryRecordAuthority.Verify(path, bytes); }
        return [];
    }
    private static async Task<byte[]> ReadLocalDocumentsAsync(ServiceWireReader reader, CancellationToken token)
    {
        var query = reader.InstanceLocalDocumentsQuery() ?? throw new InvalidDataException("Missing local document query."); reader.End();
        using var writer = new ServiceWireWriter(); writer.InstanceLocalDocumentsSnapshot(await Nexa.Services.Minecraft.Management.InstanceLocalDocumentsService.ReadAsync(query, token).ConfigureAwait(false)); return writer.Finish();
    }
    private async Task<byte[]> ReadEnvironmentFactsAsync(ServiceWireReader reader, CancellationToken token)
    {
        var query = reader.MinecraftEnvironmentFactsQuery() ?? throw new InvalidDataException("Missing Minecraft capability query."); reader.End();
        IMachineCapabilityProvider provider = query.Provider switch
        {
            MinecraftEnvironmentProvider.Java => new JavaEnvironmentCapabilityProvider(_javaLocator!, query.DefaultRoot) { SharedStateCache = _cache },
            MinecraftEnvironmentProvider.Files => new MinecraftEnvironmentCapabilityProvider(query.DefaultRoot) { SharedStateCache = _cache },
            MinecraftEnvironmentProvider.Loader => new LoaderCapabilityProvider(query.DefaultRoot) { SharedStateCache = _cache, CatalogSource = _installationCatalogSource ?? new HttpInstallCatalogSource(_metadataHttp) },
            MinecraftEnvironmentProvider.Mods => new ModCapabilityProvider(query.DefaultRoot) { SharedStateCache = _cache },
            _ => throw new InvalidDataException("Unknown Minecraft capability provider.")
        };
        var observations = await provider.CollectAsync(query.Timestamp, query.Scope, token).ConfigureAwait(false);
        using var writer = new ServiceWireWriter(); writer.MinecraftEnvironmentFacts(new(observations.Select(EncodeEnvironmentFact).ToArray())); return writer.Finish();
    }
    private static MinecraftEnvironmentFact EncodeEnvironmentFact(ICapability observation)
    {
        (MinecraftCapabilityValueKind Kind, bool Boolean, int SmallInteger, long LargeInteger, string? Text, IReadOnlyList<string>? TextList) scalar = observation switch
        {
            Capability<bool> value => (MinecraftCapabilityValueKind.Boolean, value.Value, 0, 0, null, null),
            Capability<int> value => (MinecraftCapabilityValueKind.SmallInteger, false, value.Value, 0, null, null),
            Capability<long> value => (MinecraftCapabilityValueKind.LargeInteger, false, 0, value.Value, null, null),
            Capability<string> value => (MinecraftCapabilityValueKind.Text, false, 0, 0, value.Value, null),
            Capability<IReadOnlyList<string>> value => (MinecraftCapabilityValueKind.TextList, false, 0, 0, null, value.Value),
            _ => throw new InvalidDataException("Unsupported Minecraft observation value.")
        };
        return new(observation.Id, scalar.Kind, scalar.Boolean, scalar.SmallInteger, scalar.LargeInteger, scalar.Text, scalar.TextList,
            observation.Availability, observation.Confidence, observation.Permission, observation.Timestamp, observation.Source, observation.Reason);
    }
}
