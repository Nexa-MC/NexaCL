using Nexa.Native.Minecraft.Client;
using Nexa.Services.Minecraft.Management;

internal static class FeatureParityCases
{
    internal static async Task RunAsync(NativeMinecraftClient client, string root, string instance, string executable, CancellationToken token)
    {
        string nbt = Path.Combine(root, "inspection.nbt");
        await File.WriteAllBytesAsync(nbt, [10, 0, 0, 3, 0, 1, 110, 0, 0, 0, 7, 0], token);
        var inspected = await client.InspectContentAsync(new(nbt), token);
        if (!inspected.IsSuccess || inspected.Value!.Format != "NBT" || inspected.Value.Items.Single().Value != "7") throw new InvalidDataException("Native content inspection lost NBT values.");
        var lan = await client.DiscoverLanGamesAsync(new(250), token);
        if (!lan.IsSuccess || lan.Value is null) throw new InvalidDataException("Native LAN query did not return a typed snapshot.");
        string script = Path.Combine(root, "launch.sh");
        var exported = await client.ExportLaunchScriptAsync(new(instance, executable, script), token);
        if (!exported.IsSuccess || !(await File.ReadAllTextAsync(script, token)).Contains("nexacl://run/", StringComparison.Ordinal)) throw new InvalidDataException("Native script export was not routed to its owner.");
        Console.WriteLine("PASS: independent native LAN, NBT inspection and instance script routes");
    }
}
