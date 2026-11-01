using Nexa.Native.Updates.Client;
using Nexa.Services.Updates;

namespace Nexa.Native.Updates.Services.Tests;

internal static partial class Program
{
    private static async Task SidecarCasesAsync(string fixture, string root, string rid, CancellationToken token)
    {
        const string id = "Fixture.Package";
        string bundled = Path.Combine(root, "bundled"), cache = Path.Combine(root, "sidecar-cache");
        Directory.CreateDirectory(bundled);
        File.WriteAllBytes(Path.Combine(bundled, id + ".nsc"), new byte[128]);
        NativeUpdatesInitialization config = new(AppContext.BaseDirectory, File.ReadAllText(Path.Combine(fixture, "sidecar-key.asc")), File.ReadAllText(Path.Combine(fixture, "sidecar-fingerprint")).Trim());
        using var handler = new FixtureTransport(fixture) { Mode = "sidecar" };
        using var http = new HttpClient(handler);
        await using var client = new NativeUpdatesClient(config, http);
        await client.ConfigureSidecarsAsync(new(cache, bundled, rid, "2.0.0.alpha.5"), token);
        if ((await client.ActivePackagesAsync(token)).Count != 0) throw new InvalidDataException("Unexpected initial Sidecar selection.");
        handler.InvalidSignature = true;
        try { await client.CheckSidecarsAsync("alpha", token); throw new InvalidOperationException("Unsigned Sidecar catalog accepted."); }
        catch (InvalidDataException) { }
        handler.InvalidSignature = false;
        await client.CheckSidecarsAsync("alpha", token);
        if (!client.SidecarSnapshot.Items.Single().CanInstall) throw new InvalidDataException("Signed Sidecar offer unavailable.");
        handler.Truncated = true;
        try { await client.InstallSidecarAsync(id, token); throw new InvalidOperationException("Truncated Sidecar accepted."); }
        catch (InvalidDataException) { }
        if (File.Exists(Path.Combine(cache, id, "active"))) throw new InvalidDataException("Incomplete Sidecar activated.");
        handler.Truncated = false;
        await client.InstallSidecarAsync(id, token);
        if (client.SidecarSnapshot.Items.Single().Phase != "restart") throw new InvalidDataException("Sidecar progress/state did not settle.");
        var active = await client.ActivePackagesAsync(token);
        if (active.Count != 1 || !File.ReadAllBytes(active[id]).AsSpan().SequenceEqual(Enumerable.Range(0, 128).Select(i => (byte)i).ToArray())) throw new InvalidDataException("Signed Sidecar payload mismatch.");
        File.WriteAllBytes(active[id], new byte[128]);
        if ((await client.ActivePackagesAsync(token)).Count != 0) throw new InvalidDataException("Damaged installed Sidecar admitted.");
        await client.CheckSidecarsAsync("alpha", token);
        await client.InstallSidecarAsync(id, token);
        if ((await client.ActivePackagesAsync(token)).Count != 1 || Directory.GetDirectories(Path.Combine(cache, id), ".download-*").Length != 0) throw new InvalidDataException("Sidecar repair left an incomplete publication.");
        Console.WriteLine("PASS Updates: real signed Sidecar catalog/image, invalid signature, truncated download, progress replica, atomic activation, installed-byte revalidation and repair.");
    }
}
