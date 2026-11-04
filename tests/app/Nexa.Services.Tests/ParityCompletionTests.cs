using System.Buffers.Binary;
using System.IO.Compression;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask CompletionResourcePackOptions()
    {
        string root = CreateTempDirectory();
        try
        {
            string instance = ResourceInstanceFixture(root), directory = Path.Combine(instance, "resourcepacks"); Directory.CreateDirectory(directory);
            WriteLocalJar(Path.Combine(directory, "one.zip"), ("pack.mcmeta", "{\"pack\":{\"pack_format\":34}}"));
            WriteLocalJar(Path.Combine(directory, "two.zip"), ("pack.mcmeta", "{\"pack\":{\"min_format\":[34,0],\"max_format\":[34,2]}}"));
            WriteLocalJar(Path.Combine(directory, "old.zip"), ("pack.mcmeta", "{\"pack\":{\"pack_format\":1}}"));
            string options = Path.Combine(instance, "options.txt"); await File.WriteAllTextAsync(options, "music:0.3\nresourcePacks:[\"vanilla\",\"file/one.zip\"]\n");
            var builder = new Nexa.Xsr.State.XsrStateStoreBuilder(); Nexa.Services.Minecraft.Process.MinecraftProcessStateComposition.DeclareState(builder); var store = builder.Build();
            async Task<InstanceResourcePackConfiguration> Read() => (await InstanceManagementService.ReadAsync(new(instance))).ResourcePacks!;
            var config = await Read(); AssertTrue(config.Packs.Single(p => p.Name == "one.zip").Enabled);
            AssertEqual(false, config.Packs.Single(p => p.Name == "old.zip").Compatible);
            var stale = new InstanceResourcePackCommand(instance, config.Revision, "two.zip", true, 4096);
            AssertTrue((await InstanceResourcePackService.SetAsync(stale, store)).IsSuccess);
            AssertFalse((await InstanceResourcePackService.SetAsync(stale, store)).IsSuccess);
            config = await Read(); AssertTrue(config.Packs.Single(p => p.Name == "two.zip").Enabled);
            AssertTrue((await InstanceResourcePackService.SetAsync(new(instance, config.Revision, "two.zip", true, 1), store)).IsSuccess);
            string text = await File.ReadAllTextAsync(options); AssertTrue(text.Contains("music:0.3", StringComparison.Ordinal));
            AssertTrue(text.IndexOf("file/two.zip", StringComparison.Ordinal) < text.IndexOf("file/one.zip", StringComparison.Ordinal));
            config = await Read(); AssertFalse((await InstanceResourcePackService.SetAsync(new(instance, config.Revision, "old.zip", true, 4096), store)).IsSuccess);
            AssertTrue((await InstanceResourcePackService.SetAsync(new(instance, config.Revision, "old.zip", true, 4096, true), store)).IsSuccess);
            AssertTrue((await File.ReadAllTextAsync(options)).Contains("incompatibleResourcePacks:[\"file/old.zip\"]", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask CompletionRegionChunks()
    {
        string root = CreateTempDirectory();
        try
        {
            byte[] nbt = [10, 0, 0, 3, 0, 1, 110, 0, 0, 0, 42, 0];
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, true)) zlib.Write(nbt);
            byte[] payload = compressed.ToArray(), region = new byte[12288];
            BinaryPrimitives.WriteInt32BigEndian(region, 0x201);
            BinaryPrimitives.WriteInt32BigEndian(region.AsSpan(8192), payload.Length + 1); region[8196] = 2; payload.CopyTo(region, 8197);
            string path = Path.Combine(root, "r.2.-1.mca"); await File.WriteAllBytesAsync(path, region);
            var listing = await ContentInspectionService.ReadAsync(new(path)); AssertEqual("Region", listing.Format); AssertEqual(1, listing.Total);
            var tree = await ContentInspectionService.ReadAsync(new(path, "chunk:0")); AssertEqual("42", tree.Items.Single().Value);
            BinaryPrimitives.WriteInt32BigEndian(region.AsSpan(8192), 1); region[8196] = 130;
            await File.WriteAllBytesAsync(path, region); await File.WriteAllBytesAsync(Path.Combine(root, "c.64.-32.mcc"), payload);
            tree = await ContentInspectionService.ReadAsync(new(path, "chunk:0")); AssertEqual("42", tree.Items.Single().Value);
            BinaryPrimitives.WriteInt32BigEndian(region.AsSpan(4), 0x201); await File.WriteAllBytesAsync(path, region);
            await TrustRejectsAsync<InvalidDataException>(() => ContentInspectionService.ReadAsync(new(path)));
            // lz4-java Checksum truncates XXH32 to 28 bits. This independent fixture's
            // unmasked hash is 0xBEFFC143; rejecting its masked checksum breaks real MCA files.
            byte[] lz4 = new byte[42 + nbt.Length];
            "LZ4Block"u8.CopyTo(lz4); lz4[8] = 0x16;
            BinaryPrimitives.WriteInt32LittleEndian(lz4.AsSpan(9), nbt.Length);
            BinaryPrimitives.WriteInt32LittleEndian(lz4.AsSpan(13), nbt.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(lz4.AsSpan(17), 0x0EFFC143);
            nbt.CopyTo(lz4, 21); "LZ4Block"u8.CopyTo(lz4.AsSpan(21 + nbt.Length)); lz4[29 + nbt.Length] = 0x16;
            Array.Clear(region); BinaryPrimitives.WriteInt32BigEndian(region, 0x201);
            BinaryPrimitives.WriteInt32BigEndian(region.AsSpan(8192), lz4.Length + 1); region[8196] = 4; lz4.CopyTo(region, 8197);
            await File.WriteAllBytesAsync(path, region);
            tree = await ContentInspectionService.ReadAsync(new(path, "chunk:0")); AssertEqual("42", tree.Items.Single().Value);
            region[8197 + 17] ^= 1; await File.WriteAllBytesAsync(path, region);
            await TrustRejectsAsync<InvalidDataException>(() => ContentInspectionService.ReadAsync(new(path, "chunk:0")));
            // Independent compressed block: a 256-byte NBT array, overlapping matches,
            // final literals, and the XXH32 streaming checksum for a payload over 16 bytes.
            byte[] matched = Convert.FromBase64String("TFo0QmxvY2smGAAAAA8BAABDrAEC7woAAAcABGRhdGEAAAEAAQDpUAAAAAAATFo0QmxvY2sWAAAAAAAAAAAAAAAA");
            BinaryPrimitives.WriteInt32BigEndian(region.AsSpan(8192), matched.Length + 1); matched.CopyTo(region, 8197);
            await File.WriteAllBytesAsync(path, region);
            var array = await ContentInspectionService.ReadAsync(new(path, "chunk:0", "0"));
            AssertEqual(256, array.Total); AssertTrue(array.Items.All(item => item.Value == "0"));
            string old = Path.Combine(root, "level.dat_old"); await File.WriteAllBytesAsync(old, nbt);
            AssertEqual("NBT", (await ContentInspectionService.ReadAsync(new(old))).Format);
            string structure = Path.Combine(root, "structure.nbt");
            await File.WriteAllBytesAsync(structure, [10, 0, 0, 8, 0, 6, 97, 117, 116, 104, 111, 114, 0, 7, 66, 117, 105, 108, 100, 101, 114,
                9, 0, 4, 115, 105, 122, 101, 3, 0, 0, 0, 3, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0]);
            string summary = (await ContentInspectionService.ReadAsync(new(structure))).Summary;
            AssertTrue(summary.Contains("author: Builder", StringComparison.Ordinal));
            AssertTrue(summary.Contains("Size: 2 × 3 × 4", StringComparison.Ordinal));
            AssertTrue(summary.Contains("Volume: 24", StringComparison.Ordinal));
            await File.WriteAllBytesAsync(structure, [10, 0, 0, 11, 0, 4, 115, 105, 122, 101, 0, 0, 0, 3, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0]);
            summary = (await ContentInspectionService.ReadAsync(new(structure))).Summary;
            AssertTrue(summary.Contains("Size: 2 × 3 × 4", StringComparison.Ordinal));
            // Compound iteration order must not swap axes (z=4, x=2, y=3).
            await File.WriteAllBytesAsync(structure, [10, 0, 0, 10, 0, 8, 77, 101, 116, 97, 100, 97, 116, 97, 10, 0, 13, 69, 110, 99, 108, 111, 115, 105, 110, 103, 83, 105, 122, 101,
                3, 0, 1, 122, 0, 0, 0, 4, 3, 0, 1, 120, 0, 0, 0, 2, 3, 0, 1, 121, 0, 0, 0, 3, 0, 0, 0]);
            summary = (await ContentInspectionService.ReadAsync(new(structure))).Summary;
            AssertTrue(summary.Contains("Size: 2 × 3 × 4", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask CompletionJavaPolicies()
    {
        string root = CreateTempDirectory();
        try
        {
            var a = Candidate(Path.Combine(root, "jdk17"), new Version(17, 0, 10), JavaBrand.EclipseTemurin, false);
            var b = Candidate(Path.Combine(root, "jdk21"), new Version(21, 0, 1), JavaBrand.EclipseTemurin, false);
            var locator = new InstanceOnlyJavaInspector([a, b]); var selection = new JavaSelectionService(locator);
            var any = new MinecraftJavaRequirementRequest();
            var result = await selection.SelectAsync(any, new MajorJavaPreference(21)); AssertEqual(b, result.SelectedJava);
            result = await selection.SelectAsync(new MinecraftJavaRequirementRequest { ManifestJavaMajorVersion = 17 }, new MajorJavaPreference(21)); AssertFalse(result.Success);
            int reads = locator.GlobalReads;
            result = await selection.SelectAsync(any, new UseRelativeJavaPreference("jdk17/bin/java") { BaseDirectory = root }); AssertEqual(a, result.SelectedJava); AssertEqual(reads, locator.GlobalReads);
            result = await selection.SelectAsync(any, new UseRelativeJavaPreference("../outside/java") { BaseDirectory = root }); AssertFalse(result.Success); AssertEqual(reads, locator.GlobalReads);
            var requirement = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest { HasNide8Login = true, ManifestJavaMajorVersion = 8 });
            AssertFalse(requirement.Range.Contains(new Version(1, 8, 0, 140))); AssertTrue(requirement.Range.Contains(new Version(1, 8, 0, 141)));
            AssertFalse(MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest { HasNide8Login = true, ManifestJavaMajorVersion = 7 }).Success);
        }
        finally { Directory.Delete(root, true); }
    }
}
