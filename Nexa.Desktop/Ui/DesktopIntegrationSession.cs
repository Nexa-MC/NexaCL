using Nexa.Services.Tasks;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal sealed class DesktopIntegrationSession : IAsyncDisposable
{
    private static readonly XsrUiContextMenuItem[] DesktopMenuItems =
    [
        new(ProductPresentationText.Raw("native.desktop.launch"), XsrSemanticId.Parse("ui.navigation.launch")),
        new(ProductPresentationText.Raw("native.desktop.install"), XsrSemanticId.Parse("ui.navigation.download")),
        new(ProductPresentationText.Raw("native.desktop.resources"), XsrSemanticId.Parse("ui.navigation.community")),
        new(ProductPresentationText.Raw("native.desktop.settings"), XsrSemanticId.Parse("ui.navigation.settings")),
        new(ProductPresentationText.Raw("native.desktop.minimize"), XsrSemanticId.Parse("ui.desktop.minimize")),
        new(ProductPresentationText.Raw("native.desktop.exit"), XsrSemanticId.Parse("ui.desktop.exit")),
    ];
    private readonly XsrUiContextMenuItem[] _menuItems = OperatingSystem.IsLinux()
        ? [.. DesktopMenuItems, new(ProductPresentationText.Raw("native.desktop.steam"), XsrSemanticId.Parse("ui.desktop.steam")), new(ProductPresentationText.Raw("native.desktop.gamescope"), XsrSemanticId.Parse("ui.desktop.gamescope"))]
        : DesktopMenuItems;
    private Task? _steamRegistration;
    private readonly XsrStateStore _state;
    private readonly AvaloniaUiPlatformActions _platform;
    private readonly DesktopSingleInstance? _instance;
    private readonly Action<DesktopDestination> _activate;
    private readonly Action<string> _report;
    private readonly Action<string> _notify;
    private readonly DesktopUiIntentSink _intents;
    private readonly XsrUiShell _shell;
    private readonly Action<string>? _openFile;
    private readonly Queue<string> _pendingFiles = new(16);
    private readonly HashSet<XsrStateId> _keys = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _registration;
    private readonly IDisposable? _nativeFiles;
    private bool _disposed;
    private TaskCenterSummary? _taskbarSummary;
    private long _taskbarRefresh;
    internal bool HasExplicitActivation { get; private set; }
    internal Task InitialReady => _registration;

    internal DesktopIntegrationSession(XsrUiShell shell, DesktopUiIntentSink intents, XsrStateStore state,
        AvaloniaUiPlatformActions platform, DesktopSingleInstance? instance, string[] args, Action<string> report,
        Action<string>? openFile = null, bool registerProtocol = true, Action<string>? notify = null)
    {
        _state = state; _platform = platform; _instance = instance; _report = report; _intents = intents; _shell = shell;
        _openFile = openFile; _notify = notify ?? report;
        shell.Renderer.FramePreparing += OnFrame;
        var existingMenu = shell.Tree.GetComponent<XsrUiContextMenu>(shell.Root)?.Items ?? [];
        shell.Tree.SetComponent(shell.Root, new XsrUiContextMenu(_menuItems.Concat(existingMenu)
            .DistinctBy(item => item.Command)));
        intents.IntentEmitted += OnIntent;
        _activate = destination =>
        {
            platform.RestoreWindow();
            if (destination == DesktopDestination.Activate) return;
            HasExplicitActivation = true;
            string command = destination switch
            {
                DesktopDestination.Install => "ui.navigation.download",
                DesktopDestination.Resources => "ui.navigation.community",
                DesktopDestination.Settings or DesktopDestination.Java or DesktopDestination.Storage or DesktopDestination.About => "ui.navigation.settings",
                DesktopDestination.Tasks => "ui.tasks.open",
                _ => "ui.navigation.launch",
            };
            intents.Emit(XsrSemanticId.Parse(command), default, XsrCorrelationId.Create());
            if (destination is DesktopDestination.Java or DesktopDestination.Storage or DesktopDestination.About)
            {
                string page = destination.ToString().ToLowerInvariant();
                shell.Tree.Walk(shell.Root, section =>
                {
                    if (shell.Tree.Name(section) == "SettingsNav." + page)
                        intents.Emit(XsrSemanticId.Parse("ui.settings.section"), section, XsrCorrelationId.Create());
                    return true;
                });
            }
        };
        platform.OpenSettingsRequested = () => _activate(DesktopDestination.Settings);
        platform.LocalizeDesktopText = shell.Renderer.LocalizeText;
        platform.ProtocolActivated += OnProtocol;
        foreach (string key in new[] { "UiTrayEnabled", "UiCloseToTray", "UiMinimizeToTray" })
            if (state.TryResolve(XsrSemanticId.Parse(key), out XsrStateId id)) _keys.Add(id);
        state.Changed += OnChanged;
        ApplyPolicy();
        if (instance is not null) { platform.WindowClosed += instance.BeginShutdown; instance.Wake = Wake; Wake(); }
        else
        {
            if (args.FirstOrDefault(value => value.StartsWith("nexacl:", StringComparison.OrdinalIgnoreCase)) is { } uri) OnProtocol(uri);
            foreach (string argument in args)
                if (DesktopActivation.TryFile(argument, out string file)) ScheduleFile(file);
        }
        if (OperatingSystem.IsMacOS()) _nativeFiles = DesktopNativeFileActivation.Subscribe(ScheduleFile, report);
        _registration = registerProtocol ? Task.Run(async () =>
        {
            string? error = await DesktopProtocolRegistration.RegisterAsync(_stop.Token).ConfigureAwait(false);
            if (!_stop.IsCancellationRequested && error is not null) report(error);
        }) : Task.CompletedTask;
    }

    private void OnProtocol(string uri)
    {
        if (DesktopActivation.TryParse(uri, out DesktopDestination destination))
            _platform.PostToWindow(() => { if (!_disposed) _activate(destination); });
        else _report(ProductPresentationText.Raw("native.desktop.unsupported-protocol"));
    }
    internal void ActivateProtocol(string uri) => OnProtocol(uri);
    private void OnIntent(object? sender, DesktopUiIntentEventArgs args)
    {
        if (_disposed) return;
        if (args.Intent.Command.Value.StartsWith("ui.navigation.", StringComparison.Ordinal)
            || args.Intent.Command.Value == "ui.tasks.open") HasExplicitActivation = true;
        if (args.Intent.Source != _shell.Root) return;
        if (args.Intent.Command.Value == "ui.desktop.minimize") _platform.MinimizeWindow();
        else if (args.Intent.Command.Value == "ui.desktop.exit") _platform.RequestClose();
        else if (OperatingSystem.IsLinux() && args.Intent.Command.Value is "ui.desktop.steam" or "ui.desktop.gamescope"
            && _steamRegistration is not { IsCompleted: false })
            _steamRegistration = RegisterSteamAsync(args.Intent.Command.Value == "ui.desktop.gamescope");
    }
    private async Task RegisterSteamAsync(bool gamescope)
    {
        try
        {
            string? selected = await _platform.PickDirectoryAsync("Steam userdata/<id>/config").WaitAsync(_stop.Token).ConfigureAwait(false);
            if (selected is null || _disposed) return;
            string executable = Environment.ProcessPath ?? throw new IOException("无法定位启动器。");
            if (Path.GetFileNameWithoutExtension(executable) == "dotnet") throw new IOException("请使用已发布的 NexaCL 注册 Steam。");
            await Task.Run(() => Nexa.Platform.PlatformSteamShortcut.RegisterAsync(selected, executable, gamescope, _stop.Token), _stop.Token).ConfigureAwait(false);
            if (!_disposed) _notify(gamescope ? "Steam: NexaCL (Gamescope)" : "Steam: NexaCL");
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { if (!_disposed) _report(error.Message); }
    }

    private void Wake() => _platform.PostToWindow(() =>
    {
        if (_disposed || _instance is null) return;
        while (_instance.TryTakeActivation(out DesktopInstanceActivation activation))
        {
            _activate(activation.Destination);
            if (activation.File is { } file) ScheduleFile(file);
        }
    });
    private void ScheduleFile(string file)
    {
        if (_disposed) return;
        HasExplicitActivation = true;
        if (_pendingFiles.Count >= 16) { _report(ProductPresentationText.Raw("native.drop.pending-limit")); return; }
        _pendingFiles.Enqueue(file);
        _shell.Tree.MarkDirty(_shell.Root, XsrUiDirtyKinds.Paint);
    }
    private void OnFrame(object? sender, EventArgs args)
    {
        if (_disposed) return;
        if (OperatingSystem.IsWindows() && _state.TryResolve(TaskCenterStateContract.SummaryKey, out var summaryId))
        {
            var summary = _state.Read<TaskCenterSummary>(summaryId).Value;
            long now = Environment.TickCount64;
            if (summary != _taskbarSummary || now >= _taskbarRefresh)
            {
                _platform.SetTaskbarProgress(summary?.ActiveCount > 0, summary?.Progress ?? 0);
                _taskbarSummary = summary;
                _taskbarRefresh = now + 5000;
            }
        }
        if (_pendingFiles.Count == 0) return;
        _platform.RestoreWindow();
        if (_openFile is null) { _report(ProductPresentationText.Raw("native.desktop.no-file-receiver")); _pendingFiles.Clear(); return; }
        while (_pendingFiles.TryDequeue(out string? file)) _openFile(file);
    }
    private void OnChanged(XsrStateChange change) { if (_keys.Contains(change.Id)) ApplyPolicy(); }
    private bool Read(string key, bool fallback) => _state.TryResolve(XsrSemanticId.Parse(key), out XsrStateId id)
        ? _state.Read<bool>(id).Value : fallback;
    private void ApplyPolicy() => _platform.SetDesktopPolicy(new(Read("UiTrayEnabled", true), Read("UiCloseToTray", false), Read("UiMinimizeToTray", false)));

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _nativeFiles?.Dispose();
        _platform.SetTaskbarProgress(false, 0);
        _state.Changed -= OnChanged;
        _intents.IntentEmitted -= OnIntent;
        _shell.Renderer.FramePreparing -= OnFrame;
        _pendingFiles.Clear();
        var remainingMenu = _shell.Tree.GetComponent<XsrUiContextMenu>(_shell.Root)?.Items
            .Where(item => !_menuItems.Any(owned => owned.Command == item.Command)).ToArray() ?? [];
        _shell.Tree.SetComponent<XsrUiContextMenu>(_shell.Root,
            remainingMenu.Length == 0 ? null : new XsrUiContextMenu(remainingMenu));
        _platform.ProtocolActivated -= OnProtocol;
        _platform.OpenSettingsRequested = null;
        _platform.LocalizeDesktopText = null;
        if (_instance is not null) { _platform.WindowClosed -= _instance.BeginShutdown; _instance.Wake = null; }
        await _stop.CancelAsync().ConfigureAwait(false);
        await _registration.ConfigureAwait(false);
        if (_steamRegistration is { } steam) await steam.ConfigureAwait(false);
        _stop.Dispose();
    }
}
