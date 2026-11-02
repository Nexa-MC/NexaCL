using System.Runtime.InteropServices;
using System.Text;
using Nexa.Native.Accounts.Wire;
using Nexa.Native.Host.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Native.Ui;
using Nexa.Services;
using Nexa.Services.Accounts;
using Nexa.Services.Logging;
using Nexa.Services.Minecraft.Launch;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Accounts.Client;

/// <summary>A startup descriptor. Profile persistence is performed exclusively by the account image.</summary>
public interface INativeAccountProfileStore
{
    string? NativeProfilePath { get; }
    LaunchProfileSet NativeInitialProfiles { get; }
}

public sealed class NativeProfileStore(string path) : ILaunchProfilePort, INativeAccountProfileStore
{
    public string NativeProfilePath { get; } = Path.GetFullPath(path);
    public LaunchProfileSet NativeInitialProfiles => new();
    public LaunchProfileSet Load() => throw new NotSupportedException("Native account storage belongs to nexa-accounts.");
    public void Save(LaunchProfileSet profiles) => throw new NotSupportedException("Native account storage belongs to nexa-accounts.");
}

/// <summary>Local typed state and asynchronous numeric ports for the independently compiled account owner.</summary>
public sealed partial class NativeAccountService : IAccountLaunchAccess, IAsyncDisposable
{
    private ServiceEndpoint? _endpoint;
    private ServiceEndpoint Endpoint => _endpoint ?? throw new InvalidOperationException("Account transport is not attached.");
    private readonly LogService? _log;
    private readonly INativeAccountProfileStore _profiles;
    private nint _close;
    private NativeHostServiceAdmission? _hostAdmission;
    private long _context;
    private HttpClient? _http;
    private readonly object _retirementGate = new();
    private Task? _retirement;
    private Task? _initialization;
    private RegionalPolicy _policy = RegionalPolicy.Current;

    public NativeAccountService(XsrStateStore store, ILaunchProfilePort profiles, LogService? log = null)
    {
        StateStore = store; _log = log;
        _profiles = profiles as INativeAccountProfileStore ?? throw new ArgumentException("Native composition requires an explicit profile-store descriptor.", nameof(profiles));
    }

    public XsrStateStore StateStore { get; }
    public Task Initialization => _initialization ?? throw new InvalidOperationException("Account network policy has not been attached.");
    public RegionalPolicy RegionPolicy => _policy;
    public int SelectedIndex
    {
        get { var value = StateStore.Read<int>(StateStore.Resolve(AccountStateContract.SelectedKey)); return value.HasValue ? value.Value : -1; }
    }
    public IReadOnlyList<LaunchProfileView> GetViews() => StateStore.ReadCollection<LaunchProfileView>(StateStore.Resolve(AccountStateContract.ProfilesKey)).Items;
    public void ConfigureRegionalPolicy(RegionalPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (_initialization is not null && policy != _policy) throw new InvalidOperationException("Country policy is fixed for a native account session.");
        _policy = policy;
    }
    public static void DeclareReplicas(XsrStateStoreBuilder builder) => AccountProtocol.DeclareReplicas(builder);

