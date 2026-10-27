using System.Globalization;
using Nexa.Xsr;

namespace Nexa.Xsr.Renderer.Nxml;

/// <summary>Host-local state for an admitted asynchronous scroll observation.</summary>
public sealed class NxmlScrollObservation(XsrSemanticId command)
{
    public XsrSemanticId Command { get; } = command;
    private string? _last;
    private int _pending;
    public bool TryBegin(double offset, double viewport, double content, out string argument)
    {
        argument = "";
        if (!double.IsFinite(offset) || !double.IsFinite(viewport) || !double.IsFinite(content)
            || offset < 0 || viewport <= 0 || content < 0 || Volatile.Read(ref _pending) != 0) return false;
        argument = string.Create(CultureInfo.InvariantCulture, $"{offset:0.##}|{viewport:0.##}|{content:0.##}");
        if (_last == argument || Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return false;
        _last = argument; return true;
    }
    public void Complete() => Volatile.Write(ref _pending, 0);
}
