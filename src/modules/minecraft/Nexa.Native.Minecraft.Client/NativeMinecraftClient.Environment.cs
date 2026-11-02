using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Capabilities;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    public Task AuthorizeMigrationReceiptAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken token = default) => MigrationReceiptAsync(MinecraftServiceProtocol.MigrationReceiptAuthorize, path, bytes, token);
    public Task VerifyMigrationReceiptAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken token = default) => MigrationReceiptAsync(MinecraftServiceProtocol.MigrationReceiptVerify, path, bytes, token);
    private async Task MigrationReceiptAsync(int operation, string path, ReadOnlyMemory<byte> receipt, CancellationToken token)
    {
        if (receipt.Length > 64 * 1024 * 1024) throw new InvalidDataException("Migration receipt exceeds its byte budget.");
        byte[] bytes = await InvokeAsync(operation, writer => { writer.String(path); writer.Buffer(receipt); }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    public async Task<Nexa.Services.Minecraft.Management.InstanceLocalDocumentsSnapshot> ReadLocalDocumentsAsync(Nexa.Services.Minecraft.Management.InstanceLocalDocumentsQuery query, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.LocalDocuments, writer => writer.InstanceLocalDocumentsQuery(query), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var value = reader.InstanceLocalDocumentsSnapshot() ?? throw new InvalidDataException("Missing local documents."); reader.End(); return value;
    }
    public async Task<MinecraftEnvironmentFacts> ReadEnvironmentFactsAsync(MinecraftEnvironmentFactsQuery query, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.EnvironmentFacts, writer => writer.MinecraftEnvironmentFactsQuery(query), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var value = reader.MinecraftEnvironmentFacts() ?? throw new InvalidDataException("Missing Minecraft environment facts."); reader.End(); return value;
    }
}
