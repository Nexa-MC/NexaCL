using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window;

internal sealed partial class WindowSession
{
    private void AttachPlatformEvents()
    {
        _actions.CloseRequested = () => Decide(WindowPlatformEvent.CloseRequested);
        _actions.CommandPaletteOpenRequested = () => Decide(WindowPlatformEvent.PaletteOpen);
        _actions.CommandPaletteCloseRequested = () => Decide(WindowPlatformEvent.PaletteClose);
        _actions.OpenSettingsRequested = OnSettings;
        _actions.LocalizeDesktopText = Localize;
        _actions.LocalizeContentPicker = text => LocalizeValue(text, WindowControlCallback.LocalizePicker);
        _actions.InputObserved += OnInput;
        _actions.FilesDropped += OnFiles;
        _actions.MemoryPressureObserved += OnMemory;
        _actions.MediaControlRequested += OnMedia;
        _actions.ProtocolActivated += OnProtocol;
        _actions.SystemAppearanceChanged += OnAppearance;
        _actions.WindowActivated += OnActivated;
        _actions.WindowActivityChanged += OnActivity;
        _actions.WindowClosed += OnClosed;
    }

    private void DetachPlatformEvents()
    {
        _actions.CloseRequested = null; _actions.CommandPaletteOpenRequested = null;
        _actions.CommandPaletteCloseRequested = null; _actions.OpenSettingsRequested = null;
        _actions.LocalizeDesktopText = null; _actions.LocalizeContentPicker = null;
        _actions.InputObserved -= OnInput; _actions.FilesDropped -= OnFiles;
        _actions.MemoryPressureObserved -= OnMemory; _actions.MediaControlRequested -= OnMedia;
        _actions.ProtocolActivated -= OnProtocol; _actions.SystemAppearanceChanged -= OnAppearance;
        _actions.WindowActivated -= OnActivated; _actions.WindowActivityChanged -= OnActivity;
        _actions.WindowClosed -= OnClosed;
    }

    private byte[] PlatformEvent(WindowPlatformEvent kind, Action<UiWireWriter>? payload = null, bool worker = false)
    {
        using var writer = WindowControlProtocol.Write((int)WindowControlCallback.PlatformEvent, callback: true);
        writer.Int32((int)kind); payload?.Invoke(writer);
        return worker ? _hostControl.Exchange(writer.Finish())
            : _hostUi.Exchange(WindowControlProtocol.Prefix((byte)WindowUiCallback.PlatformEvent, writer.Finish()));
    }
    private bool Decide(WindowPlatformEvent kind)
    {
        try
        {
            byte[] result = PlatformEvent(kind);
            if (result.Length != 1 || result[0] > 1) throw new InvalidDataException("Invalid native platform decision.");
            return result[0] == 1;
        }
        catch (Exception error) { Report(error); return false; }
    }
    private void Notify(WindowPlatformEvent kind, Action<UiWireWriter>? payload = null, bool worker = false)
    {
        try
        {
            if (PlatformEvent(kind, payload, worker).Length != 0) throw new InvalidDataException("Invalid native platform acknowledgement.");
        }
        catch (Exception error) { Report(error); }
    }
    private void OnSettings() => Notify(WindowPlatformEvent.Settings);
    private void OnInput(AvaloniaUiInputKind kind) => Notify(WindowPlatformEvent.Input, writer => writer.AvaloniaUiInputKind(kind));
    private void OnFiles(IReadOnlyList<string> files) => Notify(WindowPlatformEvent.Files, writer =>
    { writer.Count(files.Count); foreach (string file in files) writer.String(file); });
    private void OnMemory(AvaloniaUiMemoryObservation observation) => Notify(WindowPlatformEvent.Memory,
        writer => writer.AvaloniaUiMemoryObservation(observation), worker: true);
    private void OnMedia(string command) => Notify(WindowPlatformEvent.Media, writer => writer.String(command));
    private void OnProtocol(string uri) => Notify(WindowPlatformEvent.Protocol, writer => writer.String(uri));
    private void OnAppearance() => Notify(WindowPlatformEvent.Appearance);
    private void OnActivated() => Notify(WindowPlatformEvent.Activated);
    private void OnActivity(bool active) => Notify(WindowPlatformEvent.Activity, writer => writer.Boolean(active));
    private void OnClosed() => Notify(WindowPlatformEvent.Closed);
}
