using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{

    private static XsrUiSceneNode Node(XsrUiScene scene, XsrUiEntityId entity) =>
        scene.Nodes.Single(node => node.Entity.Equals(entity));

    private static XsrUiSceneNode Node(
        XsrUiEntityId entity,
        XsrUiSemanticRole role,
        bool selected = false,
        bool focusable = false,
        bool clickable = false,
        bool hovered = false) =>
        new(
            entity,
            new XsrUiRect(0, 0, 100, 40),
            0,
            role,
            role.ToString(),
            null,
            null,
            false,
            null,
            null,
            default,
            selected,
            focusable,
            clickable,
            hovered);

    private static T AssertNotNull<T>(T? value)
        where T : class =>
        value ?? throw new InvalidOperationException("Expected a non-null value.");

    private static void AssertTrue(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true but received false.");
        }
    }

    private static void AssertFalse(bool value) => AssertTrue(!value);

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }
}
