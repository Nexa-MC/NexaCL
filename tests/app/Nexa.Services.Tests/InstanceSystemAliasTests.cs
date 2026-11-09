using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask InstanceContentAdmitsSystemAliasesOnly()
    {
        string root = CreateTempDirectory();
        string? link = null;
        try
        {
            string instance = ResourceInstanceFixture(root);
            string mods = Path.Combine(instance, "mods"); Directory.CreateDirectory(mods);
            string jar = Path.Combine(mods, "example.jar");
            WriteLocalJar(jar, ("fabric.mod.json", "{\"schemaVersion\":1,\"id\":\"example\",\"version\":\"1\",\"name\":\"Example\"}"));
            var snapshot = await InstanceManagementService.ReadAsync(new(instance));
            var content = snapshot.Contents.Single(p => p.PageId == "mods");
            AssertTrue(content.Complete);
            var entry = content.Entries.Single(e => e.Name == "example.jar");
            var builder = new XsrStateStoreBuilder(); MinecraftProcessStateComposition.DeclareState(builder);
            AssertTrue((await InstanceContentService.SetModEnabledAsync(new(instance, entry.Name, false, entry.Size!.Value, entry.ModifiedUtcTicks), builder.Build())).IsSuccess);
            AssertTrue(File.Exists(jar + ".disabled"));
            if (!OperatingSystem.IsWindows())
            {
                link = root + "-linked";
                Directory.CreateSymbolicLink(link, root);
                await TrustRejectsAsync<IOException>(() => InstanceManagementService.ReadAsync(new(Path.Combine(link, "versions", Path.GetFileName(instance)))));
            }
        }
        finally
        {
            if (link is not null) Directory.Delete(link);
            Directory.Delete(root, true);
        }
    }
}
