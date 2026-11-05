using System.Text;
using Nexa.Services.Accounts;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask LegacyProtectedProfilesUpgradeWithoutRewriting()
    {
        string root = CreateTempDirectory();
        try
        {
            foreach (var (extra, skin, cape) in new (string, byte[], byte[])[]
            {
                ("", [], []),
                (",\"offlineSkinPng\":null,\"offlineCapePng\":null", [], []),
                (",\"offlineSkinPng\":\"AQID\"", [1, 2, 3], []),
                (",\"offlineSkinPng\":null,\"offlineCapePng\":\"BAUG\"", [], [4, 5, 6])
            })
            {
                string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
                byte[] json = LegacyProfileJson(extra);
                // Both persistence entrances must normalize old JSON, not only round-trip
                // a document serialized by the new release (which always writes the fields).
                File.WriteAllBytes(path, json);
                var legacy = new LaunchProfileFilePort(path).Load().Profiles[0];
                AssertTrue(legacy.OfflineSkinPng.SequenceEqual(skin));
                AssertTrue(legacy.OfflineCapePng.SequenceEqual(cape));
                AssertTrue(json.SequenceEqual(File.ReadAllBytes(path)));
                var cipher = new UpgradeProfileProtector();
                byte[] encrypted = [.. "NEXAAC01"u8, .. cipher.Protect(json)];
                File.WriteAllBytes(path, encrypted);
                await using (var accounts = CreateAccountService(new ProtectedLaunchProfilePort(path, cipher)))
                {
                    await accounts.Initialization.WaitAsync(TimeSpan.FromSeconds(5));
                    AssertTrue(accounts.LoadError is null);
                    AssertEqual(1, accounts.GetViews().Count);
                    var profile = accounts.GetProfile(0).Value;
                    AssertEqual("upgrade-private-token", profile.AccessToken);
                    AssertTrue(profile.OfflineSkinPng.SequenceEqual(skin));
                    AssertTrue(profile.OfflineCapePng.SequenceEqual(cape));
                }
                AssertTrue(encrypted.SequenceEqual(File.ReadAllBytes(path)));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static async ValueTask OversizedStoredTexturesPreserveProfile()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "profiles.json");
            var cipher = new UpgradeProfileProtector();
            byte[] json = LegacyProfileJson(",\"offlineSkinPng\":\"" + Convert.ToBase64String(new byte[1_048_577]) + "\"");
            byte[] encrypted = [.. "NEXAAC01"u8, .. cipher.Protect(json)];
            File.WriteAllBytes(path, encrypted);
            await using (var accounts = CreateAccountService(new ProtectedLaunchProfilePort(path, cipher)))
            {
                await accounts.Initialization.WaitAsync(TimeSpan.FromSeconds(5));
                AssertTrue(accounts.LoadError is not null);
                AssertEqual(0, accounts.GetViews().Count);
                AssertFalse(accounts.AddProfile(SampleProfile("Replacement")).IsSuccess);
            }
            AssertTrue(encrypted.SequenceEqual(File.ReadAllBytes(path)));
        }
        finally { Directory.Delete(root, true); }
    }

    private static byte[] LegacyProfileJson(string extra) => Encoding.UTF8.GetBytes(
        "{\"schemaVersion\":1,\"profiles\":[{\"username\":\"UpgradeFixture\",\"kind\":\"Microsoft\",\"uuid\":\"0123456789abcdef0123456789abcdef\",\"accessToken\":\"upgrade-private-token\"" + extra + "}]}");

    private sealed class UpgradeProfileProtector : IAsyncProfileDataProtector
    {
        private readonly TestAccountProtector _inner = new();
        public ValueTask InitializeAsync(IEnumerable<Guid> ids, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public byte[] Protect(byte[] plain) => _inner.Protect(plain);
        public byte[] Unprotect(byte[] data) => _inner.Unprotect(data);
        public void Dispose() { }
    }
}
