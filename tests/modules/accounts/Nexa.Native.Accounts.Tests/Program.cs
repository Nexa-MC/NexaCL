using System.Net;
using Nexa.Native.Accounts.Client;
using Nexa.Services.Accounts;
using Nexa.Services.Foundation;
using Nexa.Services.Logging;
using Nexa.Xsr;
using Nexa.Xsr.State;

const string secret = "native-account-private-test-token";
var builder = new XsrStateStoreBuilder();
AccountStateContract.DeclareState(builder); AccountOnboardingState.DeclareState(builder); AccountSkinContract.DeclareState(builder);
LogService.DeclareState(builder);
NativeAccountService.DeclareReplicas(builder);
var store = builder.Build();
var network = new NetworkPolicy();
using var logging = new LogService(store) { MaximumLevel = LogLevel.Debug };
var transport = new TransportErrors(); logging.AddSink(transport);
await using var accounts = new NativeAccountService(store, new MemoryStore(secret), logging);
accounts.AttachHttp(new HttpClient(network));
await accounts.Initialization.WaitAsync(TimeSpan.FromSeconds(20));
Console.WriteLine("PASS: native base storage initialized");
var rosterId = store.Resolve(AccountStateContract.ProfilesKey);
var profiles = store.ReadCollection<LaunchProfileView>(rosterId);
Check(profiles.Items.Count == 2 && profiles.Revision > 0, "Initial roster was not replicated.");
Check(accounts.SelectedIndex == 0, "Initial selection was not replicated.");
Check(!string.Join('\n', store.CaptureSnapshot().Entries.Select(entry => entry.Value)).Contains(secret, StringComparison.Ordinal), "Credentials entered public state.");
var privateProfile = await accounts.GetProfileAsync(1);
Check(privateProfile.IsSuccess && privateProfile.Value.AccessToken == secret, "Privileged profile values changed across runtimes.");
await accounts.ConfigureRuntimeAsync(new("native-fixture-client", null), null);
Console.WriteLine("PASS: native provider runtime configured");
var offline = await accounts.GetProfileAsync(0);
var identity = await accounts.ResolveAsync(0, offline.Value);
Check(identity.IsSuccess && identity.Value.PlayerName == "FixturePlayer" && identity.Value.PlayerUuid == Nexa.Services.Minecraft.Launch.MinecraftOfflineIdentity.LegacyMismatchedUuid("FixturePlayer") && identity.Value.OfflineSkinPng.Length == 65536 && identity.Value.OfflineSkinPng[0] == 42, "Offline launch resolution or its bounded binary texture payload changed.");
var selected = await accounts.SelectAsync(new(1, profiles.Revision));
Check(selected.IsSuccess && accounts.SelectedIndex == 1, "Selection completed before its state became visible.");
var missing = await accounts.SelectAsync(new(99, profiles.Revision));
Check(!missing.IsSuccess && accounts.SelectedIndex == 1, "Invalid selection mutated account state.");
var stale = await accounts.RemoveAsync(new(1, profiles.Revision + 1));
Check(!stale.IsSuccess && accounts.GetViews().Count == 2, "Stale roster command was accepted.");
Console.WriteLine("PASS: privileged identity and expected revisions");
var sites = await accounts.QueryWardrobeCatalogSitesQueryAsync(new());
Check(sites.IsSuccess && sites.Value.Count > 0 && sites.Value.All(site => site.BaseUri.IsAbsoluteUri), "Catalog query lost typed values.");
var absent = await accounts.GetProfileAsync(42);
Check(!absent.IsSuccess && absent.Error?.Code == AccountErrors.ProfileNotFoundCode, "Stable account errors changed.");
try { await accounts.GetProfileAsync(0, new(true)); throw new InvalidOperationException("Pre-canceled request was accepted."); }
catch (OperationCanceledException) { }
var reads = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => accounts.GetProfileAsync(0).AsTask()));
Check(reads.All(result => result.IsSuccess), "Concurrent native queries failed.");
var selections = await Task.WhenAll(Enumerable.Range(0, 64).Select(index => accounts.SelectAsync(new(index % 2, profiles.Revision)).AsTask()));
Check(selections.All(result => result.IsSuccess), "Concurrent selection commands failed.");
var settled = await accounts.SelectAsync(new(0, profiles.Revision));
Check(settled.IsSuccess && accounts.SelectedIndex == 0, "Coalesced state did not settle before command completion.");
Console.WriteLine("PASS: typed, concurrent and coalesced account state queries");
var login = await accounts.DispatchAccountLoginStartCommandAsync(new(AccountLoginProvider.Microsoft));
Check(login.IsSuccess, "Device login was not admitted.");
await network.Polling.Task.WaitAsync(TimeSpan.FromSeconds(10));
var progress = store.Read<AccountLoginSnapshot>(store.Resolve(AccountOnboardingState.Login));
Check(progress.Value.UserCode == "TEST-CODE" && progress.Value.Phase == AccountLoginPhase.AwaitingAuthorization,
    "Credential-free device progress was not replicated.");
