using System.Buffers.Binary;
using System.IO.Compression;
using Nexa.Native.Minecraft.Client;
using Nexa.Services.Minecraft.Management;

internal static class FeatureParityCases
{
    internal static async Task CheckInstanceJavaAsync(NativeMinecraftClient client, string instance, string executable, CancellationToken token)
    {
        var missing = await client.QueryInstanceOfflineReadinessQueryAsync(new(instance), token);
        if (!missing.IsSuccess || missing.Value!.JavaCompatible != false) throw new InvalidDataException("Native instance Java silently selected a global runtime.");
        string bin = Path.Combine(instance, "arbitrary", "nested-runtime", "bin"); Directory.CreateDirectory(bin);
        string local = Path.Combine(bin, OperatingSystem.IsWindows() ? "java.exe" : "java");
        File.Copy(executable, local);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(local, File.GetUnixFileMode(executable));
        else
        {
            Directory.CreateDirectory(Path.Combine(bin, "server"));
            File.Copy(executable, Path.Combine(bin, "server", "jvm.dll"));
        }
        try
        {
            var present = await client.QueryInstanceOfflineReadinessQueryAsync(new(instance), token);
            if (!present.IsSuccess || present.Value!.JavaCompatible != true || present.Value.JavaVersion != "17.0.10")
                throw new InvalidDataException("Native instance Java lost version-local probing or compatibility.");
        }
        finally { Directory.Delete(Path.Combine(instance, "arbitrary"), true); }
        Console.WriteLine("PASS: native version-local Java policy, probing and missing-runtime isolation");
    }

    internal static async Task RunAsync(NativeMinecraftClient client, string root, string instance, string executable, CancellationToken token)
    {
        string nbt = Path.Combine(root, "inspection.nbt");
        await File.WriteAllBytesAsync(nbt, [10, 0, 0, 3, 0, 1, 110, 0, 0, 0, 7, 0], token);
        var inspected = await client.InspectContentAsync(new(nbt), token);
        if (!inspected.IsSuccess || inspected.Value!.Format != "NBT" || inspected.Value.Items.Single().Value != "7") throw new InvalidDataException("Native content inspection lost NBT values.");
        byte[] region = new byte[12288]; byte[] payload = await File.ReadAllBytesAsync(nbt, token);
        BinaryPrimitives.WriteInt32BigEndian(region, 0x201); BinaryPrimitives.WriteInt32BigEndian(region.AsSpan(8192), payload.Length + 1);
        region[8196] = 3; payload.CopyTo(region, 8197);
        string regionPath = Path.Combine(root, "r.0.0.mca"); await File.WriteAllBytesAsync(regionPath, region, token);
        var chunks = await client.InspectContentAsync(new(regionPath), token);
        var chunk = await client.InspectContentAsync(new(regionPath, "chunk:0"), token);
        if (!chunks.IsSuccess || chunks.Value!.Format != "Region" || chunks.Value.Total != 1 || !chunk.IsSuccess || chunk.Value!.Items.Single().Value != "7")
            throw new InvalidDataException("Native region inspection lost chunk selection.");
        string packs = Path.Combine(instance, "resourcepacks"); Directory.CreateDirectory(packs);
        using (var zip = ZipFile.Open(Path.Combine(packs, "native-check.zip"), ZipArchiveMode.Create))
        {
            using var content = new StreamWriter(zip.CreateEntry("pack.mcmeta").Open());
            await content.WriteAsync("{\"pack\":{\"pack_format\":15}}");
        }
        var management = await client.QueryInstanceManagementQueryAsync(new(instance), token);
        var configuration = management.Value!.ResourcePacks ?? throw new InvalidDataException("Native management lost resource pack configuration.");
        var enabled = await client.SetResourcePackAsync(new(instance, configuration.Revision, "native-check.zip", true, 4096, true), token);
        management = await client.QueryInstanceManagementQueryAsync(new(instance), token);
        if (!enabled.IsSuccess || !management.Value!.ResourcePacks!.Packs.Single(item => item.Name == "native-check.zip").Enabled)
            throw new InvalidDataException("Native resource pack enablement did not reach options.txt.");
        var lan = await client.DiscoverLanGamesAsync(new(250), token);
        if (!lan.IsSuccess || lan.Value is null) throw new InvalidDataException("Native LAN query did not return a typed snapshot.");
        string script = Path.Combine(root, "launch.sh");
        var exported = await client.ExportLaunchScriptAsync(new(instance, executable, script), token);
        if (!exported.IsSuccess || !(await File.ReadAllTextAsync(script, token)).Contains("nexacl://run/", StringComparison.Ordinal)) throw new InvalidDataException("Native script export was not routed to its owner.");
        Console.WriteLine("PASS: independent native LAN, NBT inspection and instance script routes");
    }
}
