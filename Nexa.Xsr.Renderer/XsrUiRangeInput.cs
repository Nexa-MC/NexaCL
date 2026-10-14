namespace Nexa.Xsr.Renderer;

/// <summary>Host-declared finite range facts and renderer-local numeric draft; themes cannot change them.</summary>
public sealed class XsrUiRangeInput
{
    public XsrUiRangeInput(double minimum, double maximum, double step, double value) => Reset(minimum, maximum, step, value);
    public double Minimum { get; private set; }
    public double Maximum { get; private set; }
    public double Step { get; private set; }
    public double Value { get; private set; }

    public void Reset(double minimum, double maximum, double step, double value, bool preserveValue = false)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || !double.IsFinite(step) || !double.IsFinite(value)
            || minimum >= maximum || step <= 0 || step > maximum - minimum || Math.Abs(minimum) > 1e12 || Math.Abs(maximum) > 1e12
            || (maximum - minimum) / step > 1e9)
            throw new ArgumentException("A range input requires finite ordered bounds and a bounded positive step.");
        double retained = preserveValue ? Value : value;
        Minimum = minimum; Maximum = maximum; Step = step; Value = Snap(retained);
    }

    internal bool SetValue(double value)
    {
        if (!double.IsFinite(value)) return false;
        double next = Snap(value);
        if (next == Value) return false;
        Value = next; return true;
    }
    private double Snap(double value) => value >= Maximum ? Maximum : value <= Minimum ? Minimum : Math.Clamp(Minimum
        + Math.Round((value - Minimum) / Step, MidpointRounding.AwayFromZero) * Step, Minimum, Maximum);
    public XsrUiRangeSnapshot Snapshot() => new(Minimum, Maximum, Step, Value);
}

public readonly record struct XsrUiRangeSnapshot(double Minimum, double Maximum, double Step, double Value)
{
    public double Progress => Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1);
    public XsrUiRect Track(XsrUiRect rect) => Maximum > Minimum
        ? new(rect.X + Math.Min(9, rect.Width / 2), rect.Y + Math.Max(0, (rect.Height - 4) / 2),
            Math.Max(0, rect.Width - 18), Math.Min(4, rect.Height)) : default;
    public XsrUiRect Thumb(XsrUiRect rect)
    {
        XsrUiRect track = Track(rect); double diameter = Math.Min(18, Math.Min(rect.Width, rect.Height));
        return new(track.X + track.Width * Progress - diameter / 2, rect.Y + (rect.Height - diameter) / 2, diameter, diameter);
    }
}
