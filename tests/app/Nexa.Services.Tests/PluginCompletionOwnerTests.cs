using System.IO.Compression;
using System.Security.Cryptography;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Java;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask PluginJavaArchivePreservesIntegrityAndRegistersRuntime()
    {
        string root = CreateTempDirectory();
        try
        {
            var registry = new AsyncRegistry(); var locator = new ArchiveJavaLocator();
            var management = new JavaRuntimeManagementService(locator, new ForbiddenSyncRegistry()) { RegistrationPort = registry };
            var installer = new JavaArchiveInstaller(locator, management);
            string executable = OperatingSystem.IsWindows() ? "java.exe" : "java";
            async Task<JavaArchiveInstallCommand> Archive(string name, bool symlink = false)
            {
                string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
                using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry(name); if (symlink) entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                    await using var stream = entry.Open(); await stream.WriteAsync("runtime-fixture"u8.ToArray());
                }
                byte[] bytes = await File.ReadAllBytesAsync(path);
                return new(path, Path.Combine(root, Guid.NewGuid().ToString("N")), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), 21, "X64", registry.Snapshot.Revision);
            }
            var command = await Archive("jdk/bin/" + executable);
            AssertFalse((await installer.InstallAsync(command with { Sha256 = new('0', 64) })).IsSuccess); AssertFalse(Directory.Exists(command.Destination)); AssertEqual(0, locator.Probes);
            var traversal = await Archive("../escape"); AssertFalse((await installer.InstallAsync(traversal)).IsSuccess); AssertFalse(File.Exists(Path.Combine(root, "escape")));
            var link = await Archive("jdk/bin/" + executable, true); AssertFalse((await installer.InstallAsync(link)).IsSuccess); AssertEqual(0, locator.Probes);
            AssertTrue((await installer.InstallAsync(command)).IsSuccess);
            string installed = Path.Combine(command.Destination, "jdk", "bin", executable);
            AssertTrue(File.Exists(installed)); AssertEqual(installed, registry.Snapshot.Registrations.Single().Executable); AssertEqual(1L, registry.Snapshot.Revision);
            AssertFalse((await installer.InstallAsync(command)).IsSuccess);
            var wrongVersion = await Archive("jdk/bin/" + executable); AssertFalse((await installer.InstallAsync(wrongVersion with { Major = 17 })).IsSuccess); AssertFalse(Directory.Exists(wrongVersion.Destination));
            AssertFalse(Directory.EnumerateDirectories(root).Any(p => Path.GetFileName(p).Contains(".install-", StringComparison.Ordinal)));
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class ArchiveJavaLocator : IJavaRuntimeLocator
    {
        internal int Probes;
        public void Invalidate() { }
        public ValueTask<IReadOnlyList<JavaRuntimeCandidate>> FindAllAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<JavaRuntimeCandidate>>([]);
        public ValueTask<JavaRuntimeCandidate?> InspectAsync(string javaExecutablePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Probes++;
            AssertTrue(File.Exists(javaExecutablePath));
            return ValueTask.FromResult<JavaRuntimeCandidate?>(new(new(Path.GetDirectoryName(Path.GetDirectoryName(javaExecutablePath))!, javaExecutablePath, null, new Version(21, 0, 2), JavaBrand.Microsoft, JavaArchitecture.X64, true, true)));
        }
    }
    private static async ValueTask PluginInstallRecoveryRecordsRejectStaleAndDismissWithoutDeleting()
    {
        string root = CreateTempDirectory();
        try
        {
            using var fixture = new InstallFixture(new());
            string stage = Path.Combine(root, ".nexa-install-jobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            var plan = await InstallTaskJournal.CreateAsync(stage, new(root, "1.20.1", InstanceName: "recovery") { InheritVanilla = false }, default);
            await File.WriteAllTextAsync(Path.Combine(stage, "retain.txt"), "retain");
            var listing = await MinecraftInstallService.ListRecoveryRecordsAsync(new([root])); AssertTrue(listing.IsSuccess);
            var record = listing.Value.Items.Single(); AssertEqual("recovery", record.Instance);
            var apply = new MinecraftInstallRecoveryRecordCommand(root, record.Identity, "stale", true);
            AssertFalse((await fixture.Install.ApplyRecoveryRecordAsync(apply)).IsSuccess);
            AssertEqual(InstallTaskStatus.Pending, await InstallTaskJournal.ReadStatusAsync(stage, plan, default));
            AssertTrue((await fixture.Install.ApplyRecoveryRecordAsync(apply with { ExpectedRevision = record.Revision })).IsSuccess);
            AssertEqual(InstallTaskStatus.Dismissed, await InstallTaskJournal.ReadStatusAsync(stage, plan, default));
            AssertEqual("retain", await File.ReadAllTextAsync(Path.Combine(stage, "retain.txt")));
            AssertEqual(0, (await MinecraftInstallService.ListRecoveryRecordsAsync(new([root]))).Value.Items.Count);
            AssertFalse((await fixture.Install.ApplyRecoveryRecordAsync(apply with { ExpectedRevision = record.Revision })).IsSuccess);
        }
        finally { Directory.Delete(root, true); }
    }
}
