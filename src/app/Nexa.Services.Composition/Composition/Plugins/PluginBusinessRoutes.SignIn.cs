using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Accounts;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private sealed class SignInFlow(PluginIdentity owner, EntityId id, AccountLoginProvider provider)
    {
        public PluginIdentity Owner { get; } = owner;
        public EntityId Id { get; } = id;
        public AccountLoginProvider Provider { get; } = provider;
        public long? Generation;
        public bool Cancelled;
        public readonly SignInGate Gate = new();
    }
    private sealed class SignInGate
    {
        private readonly System.Threading.Channels.Channel<bool> _slot = System.Threading.Channels.Channel.CreateBounded<bool>(1);
        public SignInGate() => _slot.Writer.TryWrite(true);
        public async ValueTask WaitAsync(CancellationToken token) { _ = await _slot.Reader.ReadAsync(token).ConfigureAwait(false); }
        public void Release() { if (!_slot.Writer.TryWrite(true)) throw new InvalidOperationException("Sign-in gate released twice."); }
    }
    private readonly Dictionary<EntityId, SignInFlow> _signInFlows = [];
    private AccountLoginSnapshot LoginState()
    {
        var state = store.Read<AccountLoginSnapshot>(store.Resolve(AccountOnboardingState.Login));
        return state.IsAvailable && state.Value is not null ? state.Value : throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
    }
    private SignInFlow FindSignIn(HostInvocation c, EntityId id)
    {
        lock (_planGate) return _signInFlows.TryGetValue(id, out var flow) && flow.Owner == c.Identity ? flow : throw new AbiHostException(AbiErrors.HANDLE_INVALID);
    }
    private SignInChallenge ProjectSignIn(SignInFlow flow)
    {
        if (flow.Cancelled) return new(flow.Id, SignInState.Cancelled, new("Sign-in cancelled", []), null, null);
        if (flow.Generation is not { } generation) return new(flow.Id, SignInState.Challenge, new("Provide account credentials", []), null, null);
        var state = LoginState();
        if (state.Generation != generation) throw new AbiHostException(AbiErrors.CONFLICT);
        var phase = state.Phase switch
        {
            AccountLoginPhase.Completed => SignInState.Completed,
            AccountLoginPhase.Cancelled => SignInState.Cancelled,
            AccountLoginPhase.Failed => SignInState.Failed,
            AccountLoginPhase.AwaitingAuthorization or AccountLoginPhase.ChoosingProfile => SignInState.Challenge,
            _ => SignInState.Pending
        };
        string prompt = state.Message;
        if (state.Phase == AccountLoginPhase.ChoosingProfile)
        {
            var choices = store.ReadCollection<AccountCharacterChoice>(store.Resolve(AccountOnboardingState.Characters));
            prompt += "\n" + string.Join('\n', choices.Items.Take(64).Select(p => p.Username + " (" + p.Uuid + ")"));
        }
        return new(flow.Id, phase, new(prompt, []), AccountVerificationContract.IsVerificationUri(flow.Provider, state.VerificationUri) ? new(state.VerificationUri) : null,
            state.UserCode.Length == 0 ? null : state.UserCode);
    }
    private async ValueTask StartSignIn(HostInvocation c, SignInFlow flow, string username, string password, string server, CancellationToken token)
    {
        var current = LoginState();
        if (current.IsBusy) throw new AbiHostException(AbiErrors.CONFLICT);
        await Command(accountCommands, AccountOnboardingRoutes.Start, new AccountLoginStartCommand(flow.Provider, username, server, password, expectedGeneration: current.Generation), token).ConfigureAwait(false);
        flow.Generation = checked(current.Generation + 1);
        if (LoginState().Generation != flow.Generation) throw new AbiHostException(AbiErrors.CONFLICT);
    }
    private IEnumerable<HostCapabilityRoute> SignInRoutes => [
        HostRoutes.Typed<SignInRequest, SignInChallenge>("Accounts", "BeginSignIn", async (c, v) =>
        {
            if (v.Scope != "application") throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var provider = v.Provider.Value switch
            {
                "nexa:accounts/offline" => AccountLoginProvider.Offline,
                "nexa:accounts/microsoft" => AccountLoginProvider.Microsoft,
                "nexa:accounts/littleskin" => AccountLoginProvider.LittleSkin,
                "nexa:accounts/thirdparty" => AccountLoginProvider.ThirdParty,
                "nexa:accounts/nide8" => AccountLoginProvider.Nide8,
                _ => throw new AbiHostException(AbiErrors.HANDLE_INVALID)
            };
            await Confirm(c, "accounts/sign-in", "Sign in with " + provider, SignInRequest.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            var flow = new SignInFlow(c.Identity, new(Guid.NewGuid().ToString("N")), provider);
            lock (_planGate)
            {
                if (_signInFlows.Count >= 32) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                _signInFlows.Add(flow.Id, flow);
            }
            try
            {
                _ = Services.RegisterHostCleanup(c, "nexa.accounts.write", async token =>
                {
                    await flow.Gate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        if (flow.Generation is { } generation && LoginState() is { IsBusy: true } current && current.Generation == generation)
                            await Command(accountCommands, AccountOnboardingRoutes.Cancel, new AccountLoginCancelCommand(generation), token).ConfigureAwait(false);
                        flow.Cancelled = true;
                    }
                    finally { flow.Gate.Release(); lock (_planGate) _signInFlows.Remove(flow.Id); }
                });
                await flow.Gate.WaitAsync(c.Options.Cancellation).ConfigureAwait(false);
                try
                {
                    if (provider is AccountLoginProvider.Microsoft or AccountLoginProvider.LittleSkin)
                        await StartSignIn(c, flow, "", "", "", c.Options.Cancellation).ConfigureAwait(false);
                    return ProjectSignIn(flow);
                }
                finally { flow.Gate.Release(); }
            }
            catch { lock (_planGate) _signInFlows.Remove(flow.Id); throw; }
        }),
        HostRoutes.Typed<SignInContinue, SignInChallenge>("Accounts", "ContinueSignIn", async (c, v) =>
        {
            var flow = FindSignIn(c, v.Flow); await flow.Gate.WaitAsync(c.Options.Cancellation).ConfigureAwait(false);
            try
            {
                if (flow.Cancelled) return ProjectSignIn(flow);
                if (v.Schema == Abi100Schemas.Empty) { _ = Empty.Read(v.Response); return ProjectSignIn(flow); }
                if (v.Schema != Abi100Schemas.NamedValue) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                var response = NamedValue.Read(v.Response);
                if (flow.Generation is null && response.Name == "credentials" && flow.Provider is AccountLoginProvider.Offline or AccountLoginProvider.ThirdParty or AccountLoginProvider.Nide8)
                {
                    var map = DataCodec.ReadMap(response.Value);
                    string username = DataCodec.ReadString(DataCodec.Field(map, "username"));
                    string password = DataCodec.ReadString(DataCodec.Field(map, "password"));
                    string server = DataCodec.ReadString(DataCodec.Field(map, "server"));
                    if (username.Length > 256 || password.Length > 4096 || server.Length > 2048) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                    await StartSignIn(c, flow, username, password, server, c.Options.Cancellation).ConfigureAwait(false);
                }
                else if (flow.Generation is { } generation && response.Name == "character")
                    await Command(accountCommands, AccountOnboardingRoutes.ChooseCharacter, new AccountChooseCharacterCommand(generation, DataCodec.ReadString(response.Value)), c.Options.Cancellation).ConfigureAwait(false);
                else throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                return ProjectSignIn(flow);
            }
            finally { flow.Gate.Release(); }
        }),
        HostRoutes.Typed<EntityRequest, Empty>("Accounts", "CancelSignIn", async (c, v) =>
        {
            var flow = FindSignIn(c, v.Id); await flow.Gate.WaitAsync(c.Options.Cancellation).ConfigureAwait(false);
            try
            {
                if (!flow.Cancelled && flow.Generation is { } generation && LoginState() is { IsBusy: true } current && current.Generation == generation)
                    await Command(accountCommands, AccountOnboardingRoutes.Cancel, new AccountLoginCancelCommand(generation), c.Options.Cancellation).ConfigureAwait(false);
                flow.Cancelled = true; return new Empty();
            }
            finally { flow.Gate.Release(); }
        })
    ];
}
