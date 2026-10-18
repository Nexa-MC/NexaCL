using System.Net;
using Nexa.Services.Accounts;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask AccountPreparationBoundsStalledResponseBodies()
    {
        foreach (AccountLoginProvider provider in new[] { AccountLoginProvider.Microsoft, AccountLoginProvider.LittleSkin, AccountLoginProvider.ThirdParty })
        {
            using SchedulingClock clock = new();
            using PreparationHttp handler = new(stallBody: true);
            using HttpClient http = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
            AccountService accounts = CreatePreparationAccounts();
            using AccountOnboardingService service = CreatePreparationService(clock, accounts, http);
            AssertTrue(service.Start(PreparationCommand(provider)).IsSuccess);
            await handler.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(AccountLoginPhase.Starting, PreparationSnapshot(accounts).Phase);
            clock.Advance(TimeSpan.FromSeconds(30));
            await service.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));
            AccountLoginSnapshot snapshot = PreparationSnapshot(accounts);
            AssertEqual(AccountLoginPhase.Failed, snapshot.Phase);
            AssertTrue(snapshot.Message.Contains("超时", StringComparison.Ordinal));
            AssertFalse(snapshot.IsBusy);
            AssertEqual(0, accounts.GetViews().Count);
            AssertEqual(0, clock.ActiveTimers);
            AssertTrue(service.Start(new(AccountLoginProvider.Offline, "Recovered")).IsSuccess);
            await service.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(AccountLoginPhase.Completed, PreparationSnapshot(accounts).Phase);
        }
    }

    private static async ValueTask AccountPreparationCancellationAndReplacementDoNotWaitForTransport()
    {
        foreach (AccountLoginProvider provider in new[] { AccountLoginProvider.Microsoft, AccountLoginProvider.LittleSkin, AccountLoginProvider.ThirdParty })
            foreach (bool replace in new[] { false, true })
            {
                using SchedulingClock clock = new();
                using PreparationHttp handler = new(stallBody: false);
                using HttpClient http = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
                AccountService accounts = CreatePreparationAccounts();
                using AccountOnboardingService service = CreatePreparationService(clock, accounts, http);
                service.Start(PreparationCommand(provider));
                await handler.HeadersStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Task old = service.WhenIdle;
                if (replace) service.Start(new(AccountLoginProvider.Offline, "Newer"));
                else service.Cancel(PreparationSnapshot(accounts).Generation);
                await old.WaitAsync(TimeSpan.FromSeconds(5));
                await service.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));
                AssertEqual(replace ? AccountLoginPhase.Completed : AccountLoginPhase.Cancelled, PreparationSnapshot(accounts).Phase);
                long generation = PreparationSnapshot(accounts).Generation;
                // This transport deliberately ignores cancellation. Its eventual response is not
                // permission to reopen authorization or replace the newer offline profile.
                handler.Headers.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{}") });
                await handler.Returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
                AssertEqual(generation, PreparationSnapshot(accounts).Generation);
                AssertEqual(replace ? 1 : 0, accounts.GetViews().Count);
                if (replace) AssertEqual("Newer", accounts.GetViews()[0].Username);
                AssertEqual(0, clock.ActiveTimers);
            }
    }

    private static async ValueTask AccountPreparationBoundsOfflineOwnershipRefresh()
    {
        using SchedulingClock clock = new();
        using PreparationHttp handler = new(stallBody: false);
        using HttpClient http = new(handler);
        AccountService accounts = CreatePreparationAccounts();
        accounts.ConfigureRegionalPolicy(new("US"));
        AssertTrue(accounts.AddProfile(SampleProfile("Owner", "OLD-ACCESS") with
        { Kind = LaunchProfileKind.Microsoft, RefreshToken = "PRIVATE-REFRESH" }).IsSuccess);
        PreparationMicrosoft microsoft = new();
        using AccountOnboardingService service = CreatePreparationService(clock, accounts, http, microsoft);
        service.Start(new(AccountLoginProvider.Offline, "Offline"));
        await microsoft.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AssertTrue(PreparationSnapshot(accounts).Message.Contains("正版资格", StringComparison.Ordinal));
        clock.Advance(TimeSpan.FromSeconds(30));
        await service.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));
        AssertEqual(AccountLoginPhase.Failed, PreparationSnapshot(accounts).Phase);
        microsoft.Refreshed.SetResult(new("Owner", "uuid-Owner", "LATE-ACCESS", "LATE-REFRESH", null, true));
        await microsoft.RefreshReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AssertFalse(accounts.HasVerifiedMinecraftOwnership);
        AssertEqual("OLD-ACCESS", accounts.GetProfile(0).Value!.AccessToken);
        AssertEqual(1, accounts.GetViews().Count);
        AssertEqual(0, handler.Requests);
    }

    private static async ValueTask AccountPreparationBudgetEndsBeforeUserAuthorization()
    {
        using SchedulingClock clock = new();
        using PreparationHttp handler = new(stallBody: false);
        using HttpClient http = new(handler);
        AccountService accounts = CreatePreparationAccounts();
        PreparationMicrosoft microsoft = new();
        using AccountOnboardingService service = CreatePreparationService(clock, accounts, http, microsoft);
        service.Start(new(AccountLoginProvider.Microsoft));
        await microsoft.AuthorizationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AssertEqual(AccountLoginPhase.AwaitingAuthorization, PreparationSnapshot(accounts).Phase);
        AssertEqual(0, clock.ActiveTimers);
        clock.Advance(TimeSpan.FromMinutes(2));
        AssertEqual(AccountLoginPhase.AwaitingAuthorization, PreparationSnapshot(accounts).Phase);
        microsoft.Authorized.SetResult(new("Online", "online-uuid", "PRIVATE-ACCESS", "PRIVATE-REFRESH", null, true));
        await service.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));
        AssertEqual(AccountLoginPhase.Completed, PreparationSnapshot(accounts).Phase);
    }

    private static AccountService CreatePreparationAccounts()
    {
        XsrStateStoreBuilder builder = new();
        AccountService.DeclareState(builder);
        AccountOnboardingState.DeclareState(builder);
        return new(builder.Build(), new ThrowingProfilePort(), null, new("CN"));
    }

    private static AccountOnboardingService CreatePreparationService(SchedulingClock clock, AccountService accounts,
        HttpClient http, IMicrosoftMinecraftAuthService? microsoft = null) =>
        new(clock, accounts, microsoft ?? new MicrosoftMinecraftAuthService(http), new LittleSkinOAuthService(http),
            new(http), new("fixture", LittleSkinConfig()), imports: new LegacyProfileImport(() => []));

    private static AccountLoginStartCommand PreparationCommand(AccountLoginProvider provider) =>
        new(provider, "Fixture", "https://auth.example", "PRIVATE-PASSWORD");

    private static AccountLoginSnapshot PreparationSnapshot(AccountService accounts) =>
        accounts.StateStore.Read<AccountLoginSnapshot>(accounts.StateStore.Resolve(AccountOnboardingState.Login)).Value!;

    private sealed class PreparationHttp(bool stallBody) : HttpMessageHandler
    {
        public int Requests;
        public TaskCompletionSource HeadersStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BodyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Headers { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            HeadersStarted.TrySetResult();
            if (stallBody) return new(HttpStatusCode.OK) { Content = new StreamContent(new PreparationBody(BodyStarted)) };
            HttpResponseMessage response = await Headers.Task;
            Returned.TrySetResult();
            return response;
        }
    }

    private sealed class PreparationBody(TaskCompletionSource started) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class PreparationMicrosoft : IMicrosoftMinecraftAuthService
    {
        public TaskCompletionSource RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RefreshReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AuthorizationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<MicrosoftMinecraftLoginResult> Refreshed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<MicrosoftMinecraftLoginResult> Authorized { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MicrosoftDeviceCodeInfo> RequestDeviceCodeAsync(string clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MicrosoftDeviceCodeInfo("PRIVATE-DEVICE", "PUBLIC-CODE", "https://www.microsoft.com/link", null,
                "", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1)));
        public Task<MicrosoftMinecraftLoginResult> CompleteDeviceLoginAsync(string clientId, MicrosoftDeviceCodeInfo deviceCode,
            IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        { AuthorizationStarted.TrySetResult(); return Authorized.Task; }
        public async Task<MicrosoftMinecraftLoginResult> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellationToken = default)
        {
            RefreshStarted.TrySetResult();
            MicrosoftMinecraftLoginResult result = await Refreshed.Task;
            RefreshReturned.TrySetResult();
            return result;
        }
    }
}
