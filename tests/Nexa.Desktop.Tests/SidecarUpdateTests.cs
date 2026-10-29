using System.Net;
using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Updates;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarUpdatesPublishOnlyVerifiedCompletePackages() => RunSidecarUpdateRegression().GetAwaiter().GetResult();
    private static async Task RunSidecarUpdateRegression()
    {
        string root = Path.Combine(Path.GetTempPath(), "nexa-sidecar-update-" + Guid.NewGuid().ToString("N"));
        const string id = "Test.Package", version = "2.0.0.alpha.16";
        byte[] image = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
        string hash = Convert.ToHexStringLower(SHA256.HashData(image));
        string bundled = Path.Combine(root, "bundled"), cache = Path.Combine(root, "updates");
        Directory.CreateDirectory(bundled);
        await File.WriteAllBytesAsync(Path.Combine(bundled, id + ".nsc"), new byte[128]);
        byte[] catalog = Encoding.UTF8.GetBytes($$"""
            {"schema":1,"rid":"linux-x64","release":"{{version}}","packages":[{"id":"{{id}}","version":"1.1","sha256":"{{hash}}","size":{{image.Length}},"minimumHostVersion":"{{version}}"}]}
            """);
        bool truncate = false, rejectSignature = false;
        using var http = new HttpClient(new SidecarUpdateHttp(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/releases", StringComparison.Ordinal))
                return Encoding.UTF8.GetBytes($$"""[{"draft":false,"tag_name":"v{{version}}","assets":[{"name":"Nexa-Sidecars-linux-x64.json"}]}]""");
            if (path.EndsWith(".asc", StringComparison.Ordinal)) return "signature"u8.ToArray();
            if (path.EndsWith(".json", StringComparison.Ordinal)) return catalog;
            if (path.EndsWith(".nsc", StringComparison.Ordinal)) return truncate ? image[..32] : image;
            throw new InvalidOperationException("Unexpected request");
        }));
        var verifier = new SidecarTestVerifier(() => rejectSignature);
        try
        {
            using var service = new SidecarUpdateService(http, verifier, cache, bundled, "linux-x64", version);
            AssertEqual(0, (await service.ActivePackagesAsync()).Count);
            await service.CheckAsync("alpha", default);
            AssertTrue(service.Snapshot.Items.Single().CanInstall);
            truncate = true;
            try { await service.InstallAsync(id, default); throw new InvalidOperationException("Truncation accepted"); }
            catch (InvalidDataException) { }
            AssertFalse(File.Exists(Path.Combine(cache, id, "active")));
            AssertTrue(service.Snapshot.Items.Single().CanInstall);
            truncate = false; rejectSignature = true;
            try { await service.InstallAsync(id, default); throw new InvalidOperationException("Signature accepted"); }
            catch (InvalidDataException) { }
            AssertFalse(File.Exists(Path.Combine(cache, id, "active")));
            rejectSignature = false;
            await service.InstallAsync(id, default);
            AssertEqual("restart", service.Snapshot.Items.Single().Phase);
            AssertEqual(hash, await File.ReadAllTextAsync(Path.Combine(cache, id, "active")));
            AssertEqual(0, Directory.GetDirectories(Path.Combine(cache, id), ".download-*").Length);
            using var next = new SidecarUpdateService(http, verifier, cache, bundled, "linux-x64", version);
            var active = await next.ActivePackagesAsync();
            AssertEqual(Path.Combine(cache, id, hash, id + ".nsc"), active[id]);
            await next.CheckAsync("alpha", default);
            AssertEqual("current", next.Snapshot.Items.Single().Phase);
            // Invalid local catalog cannot crash startup or activate an unverified image.
            await File.WriteAllTextAsync(Path.Combine(cache, id, hash, "catalog.json"), "{}");
            using var corrupted = new SidecarUpdateService(http, verifier, cache, bundled, "linux-x64", version);
            AssertEqual(0, (await corrupted.ActivePackagesAsync()).Count);
            await corrupted.CheckAsync("alpha", default);
            AssertTrue(corrupted.Snapshot.Items.Single().CanInstall);
            await corrupted.InstallAsync(id, default);
            AssertEqual(1, (await corrupted.ActivePackagesAsync()).Count);
            await File.WriteAllBytesAsync(Path.Combine(cache, id, hash, id + ".nsc"), new byte[128]);
            using var damaged = new SidecarUpdateService(http, verifier, cache, bundled, "linux-x64", version);
            AssertEqual(0, (await damaged.ActivePackagesAsync()).Count);
            await damaged.CheckAsync("alpha", default);
            await damaged.InstallAsync(id, default);
            AssertEqual(1, (await damaged.ActivePackagesAsync()).Count);
            using var newerHost = new SidecarUpdateService(http, verifier, cache, bundled, "linux-x64", "2.0.0.alpha.17");
            AssertEqual(0, (await newerHost.ActivePackagesAsync()).Count);
            await newerHost.CheckAsync("alpha", default);
            AssertFalse(newerHost.Snapshot.Items.Single().CanInstall);
            using var oldHost = new SidecarUpdateService(http, verifier, Path.Combine(root, "old"), bundled, "linux-x64", "2.0.0.alpha.15");
            await oldHost.ActivePackagesAsync();
            await oldHost.CheckAsync("alpha", default);
            AssertEqual("host-required", oldHost.Snapshot.Items.Single().Phase);
            AssertFalse(oldHost.Snapshot.Items.Single().CanInstall);
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class SidecarUpdateHttp(Func<HttpRequestMessage, byte[]> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(reply(request)) }); }
    }
    private sealed class SidecarTestVerifier(Func<bool> reject) : IUpdateSignatureVerifier
    {
        public Task VerifyAsync(Stream content, Stream detachedSignature, CancellationToken cancellationToken = default) =>
            reject() ? Task.FromException(new InvalidDataException("Rejected fixture signature")) : Task.CompletedTask;
    }
}
