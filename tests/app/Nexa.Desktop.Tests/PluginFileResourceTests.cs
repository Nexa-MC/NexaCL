using System.Text;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void PluginFileResourcesAndSymlinks() => PluginFileResourcesAndSymlinksAsync().GetAwaiter().GetResult();
    private static async Task PluginFileResourcesAndSymlinksAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nexa-plugin-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var resources = new PluginFileResources();
            var owner = new PluginIdentity("example.files", "1.0.0", Guid.NewGuid());
            var other = owner with { LoadInstanceId = Guid.NewGuid() };
            string file = Path.Combine(directory, "document.txt"); await File.WriteAllTextAsync(file, "before");
            var reference = resources.GrantPath(owner, file, true);
            var before = await resources.StatAsync(owner, reference, default);
            bool rejected = false;
            try { resources.Resolve(other, reference); } catch (AbiHostException e) when (e.Code == AbiErrors.HANDLE_INVALID) { rejected = true; }
            AssertTrue(rejected);
            var blob = new BlobRef(Guid.NewGuid(), owner.LoadInstanceId, 5, "test");
            await resources.WriteAsync(owner, new(reference, blob, before.Revision), Encoding.UTF8.GetBytes("after"), default);
            AssertEqual("after", await File.ReadAllTextAsync(file));
            rejected = false;
            try { await resources.WriteAsync(owner, new(reference, blob, before.Revision), Encoding.UTF8.GetBytes("stale"), default); }
            catch (AbiHostException e) when (e.Code == AbiErrors.CONFLICT) { rejected = true; }
            AssertTrue(rejected); AssertEqual("after", await File.ReadAllTextAsync(file));
            var readOnly = resources.GrantPath(owner, file);
            rejected = false;
            try { resources.Resolve(owner, readOnly, true); } catch (AbiHostException e) when (e.Code == AbiErrors.PERMISSION_DENIED) { rejected = true; }
            AssertTrue(rejected);
            if (!OperatingSystem.IsWindows())
            {
                string actual = Path.Combine(directory, "actual"); Directory.CreateDirectory(actual);
                await File.WriteAllTextAsync(Path.Combine(actual, "linked.txt"), "linked");
                string link = Path.Combine(directory, "shortcut"); Directory.CreateSymbolicLink(link, actual);
                var linked = resources.GrantPath(owner, Path.Combine(link, "linked.txt"));
                AssertEqual("linked", Encoding.UTF8.GetString(await resources.ReadAsync(owner, new(linked, 0, 6), default)));
                string outside = Path.Combine(directory, "outside.txt"); await File.WriteAllTextAsync(outside, "private");
                File.CreateSymbolicLink(Path.Combine(actual, "escape.txt"), outside);
                var listed = await resources.ListAsync(owner, resources.GrantPath(owner, actual), default);
                AssertEqual(1, listed.Length); AssertEqual("linked.txt", listed[0].Name);
                var pinned = resources.GrantPath(owner, Path.Combine(actual, "linked.txt"), true);
                File.Delete(Path.Combine(actual, "linked.txt")); File.CreateSymbolicLink(Path.Combine(actual, "linked.txt"), outside);
                rejected = false;
                try { resources.Resolve(owner, pinned, true); } catch (AbiHostException e) when (e.Code == AbiErrors.CONFLICT) { rejected = true; }
                AssertTrue(rejected); AssertEqual("private", await File.ReadAllTextAsync(outside));
            }
            resources.Retire(owner.PluginId); rejected = false;
            try { resources.Resolve(owner, reference); } catch (AbiHostException e) when (e.Code == AbiErrors.HANDLE_INVALID) { rejected = true; }
            AssertTrue(rejected);
        }
        finally { Directory.Delete(directory, true); }
    }
}
