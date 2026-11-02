using Nexa.Services.Minecraft.Java;
using Nexa.Xsr;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask JavaAsyncRegistryPreservesCanonicalCommitFailures()
    {
        string root = CreateTempDirectory();
        string executable = Path.Combine(root, "java-runtime-test", "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!); await File.WriteAllTextAsync(executable, "fixture");
        try
        {
            using var locator = new RegistrationLocator(new(new(Path.GetDirectoryName(executable)!, executable, null, new Version(21, 0, 2), JavaBrand.Microsoft, JavaArchitecture.X64, true, false)));
            var registry = new AsyncRegistry { RejectCommit = true };
            var installer = new ManualConfirmedInstaller(executable);
            using var manual = new JavaManualDownloadService(new ManualMetadata(JavaRuntimeInstaller.DetectPlatform().ToMojangKey()),
                installer, locator, new ForbiddenSyncRegistry(), root, JavaRuntimeInstaller.DetectPlatform())
            { RegistrationPort = registry };
            var preview = await manual.PreviewAsync(new(21, 0)); AssertTrue(preview.IsSuccess);
            AssertFalse((await manual.InstallAsync(new(preview.Value.Id, true))).IsSuccess);
            AssertEqual(JavaManualInstallStatus.InstalledUnregistered, (await manual.StatusAsync(new(preview.Value.Id))).Value.Status);
            AssertEqual(1, registry.Writes); AssertEqual(0L, registry.Snapshot.Revision);
            registry.RejectCommit = false;
            preview = await manual.PreviewAsync(new(21, 0)); AssertTrue(preview.IsSuccess);
            AssertTrue((await manual.InstallAsync(new(preview.Value.Id, true))).IsSuccess);
            AssertEqual(executable, registry.Snapshot.Registrations.Single().Executable);
            var management = new JavaRuntimeManagementService(locator, new ForbiddenSyncRegistry()) { RegistrationPort = registry };
            AssertFalse((await management.ManageAsync(new(executable, JavaRuntimeManagementAction.Disable, 0))).IsSuccess);
            AssertTrue((await management.ManageAsync(new(executable, JavaRuntimeManagementAction.Disable, 1))).IsSuccess);
            AssertFalse(registry.Snapshot.Registrations.Single().Enabled);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask JavaAsyncInventoryRejectsChangedRegistry()
    {
        using var locator = new InventoryGateLocator([]); var registry = new AsyncRegistry();
        var inventory = new JavaRuntimeInventoryService(locator, new ForbiddenSyncRegistry()) { RegistrationPort = registry };
        var pending = inventory.ReadAsync(new()).AsTask();
        AssertTrue(locator.Entered.Wait(TimeSpan.FromSeconds(5)));
        registry.Snapshot = new(1, []); locator.Release.Set();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        AssertFalse(result.IsSuccess); AssertEqual("java.runtime.inventory.stale", result.Error!.Code.Value);
        AssertEqual(2, registry.Reads);
    }
    private sealed class ForbiddenSyncRegistry : IJavaRuntimeRegistrationStore
    {
        public JavaRuntimeRegistrySnapshot Read() => throw new InvalidOperationException("Synchronous registry access is forbidden in this fixture.");
        public XsrResult Write(long expectedRevision, IReadOnlyList<JavaRuntimeRegistration> registrations) => throw new InvalidOperationException("Synchronous registry access is forbidden in this fixture.");
    }
    private sealed class AsyncRegistry : IAsyncJavaRuntimeRegistrationStore
    {
        internal JavaRuntimeRegistrySnapshot Snapshot = new(0, []);
        internal bool RejectCommit;
        internal int Reads, Writes;
        public async ValueTask<JavaRuntimeRegistrySnapshot> ReadAsync(CancellationToken token = default)
        { await Task.Yield(); token.ThrowIfCancellationRequested(); Interlocked.Increment(ref Reads); return Snapshot; }
        public async ValueTask<XsrResult> WriteAsync(long expectedRevision, IReadOnlyList<JavaRuntimeRegistration> entries, CancellationToken token = default)
        {
            await Task.Yield(); token.ThrowIfCancellationRequested(); Writes++;
            if (RejectCommit || Snapshot.Revision != expectedRevision)
                return XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("java.fixture.registry.stale"), "Registry changed."));
            Snapshot = new(expectedRevision + 1, entries.ToArray()); return XsrResult.Success();
        }
    }
}