var canceled = await accounts.DispatchAccountLoginCancelCommandAsync(new(progress.Value.Generation));
Check(canceled.IsSuccess, "Native login cancellation was rejected.");
await network.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
await WaitAsync(() => store.Read<AccountLoginSnapshot>(store.Resolve(AccountOnboardingState.Login)).Value.Phase == AccountLoginPhase.Cancelled);
Check(network.PolicyObserved, "Account requests bypassed the configured HTTP policy.");
Console.WriteLine("PASS: reverse HTTP policy and device login cancellation");
var removed = await accounts.RemoveAsync(new(1, profiles.Revision));
Check(removed.IsSuccess && accounts.GetViews().Count == 1 && accounts.SelectedIndex == 0, "Durable mutation/state/completion ordering changed.");
await accounts.DispatchAccountLoginStartCommandAsync(new(AccountLoginProvider.Microsoft));
await WaitAsync(() => network.PollCount == 2);
await accounts.DispatchAccountLoginStartCommandAsync(new(AccountLoginProvider.Microsoft));
await WaitAsync(() => network.PollCount == 3);
Console.WriteLine("PASS: superseded device requests admitted");
await accounts.DisposeAsync();
Check(network.CancelCount == 3, "Shutdown did not drain canceled and superseded HTTP workers.");
Check(!transport.Failed, "A callback reached a retired native account endpoint.");
await accounts.DisposeAsync();
await VerifyFailureDiagnosticsAsync();
if (OperatingSystem.IsWindows()) await VerifyLegacyProfileUpgradeAsync();
Console.WriteLine("Independent native account runtimes passed storage, privileged identity, typed queries, revisions, HTTP policy, cancellation and drained shutdown.");

