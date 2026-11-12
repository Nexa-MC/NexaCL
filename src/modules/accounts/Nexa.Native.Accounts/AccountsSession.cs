using Nexa.Native.Accounts.Wire;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
using Nexa.Services;
using Nexa.Services.Accounts;
using Nexa.Services.Logging;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Accounts;

internal sealed partial class AccountsSession : IAsyncDisposable
{
    private readonly ServiceEndpoint _endpoint;
    private readonly ServiceStatePublisher _publications;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private readonly XsrStateStore _store;
    private readonly LogService _log;
    private AccountService? _accounts;
    private AccountOnboardingService? _onboarding;
    private AccountSkinService? _skins;
    private AccountWardrobeService? _wardrobe;
    private WardrobeCatalogService? _catalog;
    private AccountLaunchIdentityResolver? _resolver;
    private readonly HttpClient _http;
    private bool _initialized;

    internal AccountsSession(NativeUiCallTable peer)
    {
        _endpoint = new(ExecuteAsync, _ => throw new InvalidDataException("Unexpected account publication."), Report);
        _endpoint.Connect(peer); _publications = new(_endpoint);
        XsrStateStoreBuilder builder = new();
        AccountStateContract.DeclareState(builder); AccountOnboardingState.DeclareState(builder);
        AccountSkinContract.DeclareState(builder); LogService.DeclareState(builder);
        _store = builder.Build(); _log = new(_store); _log.AddSink(new MirrorLog(_endpoint));
        _store.Changed += Publish;
        _http = new(new ServiceHttp.Handler(_endpoint)) { Timeout = TimeSpan.FromSeconds(30) };
    }
    internal NativeUiCallTable Table => _endpoint.Table;
    internal void Report(Exception error)
    {
        try { _log.Error("Account", "Native account operation failed.", ExceptionDiagnostics.Describe(error)); }
        catch { Console.Error.WriteLine("Account module operation failed: " + error.GetType().Name); }
    }

