using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using Nexa.Services.Accounts;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private const string MinecraftSessionEndpoint = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private static readonly MicrosoftDeviceCodeInfo MicrosoftConnectionDevice = new("PRIVATE-DEVICE", "CODE", "https://www.microsoft.com/link",
        null, "", TimeSpan.FromMinutes(10), TimeSpan.Zero);

    private static ScriptedHandler MicrosoftConnectionFixture(HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new ScriptedHandler();
        handler.Serve("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
            """{"device_code":"PRIVATE-DEVICE","user_code":"CODE","verification_uri":"https://www.microsoft.com/link","expires_in":900,"interval":1}""");
        handler.Serve("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", """{"access_token":"PRIVATE-MS","refresh_token":"PRIVATE-REFRESH"}""");
        handler.Serve("https://user.auth.xboxlive.com/user/authenticate", """{"Token":"PRIVATE-XBOX","DisplayClaims":{"xui":[{"uhs":"HASH"}]}}""");
        handler.Serve("https://xsts.auth.xboxlive.com/xsts/authorize", """{"Token":"PRIVATE-XSTS","DisplayClaims":{"xui":[{"uhs":"HASH"}]}}""");
        handler.Serve(MinecraftSessionEndpoint, status == HttpStatusCode.OK ? """{"access_token":"PRIVATE-MINECRAFT"}"""
            : """{"errorMessage":"must-not-display-PRIVATE-XSTS"}""", status);
        handler.Serve("https://api.minecraftservices.com/minecraft/profile", """{"name":"Steve","id":"uuid-steve","skins":[]}""");
        handler.Serve("https://api.minecraftservices.com/entitlements/mcstore", """{"items":[{"name":"product_minecraft"}]}""");
        return handler;
    }

    private static HttpRequestException InterruptedMicrosoftTls() => new(HttpRequestError.SecureConnectionError,
        "handshake ended", new IOException("remote connection closed"));

    private static async ValueTask MicrosoftMinecraftHandshakeRetriesWithoutRepeatingAuthorization()
    {
        using var handler = MicrosoftConnectionFixture();
        handler.Fail(MinecraftSessionEndpoint, InterruptedMicrosoftTls());
        using var http = new HttpClient(handler);
        var log = CreateLogService();
        List<TimeSpan> waits = [];
        using var clock = new SchedulingClock();
        var accounts = CreatePreparationAccounts();
        AccountLoginSnapshot? retrySnapshot = null;
        var auth = new MicrosoftMinecraftAuthService(http, (delay, _) =>
        {
            waits.Add(delay);
            if (delay == TimeSpan.FromMilliseconds(300)) retrySnapshot = PreparationSnapshot(accounts);
            return Task.CompletedTask;
        }, log);
        using var onboarding = CreatePreparationService(clock, accounts, http, auth);
        AssertTrue(onboarding.Start(new(AccountLoginProvider.Microsoft)).IsSuccess);
        await onboarding.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));
        AssertEqual(AccountLoginPhase.Completed, PreparationSnapshot(accounts).Phase);
        AssertEqual(1, accounts.GetViews().Count);
        AssertEqual(2, handler.Requests.Count(request => request.EndsWith(MinecraftSessionEndpoint, StringComparison.Ordinal)));
        foreach (string path in new[] { "/token", "/user/authenticate", "/xsts/authorize" })
            AssertEqual(1, handler.Requests.Count(request => request.EndsWith(path, StringComparison.Ordinal)));
        AssertTrue(waits.Contains(TimeSpan.FromMilliseconds(300)));
        AssertTrue(retrySnapshot is { Phase: AccountLoginPhase.Starting, UserCode: "", VerificationUri: "" });
        string diagnostics = DiagnosticText(log);
        foreach (string secret in new[] { "PRIVATE-MS", "PRIVATE-REFRESH", "PRIVATE-XSTS", "PRIVATE-MINECRAFT" })
            AssertFalse(diagnostics.Contains(secret, StringComparison.Ordinal));
    }

    private static async ValueTask MicrosoftMinecraftRetriesAreBoundedAndRespectCertificateFailure()
    {
        foreach (bool certificate in new[] { false, true })
        {
            using var handler = MicrosoftConnectionFixture();
            int attempts = certificate ? 1 : 4;
            for (int i = 0; i < attempts; i++) handler.Fail(MinecraftSessionEndpoint, certificate
                ? new HttpRequestException(HttpRequestError.SecureConnectionError, "untrusted certificate", new AuthenticationException("not trusted"))
                : InterruptedMicrosoftTls());
            using var http = new HttpClient(handler);
            var auth = new MicrosoftMinecraftAuthService(http, (_, _) => Task.CompletedTask);
            bool rejected = false;
            try { await auth.CompleteDeviceLoginAsync("client-id", MicrosoftConnectionDevice); }
            catch (MicrosoftAuthenticationFailure failure)
            {
                rejected = true;
                AssertTrue(failure.Message.Contains(certificate ? "证书" : "Minecraft", StringComparison.Ordinal));
                AssertTrue(failure.InnerException is HttpRequestException);
            }
            AssertTrue(rejected);
            AssertEqual(attempts, handler.Requests.Count(request => request.EndsWith(MinecraftSessionEndpoint, StringComparison.Ordinal)));
            AssertFalse(handler.Requests.Any(request => request.EndsWith("/minecraft/profile", StringComparison.Ordinal)));
        }
    }

    private static async ValueTask MicrosoftMinecraftRetryCancellationStopsTheExchange()
    {
        using var handler = MicrosoftConnectionFixture();
        handler.Fail(MinecraftSessionEndpoint, InterruptedMicrosoftTls());
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var auth = new MicrosoftMinecraftAuthService(http, (delay, token) =>
        {
            if (delay > TimeSpan.Zero) cancellation.Cancel();
            return token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;
        });
        bool cancelled = false;
        try { await auth.CompleteDeviceLoginAsync("client-id", MicrosoftConnectionDevice, cancellationToken: cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        AssertTrue(cancelled);
        AssertEqual(1, handler.Requests.Count(request => request.EndsWith(MinecraftSessionEndpoint, StringComparison.Ordinal)));
    }

    private static async ValueTask MicrosoftMinecraftHttpRejectionKeepsStatusAndSafeGuidance()
    {
        using var handler = MicrosoftConnectionFixture(HttpStatusCode.Forbidden);
        using var http = new HttpClient(handler);
        var auth = new MicrosoftMinecraftAuthService(http, (_, _) => Task.CompletedTask);
        using var clock = new SchedulingClock();
        var accounts = CreatePreparationAccounts();
        using var onboarding = CreatePreparationService(clock, accounts, http, auth);
        AssertTrue(onboarding.Start(new(AccountLoginProvider.Microsoft)).IsSuccess);
        await onboarding.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));
        var snapshot = PreparationSnapshot(accounts);
        AssertEqual(AccountLoginPhase.Failed, snapshot.Phase);
        AssertTrue(snapshot.Message.Contains("HTTP 403", StringComparison.Ordinal));
        AssertTrue(snapshot.Message.Contains("Minecraft", StringComparison.Ordinal));
        AssertFalse(snapshot.Message.Contains("PRIVATE", StringComparison.Ordinal));
        AssertFalse(snapshot.Message.Contains("无法连接", StringComparison.Ordinal));
        AssertEqual(0, accounts.GetViews().Count);
        AssertEqual(1, handler.Requests.Count(request => request.EndsWith(MinecraftSessionEndpoint, StringComparison.Ordinal)));
    }
}
