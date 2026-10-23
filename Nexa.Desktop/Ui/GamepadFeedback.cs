namespace Nexa.Desktop.Ui;

[Flags]
internal enum GamepadFeedbackChannels { None = 0, Vibration = 1, AxisDamping = 2 }
internal readonly record struct GamepadFeedback(float Vibration, float AxisDamping)
{
    internal GamepadFeedback Bounded => new(Bound(Vibration), Bound(AxisDamping));
    private static float Bound(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
internal interface IGamepadFeedback { void ApplyFeedback(string device, GamepadFeedback feedback); }
internal interface IGamepadActuator : IDisposable
{
    GamepadFeedbackChannels Channels { get; }
    bool Set(GamepadFeedback feedback);
}

/// <summary>Session-owned native outputs with a UI-independent, monotonic expiry.</summary>
internal sealed class NativeGamepadFeedback : IGamepadFeedback, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (IGamepadActuator Actuator, long Renewed)> _active = [];
    private readonly HashSet<string> _unavailable = [];
    private readonly Func<string, IGamepadActuator?> _open;
    private readonly Func<long> _milliseconds;
    private readonly Timer? _watchdog;
    private bool _disposed;
    internal const int LeaseMilliseconds = 300;

    internal NativeGamepadFeedback(Func<string, IGamepadActuator?>? open = null,
        Func<long>? milliseconds = null, bool runTimer = true)
    {
        _open = open ?? Open; _milliseconds = milliseconds ?? (() => Environment.TickCount64);
        if (runTimer) _watchdog = new(_ => Expire(), null, 100, 100);
    }

    private static IGamepadActuator? Open(string device) => OperatingSystem.IsWindows()
        ? WindowsGamepadActuator.Open(device) : OperatingSystem.IsLinux()
            ? LinuxGamepadActuator.Open(device) : OperatingSystem.IsMacOS()
                ? MacGamepadActuator.Open(device) : null;

    public void ApplyFeedback(string device, GamepadFeedback feedback)
    {
        lock (_gate)
        {
            if (_disposed) return;
            feedback = feedback.Bounded;
            if (feedback == default) { Retire(device); _unavailable.Remove(device); return; }
            if (_unavailable.Contains(device)) return;
            try
            {
                if (!_active.TryGetValue(device, out var owned))
                {
                    var actuator = _open(device);
                    if (actuator is null) { _unavailable.Add(device); return; }
                    owned = (actuator, _milliseconds());
                    _active.Add(device, owned);
                }
                feedback = new(
                    owned.Actuator.Channels.HasFlag(GamepadFeedbackChannels.Vibration) ? feedback.Vibration : 0,
                    owned.Actuator.Channels.HasFlag(GamepadFeedbackChannels.AxisDamping) ? feedback.AxisDamping : 0);
                if (feedback == default) { Retire(device); return; }
                if (!owned.Actuator.Set(feedback)) { Retire(device); _unavailable.Add(device); return; }
                _active[device] = (owned.Actuator, _milliseconds());
            }
            catch (Exception error) when (Unavailable(error)) { Retire(device); _unavailable.Add(device); }
        }
    }

    internal void RetainDevices(IReadOnlyList<GamepadSample> samples)
    {
        lock (_gate)
        {
            foreach (string device in _active.Keys.Where(id => !samples.Any(sample => sample.Id == id)).ToArray()) Retire(device);
            _unavailable.RemoveWhere(id => !samples.Any(sample => sample.Id == id));
        }
    }

    internal void Expire()
    {
        lock (_gate)
        {
            long now = _milliseconds();
            foreach (var pair in _active.Where(pair => now - pair.Value.Renewed >= LeaseMilliseconds).ToArray()) Retire(pair.Key);
        }
    }

    private static bool Unavailable(Exception error) => error is IOException or UnauthorizedAccessException
        or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException;
    private void Retire(string device)
    {
        if (!_active.Remove(device, out var owned)) return;
        try { owned.Actuator.Set(default); }
        catch (Exception error) when (Unavailable(error)) { }
        finally { owned.Actuator.Dispose(); }
    }
    public void Dispose()
    {
        _watchdog?.Dispose();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (string device in _active.Keys.ToArray()) Retire(device);
            _unavailable.Clear();
        }
    }
}
