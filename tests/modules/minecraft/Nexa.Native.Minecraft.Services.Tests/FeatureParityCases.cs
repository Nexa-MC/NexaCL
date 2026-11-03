using Nexa.Native.Minecraft.Client;
using Nexa.Services.Minecraft.Management;

internal static class FeatureParityCases
{
    internal static async Task CheckInstanceJavaAsync(NativeMinecraftClient client, string instance, string executable, CancellationToken token)
    {
        var missing = await client.QueryInstanceOfflineReadinessQueryAsync(new(instance), token);
        if (!missing.IsSuccess || missing.Value!.JavaCompatible != false) throw new InvalidDataException("Native instance Java silently selected a global runtime.");
        string bin = Path.Combine(instance, "java", "bin"); Directory.CreateDirectory(bin);
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
        finally { Directory.Delete(Path.Combine(instance, "java"), true); }
        Console.WriteLine("PASS: native version-local Java policy, probing and missing-runtime isolation");
    }

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
