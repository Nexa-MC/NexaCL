using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using Nexa.Services.Settings;
using Nexa.Services.Themes;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>Coordinates admitted service facts with one render-boundary style/native/appearance generation.</summary>
internal sealed partial class DesktopThemeSession : IDesktopThemeSettingsSession, IAsyncDisposable
{
    private readonly XsrQueryRouter _queries;
    private readonly XsrCommandRouter _commands;
    private readonly XsrStateStore _state;
    private readonly XsrUiShell _shell;
    private readonly AvaloniaUiPlatformActions _platform;
    private readonly DesktopEffectiveAppearanceSource _appearance;
    private readonly Action<string> _log;
    private readonly bool _safeMode;
    private readonly XsrStateId _policyRevision;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly object _requestGate = new();
    private CancellationTokenSource _requestStop = new();
    private readonly XsrStateId _darkMode;
    internal Func<long, CancellationToken, Task>? WaitForAppearanceConsumers { get; set; }
    internal Func<string, AvaloniaUiThemeStartupSnapshot, Task>? SaveCommittedStartup { get; set; }
    private readonly SemaphoreSlim _operations = new(1);
    private readonly ConcurrentQueue<(Action Action, TaskCompletionSource Completion)> _renderActions = new();
    private readonly Channel<bool> _updates = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly TaskCompletionSource _initialReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _worker;
    private SettingsEffectiveSnapshot? _baseSettings;
    private PreparedTheme? _active, _committed;
    private DesktopThemeSettingsSnapshot _snapshot = new(0, new(0, []), new(0, null, null, [], [], false), null, false, false, null);
    private long _viewRevision, _activation, _request;
    private long _baseRevision = -1;
    private bool _disposed;
    private volatile bool _themeRecoveryPending;
    private bool? _appliedDark;
    private string? _policyChoice;
    private readonly IReadOnlySet<string> _capabilities;

    internal DesktopThemeSession(XsrQueryRouter queries, XsrCommandRouter commands, XsrStateStore state,
        XsrUiShell shell, AvaloniaUiPlatformActions platform, DesktopEffectiveAppearanceSource appearance,
        Action<string> log, bool safeMode = false)
    {
        _queries = queries; _commands = commands; _state = state; _shell = shell; _platform = platform;
        _appearance = appearance; _log = log; _safeMode = safeMode;
        _lifetimeToken = _stop.Token;
        _darkMode = state.Resolve(XsrSemanticId.Parse("UiDarkMode"));
        _capabilities = SupportedCapabilities();
        _activation = Math.Max(shell.Renderer.ThemeActivationGeneration, platform.ThemeAppearanceGeneration);
        _policyRevision = state.Resolve(SettingsPolicyContract.RevisionKey);
        state.Changed += OnState;
        platform.SystemAppearanceChanged += OnSystemAppearance;
        shell.Renderer.ThemePreparing += OnFrame;
        shell.Renderer.ThemeStyleFailed += OnThemeStyleFailed;
        _worker = RunUpdatesAsync();
        _updates.Writer.TryWrite(true);
    }

    internal Task InitialReady => _initialReady.Task;
    public DesktopThemeSettingsSnapshot Snapshot => Volatile.Read(ref _snapshot);
    internal AvaloniaUiThemeStartupSnapshot? StartupSnapshot { get; private set; }
    internal static IReadOnlySet<string> SupportedCapabilities()
    {
        var values = new HashSet<string>(AvaloniaUiThemeCapabilities.Supported, StringComparer.Ordinal)
        { "styles.basic", "tokens", "options", "conditions", "selectors", "viewport", "grid", "position", "order", "transform", "virtual-metrics", "keyframes", "media" };
        foreach (var (plural, singular) in new[] { ("images", "image"), ("fonts", "font"), ("gradients", "gradient"), ("shadows", "shadow") })
            if (values.Contains(plural)) values.Add(singular);
        return values;
    }

    private bool IsDark => _state.Read<int>(_state.Resolve(XsrSemanticId.Parse("UiDarkMode"))).Value switch
    { 0 => false, 1 => true, _ => _platform.IsSystemDark == true };
    private static string PlatformName => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";
    private void OnState(XsrStateChange change) { if (change.Id == _policyRevision || change.Id == _darkMode) _updates.Writer.TryWrite(true); }
    private void OnSystemAppearance() => _updates.Writer.TryWrite(true);
    internal void NotifyWatch(ThemeWatchSnapshot snapshot)
    {
        if (_disposed || _safeMode || snapshot.SourceIdentity != Snapshot.PresentedSourceIdentity) return;
        if (snapshot.Error is not null) { SetSnapshot(error: snapshot.Error); return; }
        if (snapshot.CandidateId is { } candidate) _ = ActivateWatchedAsync(candidate, _stop.Token);
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        while (_renderActions.TryDequeue(out var entry))
        {
            try { entry.Action(); entry.Completion.TrySetResult(); }
            catch (Exception error) { entry.Completion.TrySetException(error); }
        }
    }
    private Task OnRenderAsync(Action action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _renderActions.Enqueue((() => { token.ThrowIfCancellationRequested(); action(); }, completion));
        _platform.PostToWindow(() => { if (!_disposed && _shell.Tree.IsAlive(_shell.Root)) _shell.Tree.MarkDirty(_shell.Root, XsrUiDirtyKinds.Paint); });
        return completion.Task.WaitAsync(token);
    }

