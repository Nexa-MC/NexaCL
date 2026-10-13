using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Nexa.Core.Media;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal static class DesktopAppearancePresentationState
{
    internal static readonly XsrSemanticId LogoVisible = XsrSemanticId.Parse("shell.appearance.logo_visible");
    internal static void Declare(XsrStateStoreBuilder builder) => builder.Cell<bool>(LogoVisible, "Nexa.Desktop.Appearance");
}

/// <summary>Bounded local appearance assets are loaded off the render thread and published immutably.</summary>
internal sealed class CustomAppearanceSession : IAsyncDisposable
{
    private readonly XsrQueryRouter _queries;
    private readonly XsrStateStore _state;
    private readonly XsrStateId _revision;
    private readonly XsrUiShell _shell;
    private readonly AvaloniaUiPlatformActions _platform;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _token;
    private readonly SemaphoreSlim _loading = new(1);
    private readonly XsrUiEntityId _logo, _background;
    private readonly NxmlDynamicContent _dynamic;
    private readonly DesktopBackgroundPresentation _backgroundPresentation;
    private XsrUiCustomPalette? _palette;
    private string _logoPath = "", _backgroundPath = "";
    private PngImage? _logoImage, _backgroundImage;
    private Task _assetLoad = Task.CompletedTask;
    private readonly Channel<bool> _policy = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task _policyWorker;
    private readonly TaskCompletionSource _initialReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task InitialReady => _initialReady.Task;
    private long _generation;
    private bool _disposed;

