using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static async Task PreparePluginWorldFixture(string instance)
    {
        string world = Path.Combine(instance, "saves", "Fixture"); Directory.CreateDirectory(Path.Combine(world, "datapacks"));
        await File.WriteAllTextAsync(Path.Combine(world, "datapacks", "fixture.zip"), "fixture");
        using var body = new MemoryStream();
        void Text(string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length); body.Write(length); body.Write(bytes); }
        void Header(byte type, string name) { body.WriteByte(type); Text(name); }
        Header(10, ""); Header(10, "Data"); Header(8, "LevelName"); Text("Plugin world");
        Header(3, "DataVersion"); byte[] version = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(version, 3955); body.Write(version);
        Header(10, "DataPacks"); Header(9, "Enabled"); body.WriteByte(8); body.Write(new byte[] { 0, 0, 0, 1 }); Text("vanilla");
        Header(9, "Disabled"); body.WriteByte(8); body.Write(new byte[] { 0, 0, 0, 1 }); Text("file/fixture.zip");
        body.WriteByte(0); body.WriteByte(0); body.WriteByte(0);
        using var output = new MemoryStream(); using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(body.ToArray());
        await File.WriteAllBytesAsync(Path.Combine(world, "level.dat"), output.ToArray());
        Directory.CreateDirectory(Path.Combine(instance, "screenshots"));
        await File.WriteAllTextAsync(Path.Combine(instance, "screenshots", "fixture.png"), "unreadable preview still has a content identity");
    }
    private static async Task ExercisePluginWorldContent(Func<string, string, AbiValue, ValueTask<AbiReply<AbiValue>>> invoke, EntityId instance, string path)
    {
        var listing = await invoke("Minecraft.Worlds", "List", EntityRequest.Write(new(instance))); AssertTrue(listing.Success);
        var world = Page<WorldSummary>.Read(listing.Value).Items.Single(); AssertEqual("Plugin world", world.Name); AssertFalse(world.Running);
        AssertTrue((await invoke("Minecraft.Worlds", "ReadMetadata", EntityRequest.Write(new(world.Id)))).Success);
        AssertFalse((await invoke("Minecraft.Worlds", "Read", EntityRequest.Write(new(new("forged-world"))))).Success);
        var renamed = await invoke("Minecraft.Worlds", "Rename", WorldRenameRequest.Write(new(world.Id, "Renamed plugin world", world.Revision))); AssertTrue(renamed.Success);
        AssertFalse((await invoke("Minecraft.Worlds", "Rename", WorldRenameRequest.Write(new(world.Id, "Stale rename", world.Revision)))).Success);
        world = WorldSummary.Read(renamed.Value); AssertEqual("Renamed plugin world", world.Name);
        var packs = Page<ContentItem>.Read((await invoke("Minecraft.Worlds", "ListDataPacks", EntityRequest.Write(new(world.Id)))).Value);
        var selection = new DataPackSelection(world.Id, [], packs.Items.Select(p => new QualifiedId("nexa:world-datapacks/" + p.Id.Value)).ToImmutableArray(), world.Revision);
        var preview = await invoke("Minecraft.Worlds", "PlanSetDataPack", DataPackSelection.Write(selection)); AssertTrue(preview.Success);
        var plan = MutationPlan.Read(preview.Value);
        await Complete("Minecraft.Worlds", "ApplyDataPackPlan", PlanRequest.Write(new(plan.Id, plan.Revision, null)));
        packs = Page<ContentItem>.Read((await invoke("Minecraft.Worlds", "ListDataPacks", EntityRequest.Write(new(world.Id)))).Value); AssertTrue(packs.Items.All(p => p.Enabled));
        AssertFalse((await invoke("Minecraft.Worlds", "PlanSetDataPack", DataPackSelection.Write(selection))).Success);
        var content = await invoke("Minecraft.Content", "List", ContentQuery.Write(new(instance, new("nexa:content/screenshots"), new(null, 32)))); AssertTrue(content.Success);
        var item = Page<ContentItem>.Read(content.Value).Items.Single();
        var integrity = await invoke("Minecraft.Content", "ReadIntegrity", EntityRequest.Write(new(item.Id))); AssertTrue(integrity.Success);
        AssertFalse(IntegrityReport.Read(integrity.Value).Valid); // A missing baseline is not verified integrity.
        var removal = await invoke("Minecraft.Content", "PlanRemove", ContentChangeRequest.Write(new(instance, [item.Id], [], item.Revision))); AssertTrue(removal.Success);
        var removePlan = ContentPlan.Read(removal.Value).Plan;
        await Complete("Minecraft.Content", "Execute", PlanRequest.Write(new(removePlan.Id, removePlan.Revision, null)));
        AssertFalse(File.Exists(Path.Combine(path, "screenshots", "fixture.png")));
        var trash = Page<ContentItem>.Read((await invoke("Minecraft.Content", "ListTrash", EntityRequest.Write(new(instance)))).Value).Items.Single();
        await Complete("Minecraft.Content", "RestoreTrash", ContentChangeRequest.Write(new(instance, [trash.Id], [], trash.Revision)));
        AssertTrue(File.Exists(Path.Combine(path, "screenshots", "fixture.png")));
        AssertFalse((await invoke("Minecraft.Content", "Execute", PlanRequest.Write(new(removePlan.Id, removePlan.Revision, null)))).Success);
        async Task Complete(string group, string method, AbiValue input)
        {
            var response = await invoke(group, method, input); AssertTrue(response.Success); var operation = OperationRef.Read(response.Value);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var status = await invoke("Tasks", "Read", EntityRequest.Write(new(operation.Id))); AssertTrue(status.Success);
                var record = OperationRecord.Read(status.Value); if (record.State == OperationState.Completed) return;
                AssertFalse(record.State is OperationState.Failed or OperationState.Cancelled or OperationState.OutcomeUnknown);
                await Task.Delay(10, timeout.Token);
            }
        }
    }

    private static async Task ExercisePluginContentTransactions(Func<string, string, AbiValue, ValueTask<AbiReply<AbiValue>>> invoke,
        EntityId instance, string path, Nexa.Xsr.State.XsrStateStore store)
    {
        string mods = Path.Combine(path, "mods"); Directory.CreateDirectory(mods);
        string original = Path.Combine(mods, "rollback.jar"); await File.WriteAllTextAsync(original, "original-content");
        string stage = Path.Combine(path, ".nexa-resource-plugin-transaction"); Directory.CreateDirectory(stage);
        string updated = Path.Combine(stage, "replacement.jar"); await File.WriteAllTextAsync(updated, "updated-content");
        var info = new FileInfo(original);
        await Nexa.Services.Minecraft.Management.InstanceContentUpdateTransaction.ApplyAsync([
            new(new(path, "mods", "rollback.jar", false, info.Length, info.LastWriteTimeUtc.Ticks), updated, "replacement.jar")], store);
        AssertFalse(File.Exists(original)); AssertTrue(File.Exists(Path.Combine(mods, "replacement.jar")));
        var response = await invoke("Minecraft.Content", "ListTransactions", EntityRequest.Write(new(instance))); AssertTrue(response.Success);
        var record = Page<ContentTransaction>.Read(response.Value).Items.Single(); AssertEqual(OperationState.Completed, record.State);
        AssertTrue((await invoke("Minecraft.Content", "Preview", EntityRequest.Write(new(record.Plan)))).Success);
        response = await invoke("Minecraft.Content", "Rollback", PlanRequest.Write(new(record.Plan, record.Revision, null))); AssertTrue(response.Success);
        var operation = OperationRef.Read(response.Value);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var status = OperationRecord.Read((await invoke("Tasks", "Read", EntityRequest.Write(new(operation.Id)))).Value);
            if (status.State == OperationState.Completed) break;
            AssertFalse(status.State is OperationState.Failed or OperationState.Cancelled);
            await Task.Delay(5, timeout.Token);
        }
        AssertEqual("original-content", await File.ReadAllTextAsync(original)); AssertFalse(File.Exists(Path.Combine(mods, "replacement.jar")));
        AssertFalse((await invoke("Minecraft.Content", "Rollback", PlanRequest.Write(new(record.Plan, record.Revision, null)))).Success);
        response = await invoke("Minecraft.Content", "ListTransactions", EntityRequest.Write(new(instance))); AssertTrue(response.Success);
        AssertEqual(OperationState.Cancelled, Page<ContentTransaction>.Read(response.Value).Items.Single().State);
    }
}
