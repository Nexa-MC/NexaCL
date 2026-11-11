using System.Security.Cryptography;
using Nexa.Desktop.Ui;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Services.Accounts;
using Nexa.Services.Composition.Plugins;
using PluginSidecar.Packages;
using PluginSidecar.WasmRuntime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void PluginOfflineRestoration() => PluginOfflineRestorationAsync().GetAwaiter().GetResult();
    private static async Task PluginOfflineRestorationAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        string directory = Path.Combine(fixture.TemporaryDirectory, "approved-plugins");
        byte[] source = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "plugin-fixtures", "desktop.zip"));
        using var package = VerifiedPluginPackage.Parse(source, "example.desktop", "1.0.0");
        byte[] key = RandomNumberGenerator.GetBytes(32);
        using (var catalog = new PluginOfflineCatalog(directory, new PluginTestProtector(key)))
        {
            await catalog.SaveAsync(source, package.Manifest.PluginId, package.Manifest.PluginVersion, package.Manifest.RequestedPermissions, default);
            var record = catalog.Records().Single(); byte[] saved = await File.ReadAllBytesAsync(record);
            AssertFalse(System.Text.Encoding.UTF8.GetString(saved).Contains("example.desktop", StringComparison.Ordinal));
            using (var reopened = await catalog.OpenAsync(record, default)) AssertTrue(source.AsSpan().SequenceEqual(reopened.Package));
            saved[^1] ^= 1; await File.WriteAllBytesAsync(record, saved);
            bool denied = false;
            try { using var invalid = await catalog.OpenAsync(record, default); } catch (CryptographicException) { denied = true; }
            AssertTrue(denied); AssertTrue(File.Exists(record));
            await catalog.SaveAsync(source, package.Manifest.PluginId, package.Manifest.PluginVersion, package.Manifest.RequestedPermissions, default);
            denied = false;
            try { await catalog.SaveAsync(source, package.Manifest.PluginId, package.Manifest.PluginVersion, ["nexa.accounts.write"], default); } catch (IOException) { denied = true; }
            AssertTrue(denied);
        }
        string corrupt = Path.Combine(directory, new string('a', 64) + ".npinstall");
        await File.WriteAllBytesAsync(corrupt, [1, 2, 3]);
        await using (var safe = new PluginPageController(fixture.Shell, fixture.Intents, fixture.Store, fixture.Feedback,
            Path.Combine(fixture.TemporaryDirectory, "safe-runtime"), [], (_, _) => throw new InvalidOperationException("Safe mode restored a plugin."),
            _ => Task.FromResult<string?>(null), () => true, () => true, Guid.NewGuid, true,
            new PluginOfflineCatalog(directory, new PluginTestProtector(key))))
        {
            fixture.Shell.Render(new(1000, 800)); AssertEqual(0, safe.Application.Runtime.Active.Length);
        }
        await using var engine = new GuestExecutionServer(); using var endpoint = new ChunkedPluginEndpoint(engine);
        var exchange = new ChunkedPluginExchange(endpoint); bool developer = false, available = false; Guid session = Guid.NewGuid(); int creates = 0;
        await using var controller = new PluginPageController(fixture.Shell, fixture.Intents, fixture.Store, fixture.Feedback,
            Path.Combine(fixture.TemporaryDirectory, "plugin-runtime"), [],
            (bytes, token) => { Interlocked.Increment(ref creates); return RemoteWasmPluginMachine.CreateAsync(exchange, bytes, token); },
            _ => Task.FromResult<string?>(null), () => developer, () => available, () => available ? session : Guid.Empty, false,
            new PluginOfflineCatalog(directory, new PluginTestProtector(key)));
        fixture.Shell.Render(new(1000, 800)); AssertEqual(0, creates);
        available = true; fixture.Shell.Render(new(1000, 800)); AssertEqual(0, creates);
        developer = true; await Until(() => controller.Application.Runtime.Active.Length == 1);
        var first = controller.Application.Runtime.Active.Single(); AssertEqual(1, creates);
        for (int i = 0; i < 4; i++) fixture.Shell.Render(new(1000, 800)); AssertEqual(1, creates);
        available = false; await Until(() => controller.Application.Runtime.Active.Length == 0);
        session = Guid.NewGuid(); available = true; await Until(() => controller.Application.Runtime.Active.Length == 1);
        AssertTrue(first.LoadInstanceId != controller.Application.Runtime.Active.Single().LoadInstanceId);
        AssertEqual(2, creates);
        fixture.Shell.Stage.Navigation.Replace(controller.Page);
        await Until(() =>
        {
            var current = fixture.Shell.Render(new(1000, 800));
            return HasKey(fixture.Shell, current, "PluginStop") && FindByKey(fixture.Shell, current, "PluginStop").IsEnabled;
        });
        var scene = fixture.Shell.Render(new(1000, 800));
        Emit(fixture.Intents, "ui.plugins.manage", FindByKey(fixture.Shell, scene, "PluginStop").Entity);
        await Until(() => controller.Application.Runtime.Active.Length == 0);
        AssertEqual(1, Directory.GetFiles(directory, "*.npinstall").Length); AssertTrue(File.Exists(corrupt));
        available = false; fixture.Shell.Render(new(1000, 800));
        available = true; session = Guid.NewGuid(); fixture.Shell.Render(new(1000, 800));
        AssertEqual(2, creates);
        CryptographicOperations.ZeroMemory(key);

        async Task Until(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            do { fixture.Shell.Render(new(1000, 800)); await Task.Delay(5, timeout.Token); } while (!predicate());
        }
    }
    private sealed class PluginTestProtector(byte[] key) : IProfileDataProtector
    {
        public byte[] Protect(byte[] plaintext)
        {
            byte[] result = new byte[28 + plaintext.Length]; RandomNumberGenerator.Fill(result.AsSpan(0, 12));
            using var aes = new AesGcm(key, 16); aes.Encrypt(result.AsSpan(0, 12), plaintext, result.AsSpan(28), result.AsSpan(12, 16)); return result;
        }
        public byte[] Unprotect(byte[] ciphertext)
        {
            byte[] result = new byte[ciphertext.Length - 28];
            using var aes = new AesGcm(key, 16); aes.Decrypt(ciphertext.AsSpan(0, 12), ciphertext.AsSpan(28), ciphertext.AsSpan(12, 16), result); return result;
        }
    }
}
