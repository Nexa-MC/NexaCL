using System.Text;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static ValueTask ResourcePackWritesKeepListReadable() => ResourcePackWritesRespectReadBudgets(false);
    private static ValueTask ResourcePackWritesKeepFileReadable() => ResourcePackWritesRespectReadBudgets(true);

    private static async ValueTask ResourcePackWritesRespectReadBudgets(bool fileBudget)
    {
        string root = CreateTempDirectory();
        try
        {
            string instance = ResourceInstanceFixture(root);
            string directory = Path.Combine(instance, "resourcepacks");
            Directory.CreateDirectory(directory);
            WriteLocalJar(Path.Combine(directory, "one.zip"), ("pack.mcmeta", "{\"pack\":{\"pack_format\":34}}"));
            string text;
            if (fileBudget)
            {
                const string prefix = "custom:";
                const string suffix = "\nresourcePacks:[]\n";
                text = prefix + new string('a', 1024 * 1024 - prefix.Length - suffix.Length) + suffix;
            }
            else
            {
                var entries = Enumerable.Range(0, 4096).Select(i => "\"builtin/" + i + "\"");
                text = "resourcePacks:[" + string.Join(',', entries) + "]\n";
            }
            string path = Path.Combine(instance, "options.txt");
            byte[] original = Encoding.UTF8.GetBytes(text);
            await File.WriteAllBytesAsync(path, original);
            var snapshot = await InstanceManagementService.ReadAsync(new(instance));
            AssertEqual("", snapshot.ResourcePacks!.Problem);
            var builder = new XsrStateStoreBuilder(); MinecraftProcessStateComposition.DeclareState(builder);
            var result = await InstanceResourcePackService.SetAsync(new(instance, snapshot.ResourcePacks.Revision, "one.zip", true, 4096), builder.Build());
            AssertFalse(result.IsSuccess);
            AssertTrue((await File.ReadAllBytesAsync(path)).SequenceEqual(original));
            AssertEqual(0, Directory.GetFiles(instance, ".nexa-options-*").Length);
            AssertEqual("", (await InstanceManagementService.ReadAsync(new(instance))).ResourcePacks!.Problem);
        }
        finally { Directory.Delete(root, true); }
    }
}
