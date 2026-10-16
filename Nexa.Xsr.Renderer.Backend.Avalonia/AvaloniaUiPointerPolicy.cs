using Avalonia;
using Avalonia.Input;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>Admits one primary contact to a native move or resize operation.</summary>
internal static class AvaloniaUiPointerPolicy
{
    internal static bool IsPrimaryPress(PointerPressedEventArgs args, Visual relativeTo) =>
        args.Pointer.IsPrimary && args.GetCurrentPoint(relativeTo).Properties.IsLeftButtonPressed;
}