    internal async Task PrepareStartupAsync(CancellationToken token)
    {
        while (!InitialReady.IsCompleted)
        {
            token.ThrowIfCancellationRequested();
            _shell.Render(new(850, 500));
            await Task.Delay(5, token).ConfigureAwait(true);
        }
        await InitialReady.WaitAsync(token).ConfigureAwait(true);
    }

    private async Task<XsrResult<T>> QueryAsync<TQuery, T>(XsrSemanticId id, TQuery query, CancellationToken token) where TQuery : notnull where T : notnull
    {
        if (!_queries.TryResolve(id, out var route)) return XsrResult.Failure<T>(XsrRuntimeErrors.RouteNotFound());
        return await _queries.QueryAsync<TQuery, T>(route, query, cancellationToken: token).ConfigureAwait(false);
    }
    private async Task<XsrResult> CommandAsync<T>(XsrSemanticId id, T command, CancellationToken token) where T : notnull
    {
        if (!_commands.TryResolve(id, out var route)) return XsrResult.Failure(XsrRuntimeErrors.RouteNotFound());
        return await _commands.Dispatch(route, command, cancellationToken: token).Completion.ConfigureAwait(false);
    }
    private static XsrResult Failure(Exception error) => XsrResult.Failure(new(XsrErrorKind.Rejected,
        XsrSemanticId.Parse("themes.activation_failed"), error.Message));
    private void SetSnapshot(ThemeCatalogueSnapshot? catalogue = null, ThemeCurrentSnapshot? current = null,
        bool? busy = null, string? error = null)
    {
        var old = Snapshot;
        Volatile.Write(ref _snapshot, new(Interlocked.Increment(ref _viewRevision), catalogue ?? old.Catalogue,
            current ?? old.Current, _themeRecoveryPending ? "builtin" : _active?.Candidate.SourceIdentity,
            !_themeRecoveryPending && _active is not null && !ReferenceEquals(_active, _committed),
            busy ?? old.Busy, error, IsDark, PlatformName, _capabilities));
    }
    private async Task RefreshSnapshotAsync(CancellationToken token)
    {
        var catalogue = await QueryAsync<ThemeCatalogueQuery, ThemeCatalogueSnapshot>(ThemesContract.Catalogue, new(), token).ConfigureAwait(false);
        var current = await QueryAsync<ThemeCurrentQuery, ThemeCurrentSnapshot>(ThemesContract.Current, new(), token).ConfigureAwait(false);
        if (!catalogue.IsSuccess) throw new IOException(catalogue.Error!.Message);
        if (!current.IsSuccess) throw new IOException(current.Error!.Message);
        SetSnapshot(catalogue.Value!, current.Value!);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_requestGate) { if (_disposed) return; _disposed = true; }
        _state.Changed -= OnState; _platform.SystemAppearanceChanged -= OnSystemAppearance;
        _updates.Writer.TryComplete(); await _stop.CancelAsync().ConfigureAwait(false);
        try { await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        // All public operations share this gate. Drain their compensation before retiring live leases.
        await _operations.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _shell.Renderer.ThemePreparing -= OnFrame;
            _shell.Renderer.ThemeStyleFailed -= OnThemeStyleFailed;
            while (_renderActions.TryDequeue(out var pending)) pending.Completion.TrySetCanceled();
            try
            {
                if (_appearance.Current is { } current && WaitForAppearanceConsumers is { } wait)
                {
                    long generation = NextGeneration();
                    var values = current.Settings.Values.Select(value => value.Key is "appearance.logo-path" or "appearance.background-path" or "appearance.video-path" or "music.path"
                        ? value with { Value = new(SettingsOverrideMode.Custom, "") } : value).ToArray();
                    _appearance.Publish(new(generation, current.Settings with { Values = values }, [], ThemeResources: true, HasPreparedImages: true));
                    try { await wait(generation, CancellationToken.None).ConfigureAwait(false); }
                    catch (ObjectDisposedException) { _log("Theme appearance consumer was already disposed during shutdown."); }
                }
            }
            finally
            {
                var active = _active; var committed = _committed; _active = _committed = null;
                foreach (var prepared in new[] { active, committed }.OfType<PreparedTheme>().DistinctBy(value => value.Candidate.CandidateId))
                    await RetirePreparedAsync(prepared).ConfigureAwait(false);
            }
        }
        finally { _operations.Release(); _operations.Dispose(); _requestStop.Cancel(); _requestStop.Dispose(); _stop.Dispose(); }
    }

    private sealed record PreparedTheme(ThemeCandidate Candidate, NssThemeStyleProgram Program,
        AvaloniaUiThemeAppearance NativeAppearance, DesktopEffectiveAppearanceSnapshot Appearance,
        IReadOnlyList<Guid> Leases)
    {
        public bool DowngradeApproved { get; init; }
    }
}
