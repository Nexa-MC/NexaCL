using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Nexa.Services.Composition;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static void ApiClientCertificateImportsFileAndEmbeddedPrivateKeys()
    {
        string root = CreateTempDirectory();
        string? originalPath = Environment.GetEnvironmentVariable("NEXA_API_CLIENT_CERT_PATH");
        string? originalPassword = Environment.GetEnvironmentVariable("NEXA_API_CLIENT_CERT_PASSWORD");
        try
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Nexa temporary test identity", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
            const string password = "temporary-test-password";
            byte[] pfx = certificate.Export(X509ContentType.Pkcs12, password);
            string path = Path.Combine(root, "client.pfx");
            File.WriteAllBytes(path, pfx);
            Environment.SetEnvironmentVariable("NEXA_API_CLIENT_CERT_PASSWORD", password);
            Environment.SetEnvironmentVariable("NEXA_API_CLIENT_CERT_PATH", path);
            using (var fromFile = CloudflareApiClient.TryCreate())
            {
                AssertTrue(fromFile is not null);
                AssertEqual(TimeSpan.FromSeconds(15), fromFile!.Client.Timeout);
            }
            Environment.SetEnvironmentVariable("NEXA_API_CLIENT_CERT_PATH", null);
            using (var embedded = CloudflareApiClient.TryCreate(() => new MemoryStream(pfx)))
                AssertTrue(embedded is not null);

            // A public-only PKCS12 must still fail identity admission after the platform fix.
            using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
            byte[] invalid = publicOnly.Export(X509ContentType.Pkcs12, password);
            AssertThrows<InvalidOperationException>(() => CloudflareApiClient.TryCreate(() => new MemoryStream(invalid))?.Dispose());
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXA_API_CLIENT_CERT_PATH", originalPath);
            Environment.SetEnvironmentVariable("NEXA_API_CLIENT_CERT_PASSWORD", originalPassword);
            Directory.Delete(root, recursive: true);
        }
    }
}