static XsrStateStore UpgradeStore()
{
    XsrStateStoreBuilder builder = new();
    AccountStateContract.DeclareState(builder); AccountOnboardingState.DeclareState(builder); AccountSkinContract.DeclareState(builder);
    LogService.DeclareState(builder); NativeAccountService.DeclareReplicas(builder);
    return builder.Build();
}
static async Task VerifyFailureDiagnosticsAsync()
{
    var state = UpgradeStore(); using var log = new LogService(state);
    await using var client = new NativeAccountService(state, new InvalidStore(), log);
    client.AttachHttp(new HttpClient(new NetworkPolicy()));
    bool rejected = false;
    try { await client.Initialization.WaitAsync(TimeSpan.FromSeconds(10)); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "Invalid profile path was accepted.");
    Check(log.GetSnapshot().Any(entry => entry.Module == "Account" && entry.Level == LogLevel.Error
        && entry.ExceptionText?.Contains("System.IO.InvalidDataException", StringComparison.Ordinal) == true),
        "The original native initialization exception was not recorded before retirement.");
    Console.WriteLine("PASS: native initialization diagnostics retain original exception facts");
}
static async Task VerifyLegacyProfileUpgradeAsync()
{
    string root = Path.Combine(Path.GetTempPath(), "nexa-a21-native-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        string path = Path.Combine(root, "profiles.json");
        // Literal A21 JSON: no new fields, rather than JSON serialized by A22.
        File.WriteAllText(path, """
            {"schemaVersion":1,"profiles":[{"username":"LegacyPlayer","kind":"Microsoft","uuid":"0123456789abcdef0123456789abcdef","accessToken":"upgrade-fixture-token"}]}
            """);
        byte[]? encrypted = null;
        for (int pass = 0; pass < 2; pass++)
        {
            var state = UpgradeStore(); using var log = new LogService(state);
            log.AddSink(new ConsoleLogSink());
            await using (var client = new NativeAccountService(state, new NativeProfileStore(path), log))
            {
                client.AttachHttp(new HttpClient(new UpgradeTextureNetwork()));
                await client.Initialization.WaitAsync(TimeSpan.FromSeconds(15));
                var loaded = await client.GetProfileAsync(0);
                Check(loaded.IsSuccess && loaded.Value.AccessToken == "upgrade-fixture-token"
                    && loaded.Value.OfflineSkinPng.Length == 0 && loaded.Value.OfflineCapePng.Length == 0,
                    "Legacy protected profile initialization lost credentials or optional texture defaults.");
                await client.ConfigureRuntimeAsync(new("native-fixture-client", null), root);
            }
            byte[] current = File.ReadAllBytes(path);
            Check(current.AsSpan().StartsWith("NEXAAC01"u8), "Legacy plaintext did not enter protected storage.");
            if (encrypted is not null) Check(encrypted.SequenceEqual(current), "Reading a protected legacy file rewrote credentials.");
            encrypted = current;
        }
        Console.WriteLine("PASS: native Windows A21 plaintext and protected profiles upgrade and retire without rewriting credentials");
    }
    finally { Directory.Delete(root, true); }
}
static void Check(bool condition, string message)
{ if (!condition) throw new InvalidOperationException(message); }
static async Task WaitAsync(Func<bool> ready)
{
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
    while (!ready()) await Task.Delay(10, timeout.Token);
}
sealed class MemoryStore(string secret) : ILaunchProfilePort, INativeAccountProfileStore
{
    public string? NativeProfilePath => null;
    public LaunchProfileSet NativeInitialProfiles => new()
    {
        Profiles = [new() { Username = "FixturePlayer", Kind = LaunchProfileKind.Offline, OfflineCustomUuid = true, Uuid = Nexa.Services.Minecraft.Launch.MinecraftOfflineIdentity.LegacyMismatchedUuid("FixturePlayer"), OfflineSkinPng = Enumerable.Repeat((byte)42, 65536).ToArray() },
            new() { Username = "PrivatePlayer", Kind = LaunchProfileKind.ThirdParty, Uuid = "0123456789abcdef0123456789abcdef", AccessToken = secret }],
    };
    public LaunchProfileSet Load() => throw new InvalidOperationException("Caller must not load native storage.");
    public void Save(LaunchProfileSet profiles) => throw new InvalidOperationException("Caller must not save native storage.");
}
sealed class InvalidStore : ILaunchProfilePort, INativeAccountProfileStore
{
    public string? NativeProfilePath => "relative-profile-path";
    public LaunchProfileSet NativeInitialProfiles => new();
    public LaunchProfileSet Load() => throw new NotSupportedException();
    public void Save(LaunchProfileSet profiles) => throw new NotSupportedException();
}
sealed class UpgradeTextureNetwork : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
}
sealed class NetworkPolicy : HttpMessageHandler
{
    public TaskCompletionSource Polling { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool PolicyObserved { get; private set; }
    private int _pollCount, _cancelCount;
    public int PollCount => Volatile.Read(ref _pollCount);
    public int CancelCount => Volatile.Read(ref _cancelCount);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        PolicyObserved = true;
        if (request.RequestUri!.AbsolutePath.EndsWith("/devicecode", StringComparison.Ordinal))
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (!body.Contains("native-fixture-client", StringComparison.Ordinal)) throw new InvalidDataException("Configured OAuth identity was lost.");
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"device_code":"private-device-code","user_code":"TEST-CODE","verification_uri":"https://microsoft.com/link","expires_in":30,"interval":1}
                """) };
        }
        if (request.RequestUri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _pollCount); Polling.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException)
            {
                // Caller cancellation completes before the transport physically retires.
                await Task.Delay(50, CancellationToken.None);
                Interlocked.Increment(ref _cancelCount); Canceled.TrySetResult(); throw;
            }
        }
        throw new InvalidDataException("Unexpected account HTTP route.");
    }
}
sealed class TransportErrors : ILogSink
{
    private int _failed;
    public bool Failed => Volatile.Read(ref _failed) != 0;
    public void Write(LogEntry entry, string formattedLine)
    {
        if (entry.Module == "Accounts" && entry.Message == "Native account operation failed.")
            Interlocked.Exchange(ref _failed, 1);
    }
}
