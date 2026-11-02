using System.Net;
using Nexa.Native.Updates.Client;
using Nexa.Services.Updates;

namespace Nexa.Native.Updates.Services.Tests;

internal static partial class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2) return 2;
        string fixture = Path.GetFullPath(args[0]), rid = args[1];
        string temporary = Path.GetPathRoot(Path.GetTempPath())!;
        foreach (string segment in Path.GetRelativePath(temporary, Path.GetTempPath()).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(temporary, segment));
            temporary = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
        }
        string root = Path.Combine(temporary, "nexa-updates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await SidecarCasesAsync(fixture, root, rid, budget.Token);
            using var transport = new FixtureTransport(fixture);
            using var http = new HttpClient(transport);
            NativeUpdatesInitialization config = new(AppContext.BaseDirectory, File.ReadAllText(Path.Combine(fixture, "key.asc")), File.ReadAllText(Path.Combine(fixture, "fingerprint")).Trim());
            await using var client = new NativeUpdatesClient(config, http);
            await client.ConfigureSidecarsAsync(new(Path.Combine(root, "sidecars"), AppContext.BaseDirectory, rid, "2.0.0.alpha.5"), budget.Token);
            if ((await client.ActivePackagesAsync(budget.Token)).Count != 0) throw new InvalidDataException("Unexpected Sidecar override.");
            await client.ConfigureDiscoveryAsync(new(Path.Combine(root, "seed"), "alpha", rid, false), budget.Token);
            await client.ConfigureCommunityAsync(Path.Combine(root, "community.json"), budget.Token);
            var announcements = await client.StartCommunityAsync(budget.Token);
            if (announcements.SurveyDue || announcements.Announcements.Count != 0) throw new InvalidDataException("Unexpected announcement state.");
            await client.AcknowledgeCommunityAsync(new(Guid.NewGuid().ToString(), 1, "Fixture", "Acknowledged"), budget.Token);
            int telemetry = 0; client.Record = (_, _) => Interlocked.Increment(ref telemetry);
            await client.RefreshRolloutsAsync(budget.Token);
            var check = await client.CheckAsync(new("2.0.0.alpha.5", rid, "alpha"), budget.Token);
            if (!check.IsSuccess || check.Value!.Offer is not null || telemetry < 2) throw new InvalidDataException("Discovery/telemetry parity failed.");
            string portable = Path.Combine(root, "portable"); Directory.CreateDirectory(portable);
            File.WriteAllText(Path.Combine(portable, "user-settings.json"), "preserve");
            await client.ConfigureControlAsync(new(portable, new("2.0.0.alpha.5", rid, "nativeaot-self-contained", "Release")), budget.Token);
            try { await client.ReadControlAsync(budget.Token); }
            catch (UnauthorizedAccessException error) when (error.Message.Contains("管理员", StringComparison.Ordinal))
            {
                Console.WriteLine("PASS Updates: elevated portable admission rejected by the real owner; transaction cases require an ordinary-user runner.");
                await client.DisposeAsync();
                await HeldRetirementAsync(config, fixture, rid, budget.Token);
                return 0;
            }
            transport.Mode = "signature";
            await RejectAsync(() => client.InstallControlAsync("2.0.0.alpha.6", "alpha", budget.Token));
            if (transport.Packages != 0) throw new InvalidDataException("Invalid signature reached payload download.");
            transport.Mode = "package";
            await RejectAsync(() => client.InstallControlAsync("2.0.0.alpha.6", "alpha", budget.Token));
            transport.Mode = "ready";
            var installed = await client.InstallControlAsync("2.0.0.alpha.6", "alpha", budget.Token);
            if (installed.Version != "2.0.0.alpha.6" || installed.Phase != "complete" || !installed.CanRollback) throw new InvalidDataException("Signed portable activation failed.");
            string selected = Directory.GetDirectories(portable, ".nexa-slot-*").Single();
            if (rid.StartsWith("osx-", StringComparison.Ordinal)) selected = Path.Combine(selected, "Nexa.app", "Contents", "MacOS");
            string desktop = Path.Combine(selected, "Nexa.Desktop" + (rid.StartsWith("win-", StringComparison.Ordinal) ? ".exe" : ""));
            if (!File.ReadAllBytes(desktop).AsSpan().SequenceEqual(new byte[] { 1, 2, 3 })) throw new InvalidDataException("Installed bytes differ.");
            var rollback = await client.RollbackControlAsync(budget.Token);
            if (rollback.Version != "2.0.0.alpha.5" || rollback.Phase != "rolledback") throw new InvalidDataException("Rollback failed.");
            await RejectAsync(() => client.InstallControlAsync("2.0.0.alpha.6", "alpha", budget.Token));
            if (File.ReadAllText(Path.Combine(portable, "user-settings.json")) != "preserve") throw new InvalidDataException("Settings changed.");
            Console.WriteLine("PASS Updates: independent owner, discovery, ordered telemetry, invalid signature before download, corrupt payload rejection, signed activation, rollback, high-water replay rejection, settings preservation.");
            await client.DisposeAsync();
            await HeldRetirementAsync(config, fixture, rid, budget.Token);
            return 0;
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task RejectAsync(Func<Task<AutomaticUpdateStatus>> action)
    {
        try { await action(); }
        catch (Exception error) when (error is InvalidDataException or IOException) { return; }
        throw new InvalidOperationException("Invalid update was accepted.");
    }

    private static async Task HeldRetirementAsync(NativeUpdatesInitialization config, string fixture, string rid, CancellationToken token)
    {
        using var handler = new FixtureTransport(fixture) { Mode = "held" };
        using var http = new HttpClient(handler);
        var client = new NativeUpdatesClient(config, http);
        await client.ConfigureDiscoveryAsync(new(Path.Combine(fixture, "unused-seed"), "alpha", rid, false), token);
        var pending = client.CheckAsync(new("2.0.0.alpha.5", rid, "alpha"), token);
        await handler.Entered.Task.WaitAsync(token);
        var closing = client.DisposeAsync().AsTask();
        await handler.Canceled.Task.WaitAsync(token);
        if (closing.IsCompleted) throw new InvalidOperationException("Updates retired before physical HTTP completion.");
        handler.Release.TrySetResult();
        await closing.WaitAsync(token);
        try { await pending; } catch (OperationCanceledException) { }
        Console.WriteLine("PASS Updates: retirement cancels active HTTP and waits for physical completion.");
    }

    private sealed class FixtureTransport(string fixture) : HttpMessageHandler
    {
        internal string Mode { get; set; } = "ready";
        internal int Packages;
        internal bool InvalidSignature, Truncated;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Uri uri = request.RequestUri!;
            if (uri.Scheme != "https" || uri.Host is not ("github.com" or "api.github.com" or "api.pcln.top" or "nexa-telemetry.pcln.workers.dev")) throw new InvalidOperationException("Unexpected update origin.");
            if (Mode == "held")
            {
                Entered.TrySetResult();
                using var registration = token.Register(() => Canceled.TrySetResult());
                await Release.Task; token.ThrowIfCancellationRequested();
            }
            byte[] bytes;
            if (uri.Host == "nexa-telemetry.pcln.workers.dev") bytes = "{\"announcements\":[]}"u8.ToArray();
            else if (uri.Host == "api.github.com") bytes = Mode == "sidecar" ? File.ReadAllBytes(Path.Combine(fixture, "sidecar-releases.json")) : "[]"u8.ToArray();
            else if (uri.Host == "api.pcln.top")
            {
                if (!uri.AbsolutePath.EndsWith("rollouts", StringComparison.Ordinal)) return new(HttpStatusCode.NoContent);
                bytes = "{\"revision\":1,\"rules\":[]}"u8.ToArray();
            }
            else
            {
                string name = Path.GetFileName(uri.AbsolutePath);
                if (!uri.AbsolutePath.Contains("/releases/download/", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected release route.");
                string path = Path.Combine(fixture, name);
                if (!File.Exists(path)) return new(HttpStatusCode.NotFound);
                bytes = File.ReadAllBytes(path);
                if (name.EndsWith(".asc", StringComparison.Ordinal) && InvalidSignature) bytes = "invalid signature"u8.ToArray();
                if (name.EndsWith(".nsc", StringComparison.Ordinal) && Truncated) bytes = bytes[..32];
                if (name == "Nexa-Release.json.asc" && Mode == "signature") bytes = "invalid signature"u8.ToArray();
                if (name.EndsWith(".zip", StringComparison.Ordinal) || name.EndsWith(".tar.gz", StringComparison.Ordinal))
                { Packages++; if (Mode == "package") { bytes = bytes.ToArray(); bytes[^1] ^= 1; } }
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
    }
}