    internal CustomAppearanceSession(XsrQueryRouter queries, XsrStateStore state, XsrUiShell shell,
        AvaloniaUiPlatformActions platform, Action<string> log)
    {
        _queries = queries; _state = state; _shell = shell; _platform = platform; _log = log;
        _token = _stop.Token;
        _revision = state.Resolve(SettingsPolicyContract.RevisionKey);
        _background = shell.Content;
        _backgroundPresentation = DesktopBackgroundPresentation.GetOrCreate(shell.Tree, _background);
        _dynamic = new(shell.Tree, state);
        XsrUiEntityId slot = default;
        shell.Tree.Walk(shell.TitleBar, entity => { if (shell.Tree.Name(entity) == "CustomLauncherLogoSlot") slot = entity; return true; });
        _logo = _dynamic.Append(slot, DesktopMarkup.Dynamic("CustomLauncherLogo.d.nxml", new Dictionary<string, string> { ["visible"] = "false" }));
        state.Changed += OnState;
        shell.Renderer.FramePreparing += OnFrame;
        _policyWorker = RunPolicyAsync(); _policy.Writer.TryWrite(true);
    }
    internal static XsrUiCustomPalette? ParsePalette(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length > 4096) throw new InvalidDataException(DesktopBootstrapText.Raw("appearance.error.palette-length"));
        using JsonDocument document = JsonDocument.Parse(text, new() { MaxDepth = 4 });
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != 3)
            throw new InvalidDataException(DesktopBootstrapText.Raw("appearance.error.palette-shape"));
        XsrUiColor Read(string key)
        {
            if (!document.RootElement.TryGetProperty(key, out JsonElement value) || value.ValueKind != JsonValueKind.String
                || value.GetString() is not { Length: 7 } color || color[0] != '#'
                || !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
                throw new InvalidDataException(DesktopBootstrapText.Raw("appearance.error.color"));
            return new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        return new(Read("background"), Read("foreground"), Read("accent"));
    }
    private void OnState(XsrStateChange change) { if (change.Id == _revision) _policy.Writer.TryWrite(true); }
    private void ApplyPolicy(SettingsEffectiveSnapshot settings)
    {
        if (_disposed) return;
        var values = settings.Values.ToDictionary(item => item.Key, item => item.Value.Value);
        string Read(string key) => values.GetValueOrDefault(key) ?? "";
        try { _palette = ParsePalette(Read("appearance.custom-theme")); }
        catch (Exception error) when (error is InvalidDataException or JsonException)
        { _palette = null; _log(DesktopBootstrapText.Format("PaletteFailure", new Dictionary<string, string> { ["error"] = error.Message }, _shell.Renderer.LocalizeText)); }
        string logo = Read("appearance.logo-path"), background = Read("appearance.background-path");
        DesktopBackgroundAppearance appearance;
        try { appearance = DesktopBackgroundAppearance.Read(values); }
        catch (InvalidDataException error) { appearance = DesktopBackgroundAppearance.Default; _log(DesktopBootstrapText.Format("BackgroundFailure", new Dictionary<string, string> { ["error"] = error.Message }, _shell.Renderer.LocalizeText)); }
        _platform.PostToWindow(() => { if (!_disposed) _backgroundPresentation.SetAppearance(appearance); });
        long generation = Interlocked.Increment(ref _generation);
        _assetLoad = LoadAssetsAsync(logo, background, appearance, generation);
    }
    private async Task RunPolicyAsync()
    {
        try
        {
            await foreach (bool ignored in _policy.Reader.ReadAllAsync(_token).ConfigureAwait(false))
            {
                if (await CommittedSettingsRead.QueryAsync(_queries, _token).ConfigureAwait(false) is not { } settings)
                { _initialReady.TrySetException(new IOException(DesktopBootstrapText.Raw("appearance.error.initial"))); continue; }
                ApplyPolicy(settings);
                await _assetLoad.ConfigureAwait(false);
                _initialReady.TrySetResult();
            }
        }
        catch (OperationCanceledException) { _initialReady.TrySetCanceled(_token); }
        catch (Exception error) { _initialReady.TrySetException(error); throw; }
    }
    private void OnFrame(object? sender, EventArgs args)
    {
        if (!_disposed) _shell.Renderer.ColorScheme = _shell.Renderer.ColorScheme with { CustomPalette = _palette };
    }
    private async Task LoadAssetsAsync(string logo, string background, DesktopBackgroundAppearance appearance, long generation)
    {
        try
        {
            await _loading.WaitAsync(_token).ConfigureAwait(false);
            PngImage? logoImage, backgroundImage;
            try
            {
                if (generation != Volatile.Read(ref _generation)) return;
                logoImage = logo == _logoPath ? _logoImage : await LoadAsync(logo, _token).ConfigureAwait(false);
                backgroundImage = background == _backgroundPath ? _backgroundImage : await LoadAsync(background, _token).ConfigureAwait(false);
            }
            finally { _loading.Release(); }
            _platform.PostToWindow(() =>
            {
                if (_disposed || generation != Volatile.Read(ref _generation)) return;
                _logoPath = logo; _backgroundPath = background;
                _logoImage = logoImage; _backgroundImage = backgroundImage;
                _dynamic.Reload(_logo, DesktopMarkup.Dynamic("CustomLauncherLogo.d.nxml", new Dictionary<string, string> { ["visible"] = logoImage is null ? "false" : "true" }));
                _state.Publish(_state.Resolve(DesktopAppearancePresentationState.LogoVisible), logoImage is not null);
                _shell.Tree.SetComponent(_logo, logoImage is null ? null : new XsrUiRasterImage(logoImage, []) { FitToBounds = true, AspectRatio = (double)logoImage.Width / logoImage.Height });
                _backgroundPresentation.SetStatic(backgroundImage, appearance);
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        { if (!_stop.IsCancellationRequested) _log(DesktopBootstrapText.Format("ImageFailure", new Dictionary<string, string> { ["error"] = error.Message }, _shell.Renderer.LocalizeText)); }
    }
    internal static async Task<PngImage?> LoadAsync(string path, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.Length > 4096 || !Path.IsPathFullyQualified(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(DesktopBootstrapText.Raw("appearance.error.path"));
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (stream.Length is < 33 or > 16 * 1048576) throw new InvalidDataException(DesktopBootstrapText.Raw("appearance.error.size"));
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return PngImage.TryCreatePreview(bytes) ?? throw new InvalidDataException(DesktopBootstrapText.Raw("appearance.error.dimensions"));
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _state.Changed -= OnState; _shell.Renderer.FramePreparing -= OnFrame;
        _policy.Writer.TryComplete();
        await _stop.CancelAsync().ConfigureAwait(false);
        await _policyWorker.ConfigureAwait(false);
        await _assetLoad.ConfigureAwait(false);
        _shell.Renderer.ColorScheme = _shell.Renderer.ColorScheme with { CustomPalette = null };
        _dynamic.Remove(_logo); _state.Publish(_state.Resolve(DesktopAppearancePresentationState.LogoVisible), false); _backgroundPresentation.ResetStatic();
        _stop.Dispose();
    }
}