    private async Task<byte[]> ExecuteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = AccountProtocol.Read(bytes, out int operation);
        using var writer = new ServiceWireWriter();
        if (operation == 0)
        {
            string? path = reader.String(); var profiles = reader.LaunchProfileSet() ?? throw new InvalidDataException("Missing profile store.");
            string country = reader.String() ?? ""; string directory = reader.String() ?? ""; int level = reader.Int32(); reader.End();
            if (!Path.IsPathFullyQualified(directory)) throw new InvalidDataException("Account module base directory must be absolute.");
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", directory);
            if (level is < 0 or > 4) throw new InvalidDataException("Invalid log level.");
            await _initialization.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_initialized) throw new InvalidOperationException("Account store is already initialized.");
                if (path is not null && !Path.IsPathFullyQualified(path)) throw new InvalidDataException("Profile path must be absolute.");
                _log.MaximumLevel = (LogLevel)level;
                _accounts = new(_store, path is null ? new MemoryProfiles(profiles) : new ProtectedLaunchProfilePort(path), _log, new(country));
                await _accounts.Initialization.WaitAsync(token).ConfigureAwait(false);
                _initialized = true;
            }
            finally { _initialization.Release(); }
            await _publications.FlushAsync(token).ConfigureAwait(false);
            return [];
        }
        if (!_initialized) throw new InvalidOperationException("Account store has not initialized.");
        if (operation == 1)
        {
            var options = reader.AccountOnboardingOptions() ?? throw new InvalidDataException("Missing account options.");
            string? dataDirectory = reader.String(); reader.End();
            await _initialization.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_onboarding is not null) throw new InvalidOperationException("Account runtime is already configured.");
                IMicrosoftMinecraftAuthService microsoft = new MicrosoftMinecraftAuthService(_http, log: _log);
                ILittleSkinOAuthService littleSkin = new LittleSkinOAuthService(_http, _log);
                YggdrasilAuthService yggdrasil = new(_http, _log);
                _onboarding = new(_accounts!, microsoft, littleSkin, yggdrasil, options, log: _log);
                _skins = new(_accounts!, _http, _log); _catalog = new(_http);
                _wardrobe = new(_accounts!, _http, _skins, new WardrobeHistoryStore(dataDirectory), null, microsoft,
                    options.MicrosoftClientId, littleSkin, options.LittleSkin, _log, _catalog);
                _resolver = new(_accounts!, microsoft, options.MicrosoftClientId, _log, littleSkin, options.LittleSkin, yggdrasil);
            }
            finally { _initialization.Release(); }
            await _publications.FlushAsync(token).ConfigureAwait(false);
            return [];
        }
        if (operation == 200)
        {
            int index = reader.Int32(); reader.End(); var result = _accounts!.GetProfile(index);
            writer.XsrError(result.Error); if (result.IsSuccess) writer.LaunchProfile(result.Value);
        }
        else if (operation == 202)
        {
            int index = reader.Int32(); long revision = reader.Int64(); reader.End();
            var result = await _accounts!.GetProfileAsync(index, revision, token).ConfigureAwait(false);
            writer.XsrError(result.Error); if (result.IsSuccess) writer.LaunchProfile(result.Value);
        }
        else if (operation == 201)
        {
            int index = reader.Int32(); var profile = reader.LaunchProfile() ?? throw new InvalidDataException("Missing profile."); reader.End();
            var result = await (_resolver ?? throw new InvalidOperationException("Account runtime is unavailable."))
                .ResolveAsync(index, profile, token).ConfigureAwait(false);
            writer.XsrError(result.Error); if (result.IsSuccess) writer.MinecraftLaunchIdentity(result.Value);
        }
        else await DispatchAsync(operation, reader, writer, token).ConfigureAwait(false);
        await _publications.FlushAsync(token).ConfigureAwait(false);
        return writer.Finish();
    }

    private static XsrResult AsResult(XsrError? error) => error is null ? XsrResult.Success() : XsrResult.Failure(error);
    private static XsrResult AsResult(XsrResult<int> result) => result.IsSuccess ? XsrResult.Success() : XsrResult.Failure(result.Error!);

    private void Publish(XsrStateChange change)
    {
        if (!AccountProtocol.Keys.Contains(change.SemanticId)) return;
        int slot = Array.IndexOf(AccountProtocol.Keys, change.SemanticId);
        var entry = _store.CaptureSnapshot().Entries.First(value => value.SemanticId == change.SemanticId);
        _publications.Post(slot, entry.Revision, () =>
        {
            // Capture under the source store gates. Concurrent notification delivery can reorder,
            // so the receiving replica also rejects older revisions.
            using var writer = AccountProtocol.Request(Array.IndexOf(AccountProtocol.Keys, change.SemanticId));
            writer.Int64(entry.Revision); writer.Int32((int)entry.Availability);
            switch (Array.IndexOf(AccountProtocol.Keys, change.SemanticId))
            {
                case 0: WriteItems(writer, (IReadOnlyList<LaunchProfileView>)entry.Value!, writer.LaunchProfileView); break;
                case 1: writer.Boolean(entry.Value is not null); writer.Int32(entry.Value is int selected ? selected : 0); break;
                case 2: writer.Boolean(entry.Value is not null); writer.AccountLoginSnapshot((AccountLoginSnapshot?)entry.Value); break;
                case 3: WriteItems(writer, (IReadOnlyList<AccountCharacterChoice>)entry.Value!, writer.AccountCharacterChoice); break;
                case 4: WriteItems(writer, (IReadOnlyList<AccountImportCandidate>)entry.Value!, writer.AccountImportCandidate); break;
                case 5: WriteItems(writer, (IReadOnlyList<AccountSkinSnapshot>)entry.Value!, writer.AccountSkinSnapshot); break;
            }
            return writer.Finish();
        });
    }
    private static void WriteItems<T>(ServiceWireWriter writer, IReadOnlyList<T> items, Action<T> write)
    { writer.Count(items.Count); foreach (var item in items) write(item); }
    private sealed class MemoryProfiles(LaunchProfileSet profiles) : ILaunchProfilePort
    { private LaunchProfileSet _profiles = profiles; public LaunchProfileSet Load() => _profiles; public void Save(LaunchProfileSet profiles) => _profiles = profiles; }
    private sealed class MirrorLog(ServiceEndpoint endpoint) : ILogSink
    {
        public void Write(LogEntry entry, string formattedLine)
        {
            using var writer = AccountProtocol.Request(7); writer.Int32((int)entry.Level); writer.String(entry.Module); writer.String(entry.Message); writer.String(entry.ExceptionText);
            endpoint.Publish(writer.Finish());
        }
    }
    public async ValueTask DisposeAsync()
    {
        ValueTask stopped = _endpoint.StopAsync();
        _onboarding?.Dispose(); _skins?.Dispose();
        if (_onboarding is not null) await _onboarding.WhenDrained.ConfigureAwait(false);
        if (_skins is not null) await _skins.WhenDrained.ConfigureAwait(false);
        await stopped.ConfigureAwait(false);
        // Initialization can allocate its service while shutdown is canceling its request.
        _onboarding?.Dispose(); _skins?.Dispose();
        if (_onboarding is not null) await _onboarding.WhenDrained.ConfigureAwait(false);
        if (_skins is not null) await _skins.WhenDrained.ConfigureAwait(false);
        if (_accounts is not null) await _accounts.DisposeAsync().ConfigureAwait(false);
        _store.Changed -= Publish;
        await _publications.DisposeAsync().ConfigureAwait(false);
        _http.Dispose(); _log.Dispose();
        await _endpoint.DisposeAsync().ConfigureAwait(false); _initialization.Dispose();
    }
}