    public void AttachHttp(HttpClient http)
    {
        lock (_retirementGate)
        {
            ObjectDisposedException.ThrowIf(_retirement is not null, this);
            ArgumentNullException.ThrowIfNull(http);
            if (_http is not null) throw new InvalidOperationException("Account network policy is already attached.");
            _http = http;
            _endpoint = new((bytes, token) => ServiceHttp.ExecuteAsync(_http ?? throw new InvalidOperationException("Network policy is unavailable."), bytes, token), Apply, Report);
            try { (_context, _close) = Open(_endpoint); }
            catch
            {
                _ = _endpoint.DisposeAsync().AsTask(); _endpoint = null; http.Dispose(); throw;
            }
            _initialization = InitializeAsync();
        }
    }
    private async Task InitializeAsync()
    {
        using var writer = AccountProtocol.Request(0);
        writer.String(_profiles.NativeProfilePath); writer.LaunchProfileSet(_profiles.NativeInitialProfiles);
        writer.String(_policy.CountryCode); writer.String(AppContext.BaseDirectory); writer.Int32((int)(_log?.MaximumLevel ?? LogLevel.Info));
        var response = await Endpoint.InvokeAsync(writer.Finish()).ConfigureAwait(false);
        using var reader = new ServiceWireReader(response); reader.End();
    }
    public async Task ConfigureRuntimeAsync(AccountOnboardingOptions options, string? dataDirectory)
    {
        await Initialization.ConfigureAwait(false);
        using var writer = AccountProtocol.Request(1); writer.AccountOnboardingOptions(options); writer.String(dataDirectory);
        var response = await Endpoint.InvokeAsync(writer.Finish()).ConfigureAwait(false);
        using var reader = new ServiceWireReader(response); reader.End();
    }
    private async ValueTask<XsrResult> CommandAsync(int operation, Action<ServiceWireWriter> write, CancellationToken token = default)
    {
        await Initialization.WaitAsync(token).ConfigureAwait(false);
        using var writer = AccountProtocol.Request(operation); write(writer);
        var bytes = await Endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    private async ValueTask<XsrResult<T>> QueryAsync<T>(int operation, Action<ServiceWireWriter> write,
        Func<ServiceWireReader, T> read, CancellationToken token)
    {
        await Initialization.WaitAsync(token).ConfigureAwait(false);
        using var writer = AccountProtocol.Request(operation); write(writer);
        var bytes = await Endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        XsrResult<T> result = error is null ? XsrResult.Success(read(reader)) : XsrResult.Failure<T>(error);
        reader.End(); return result;
    }
    public ValueTask<XsrResult<LaunchProfile>> GetProfileAsync(int index, CancellationToken cancellationToken = default) =>
        QueryAsync(200, writer => writer.Int32(index), reader => reader.LaunchProfile() ?? throw new InvalidDataException("Missing launch profile."), cancellationToken);
    public XsrResult<LaunchProfile> GetProfile(int index) => throw new NotSupportedException("Use asynchronous privileged account access.");
    public ValueTask<XsrResult> UpsertAsync(Nexa.Services.Foundation.AccountUpsertProfileCommand command, CancellationToken token = default) =>
        CommandAsync(20, writer => writer.AccountUpsertProfileCommand(command), token);
    public ValueTask<XsrResult> SelectAsync(Nexa.Services.Foundation.AccountSelectProfileCommand command, CancellationToken token = default) =>
        CommandAsync(21, writer => writer.AccountSelectProfileCommand(command), token);
    public ValueTask<XsrResult> RemoveAsync(Nexa.Services.Foundation.AccountRemoveProfileCommand command, CancellationToken token = default) =>
        CommandAsync(22, writer => writer.AccountRemoveProfileCommand(command), token);
    public ValueTask<XsrResult<MinecraftLaunchIdentity>> ResolveAsync(int accountIndex, LaunchProfile profile, CancellationToken cancellationToken = default) =>
        QueryAsync(201, writer => { writer.Int32(accountIndex); writer.LaunchProfile(profile); },
            reader => reader.MinecraftLaunchIdentity() ?? throw new InvalidDataException("Missing launch identity."), cancellationToken);

    private byte[] Apply(ReadOnlyMemory<byte> bytes)
    {
        using var reader = AccountProtocol.Read(bytes, out int operation);
        if (operation == 7)
        {
            int level = reader.Int32(); string module = reader.String() ?? "Accounts"; string message = reader.String() ?? ""; string? exception = reader.String(); reader.End();
            if (level is < 0 or > 4) throw new InvalidDataException("Invalid native log level.");
            _log?.Write((LogLevel)level, module, message, exception); return [];
        }
        if (operation is < 0 or > 5) throw new InvalidDataException("Unknown account state slot.");
        long revision = reader.Int64(); var availability = (XsrStateAvailability)reader.Int32();
        var id = StateStore.Resolve(AccountProtocol.Keys[operation]);
        // Fully decode and validate the frame before making any publication visible.
        switch (operation)
        {
            case 0: var profiles = Items(reader, reader.LaunchProfileView); reader.End(); StateStore.ReplicateCollection<LaunchProfileView, int>(id, revision, availability, profiles); break;
            case 1: bool hasSelected = reader.Boolean(); int selected = reader.Int32(); reader.End(); StateStore.Replicate(id, revision, availability, hasSelected, selected); break;
            case 2:
                bool hasLogin = reader.Boolean(); var login = reader.AccountLoginSnapshot(); reader.End();
                if (hasLogin && login is null) throw new InvalidDataException("Missing login state.");
                StateStore.Replicate(id, revision, availability, hasLogin, login!); break;
            case 3: var choices = Items(reader, () => reader.AccountCharacterChoice() ?? throw new InvalidDataException("Missing character.")); reader.End(); StateStore.ReplicateCollection<AccountCharacterChoice, string>(id, revision, availability, choices); break;
            case 4: var imports = Items(reader, () => reader.AccountImportCandidate() ?? throw new InvalidDataException("Missing import.")); reader.End(); StateStore.ReplicateCollection<AccountImportCandidate, string>(id, revision, availability, imports); break;
            case 5: var skins = Items(reader, () => reader.AccountSkinSnapshot() ?? throw new InvalidDataException("Missing skin.")); reader.End(); StateStore.ReplicateCollection<AccountSkinSnapshot, string>(id, revision, availability, skins); break;
        }
        return [];
    }
    private static T[] Items<T>(ServiceWireReader reader, Func<T> read)
    {
        int count = reader.Count(); if (count < 0) throw new InvalidDataException("Missing account state collection.");
        T[] items = new T[count]; for (int index = 0; index < count; index++) items[index] = read(); return items;
    }
    private void Report(Exception error) => _log?.Error("Accounts", "Native account operation failed.", error.GetType().Name);

    private unsafe (long Context, nint Close) Open(ServiceEndpoint endpoint)
    {
        if (NativeHostServiceAdmission.Required)
        {
            _hostAdmission = NativeHostServiceAdmission.Open(1, AccountProtocol.Identity, endpoint.Table, () => DisposeAsync().AsTask());
            endpoint.Connect(_hostAdmission.Owner);
            return (_hostAdmission.Owner.Context, 0);
        }
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        nint image = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nexa-accounts" + suffix));
        var identity = (delegate* unmanaged[Cdecl]<byte*, int, int>)NativeLibrary.GetExport(image, "nexa_accounts_identity_v1");
        var open = (delegate* unmanaged[Cdecl]<NativeUiCallTable*, NativeUiCallTable*, int>)NativeLibrary.GetExport(image, "nexa_accounts_open_v1");
        nint close = NativeLibrary.GetExport(image, "nexa_accounts_close_v1");
        byte[] expected = Encoding.UTF8.GetBytes(AccountProtocol.Identity), actual = new byte[expected.Length];
        if (identity(null, 0) != actual.Length) throw new InvalidDataException("Incompatible account module identity.");
        fixed (byte* output = actual)
            if (identity(output, actual.Length) != actual.Length || !actual.AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Incompatible account module identity.");
        var host = endpoint.Table; NativeUiCallTable peer = default;
        if (open(&host, &peer) != 0) throw new InvalidOperationException("Account module admission failed.");
        endpoint.Connect(peer); return (peer.Context, close);
    }
    private unsafe int Close() => _hostAdmission is { } admission ? admission.CloseOwner() : ((delegate* unmanaged[Cdecl]<long, int>)_close)(_context);
    public ValueTask DisposeAsync()
    { lock (_retirementGate) return new(_retirement ??= RetireAsync()); }
    private async Task RetireAsync()
    {
        if (_endpoint is null) { _http?.Dispose(); return; }
        int delay = 1;
        while (true)
        {
            int status = Close(); if (status == 1) break;
            if (status != 0) throw new IOException("Native account shutdown failed; callbacks remain retained.");
            await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32);
        }
        await _endpoint.DisposeAsync().ConfigureAwait(false); _http?.Dispose();
        _hostAdmission?.Complete();
    }
}
