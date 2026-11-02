using Nexa.Native.Minecraft.Client;
using Nexa.Services.Capabilities;

internal static class EnvironmentCases
{
    internal static async Task RunAsync(NativeMinecraftClient client, string root, string instance, string executable, CancellationToken token)
    {
        await File.WriteAllTextAsync(Path.Combine(root, "options.txt"), "renderDistance:12\nsimulationDistance:10\nmipmapLevels:4\ngraphicsMode:1\nfullscreen:false\nresourcePacks:[\"vanilla\",\"file/native-pack.zip\"]\n", token);
        var scope = new MachineCapabilityQuery(instance, Path.GetFileName(instance), root) { JavaExecutablePath = executable, RefreshInstance = true };
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        var java = await client.ReadEnvironmentFactsAsync(new(MinecraftEnvironmentProvider.Java, root, scope, timestamp), token);
        var major = java.Items.Single(item => item.Id == "java.runtime.major");
        if (major.Kind != MinecraftCapabilityValueKind.SmallInteger || major.SmallInteger != 17 || major.Availability != CapabilityAvailability.Available || major.Timestamp != timestamp)
            throw new InvalidDataException("Native Java observations lost their scalar identity, provenance or actual executable.");
        var files = await client.ReadEnvironmentFactsAsync(new(MinecraftEnvironmentProvider.Files, root, scope, timestamp), token);
        var render = files.Items.Single(item => item.Id == "minecraft.settings.render_distance");
        var packs = files.Items.Single(item => item.Id == "minecraft.settings.resource_packs");
        if (render.SmallInteger != 12 || render.Availability != CapabilityAvailability.Available || packs.Kind != MinecraftCapabilityValueKind.TextList
            || packs.TextList is null || !packs.TextList.Contains("file/native-pack.zip") || files.Items.All(item => item.Id != "minecraft.files.required"))
            throw new InvalidDataException("Native file/settings observations lost their bounded text-list or integer values.");
        var loader = await client.ReadEnvironmentFactsAsync(new(MinecraftEnvironmentProvider.Loader, root, scope, timestamp), token);
        if (loader.Items.Single(item => item.Id == "loader.type").Text != "Vanilla") throw new InvalidDataException("Native loader facts lost actual manifest classification.");
        var mods = await client.ReadEnvironmentFactsAsync(new(MinecraftEnvironmentProvider.Mods, root, scope, timestamp), token);
        if (mods.Items.Single(item => item.Id == "mod.count").SmallInteger < 1 || mods.Items.Single(item => item.Id == "mod.json.size").LargeInteger <= 0)
            throw new InvalidDataException("Native mod inventory facts lost actual directory counts or byte totals.");
        var documents = await client.ReadLocalDocumentsAsync(new(instance), token);
        if (documents.InstanceDirectory != instance || documents.Documents.Count == 0) throw new InvalidDataException("Native local diagnostic documents lost their instance scope.");
        string receipt = Path.Combine(root, ".nexa-launcher-migration.json");
        byte[] trusted = "migration receipt sentinel"u8.ToArray();
        await client.AuthorizeMigrationReceiptAsync(receipt, trusted, token);
        await client.VerifyMigrationReceiptAsync(receipt, trusted, token);
        bool rejected = false;
        try { await client.VerifyMigrationReceiptAsync(receipt, "changed receipt"u8.ToArray(), token); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new InvalidDataException("Native migration receipt authority accepted changed bytes.");
        Console.WriteLine("Native environment providers preserved Java, file/settings, loader, mod facts and local diagnostic documents across a closed scalar boundary.");
    }
}
