namespace Nexa.Native.Window.Wire;

internal enum WindowInputOperation { Poll = 0, DoubleClick = 1, CancelDoubleClick = 2 }

internal static class WindowInputProtocol
{
    internal static TimeSpan Interval(long ticks)
    {
        if (ticks <= 0 || ticks > TimeSpan.TicksPerDay)
            throw new InvalidDataException("Invalid native input polling interval.");
        return TimeSpan.FromTicks(ticks);
    }
}
