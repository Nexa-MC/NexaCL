using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Wire;

namespace Nexa.Xsr.Renderer.Backend.Native;

public sealed partial class NativeWindowSession
{
    private int _invalidations, _invalidationQueued;
    public Func<bool>? CloseRequested { get; set; }
    public Func<bool>? CommandPaletteOpenRequested { get; set; }
    public Func<bool>? CommandPaletteCloseRequested { get; set; }
    public event Action? SettingsRequested;
    public event Action? WindowClosed;
    public event Action? WindowActivated;
    public event Action? SystemAppearanceChanged;
    public event Action<bool>? WindowActivityChanged;
    public event Action<AvaloniaUiInputKind>? InputObserved;
    public event Action<IReadOnlyList<string>>? FilesDropped;
    public event Action<string>? MediaControlRequested;
    public event Action<string>? ProtocolActivated;
    public event Action<AvaloniaUiMemoryObservation>? MemoryPressureObserved;

    private byte[] ReceivePlatformEvent(UiWireReader reader, bool worker)
    {
        var kind = (WindowPlatformEvent)reader.Int32();
        if (worker)
        {
            if (kind != WindowPlatformEvent.Memory) throw new InvalidDataException("Invalid worker event.");
            var observation = reader.AvaloniaUiMemoryObservation(); reader.End();
            MemoryPressureObserved?.Invoke(observation); return [];
        }
        switch (kind)
        {
            case WindowPlatformEvent.CloseRequested:
                reader.End(); return [(CloseRequested?.Invoke() ?? true) ? (byte)1 : (byte)0];
            case WindowPlatformEvent.PaletteOpen:
                reader.End(); return [(CommandPaletteOpenRequested?.Invoke() ?? false) ? (byte)1 : (byte)0];
            case WindowPlatformEvent.PaletteClose:
                reader.End(); return [(CommandPaletteCloseRequested?.Invoke() ?? false) ? (byte)1 : (byte)0];
            case WindowPlatformEvent.Settings:
                reader.End(); SettingsRequested?.Invoke(); break;
            case WindowPlatformEvent.Input:
                var input = reader.AvaloniaUiInputKind(); reader.End(); InputObserved?.Invoke(input); break;
            case WindowPlatformEvent.Files:
            case WindowPlatformEvent.FileActivation:
                int count = reader.Count();
                if (count < 0) throw new InvalidDataException("Missing dropped files.");
                string[] files = new string[count];
                for (int i = 0; i < count; i++) files[i] = reader.String() ?? throw new InvalidDataException("Missing dropped file.");
                reader.End();
                if (kind == WindowPlatformEvent.Files) FilesDropped?.Invoke(files);
                else Volatile.Read(ref _fileActivation)?.Invoke(files);
                break;
            case WindowPlatformEvent.Activity:
                bool active = reader.Boolean(); reader.End(); WindowActivityChanged?.Invoke(active); break;
            case WindowPlatformEvent.Media:
            case WindowPlatformEvent.Protocol:
                string text = reader.String() ?? throw new InvalidDataException("Missing platform event value."); reader.End();
                if (kind == WindowPlatformEvent.Media) MediaControlRequested?.Invoke(text); else ProtocolActivated?.Invoke(text);
                break;
            case WindowPlatformEvent.Appearance:
                reader.End(); SystemAppearanceChanged?.Invoke(); break;
            case WindowPlatformEvent.Activated:
                reader.End(); WindowActivated?.Invoke(); break;
            case WindowPlatformEvent.Closed:
                reader.End(); WindowClosed?.Invoke(); break;
            default: throw new InvalidDataException("Unknown native platform event.");
        }
        return [];
    }

    private void OnInvalidated(XsrUiShellInvalidation kind)
    {
        if (Volatile.Read(ref _uiRetired) != 0) return;
        Interlocked.Or(ref _invalidations, (int)kind);
        if (Interlocked.Exchange(ref _invalidationQueued, 1) == 0) _ = Task.Run(FlushInvalidationsAsync);
    }

    private async Task FlushInvalidationsAsync()
    {
        try
        {
            while (Volatile.Read(ref _uiRetired) == 0)
            {
                int kind = Interlocked.Exchange(ref _invalidations, 0);
                if (kind == 0) return;
                int delay = 1;
                while (Volatile.Read(ref _uiRetired) == 0)
                {
                    try { _ui.Exchange(new byte[] { (byte)WindowUiCommand.Invalidate, checked((byte)kind) }); break; }
                    catch (Nexa.Native.Ui.NativeUiCallBusyException)
                    { await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32); }
                }
            }
        }
        catch (Exception error) when (Volatile.Read(ref _uiRetired) == 0) { Report(error); }
        catch (InvalidOperationException) when (Volatile.Read(ref _uiRetired) != 0) { }
        finally
        {
            Volatile.Write(ref _invalidationQueued, 0);
            if (Volatile.Read(ref _invalidations) != 0 && Volatile.Read(ref _uiRetired) == 0)
                OnInvalidated(XsrUiShellInvalidation.None);
        }
    }
}
