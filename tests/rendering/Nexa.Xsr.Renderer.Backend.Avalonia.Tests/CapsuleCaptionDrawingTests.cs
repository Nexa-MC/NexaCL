using Avalonia;
using Avalonia.Media.Imaging;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using SkiaSharp;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void VerifyCapsuleCaptionScrollChangesActualInkAndKeepsIconFixed()
    {
        var tree = new XsrUiTree();
        var node = Node(tree.Create("caption-drawing"), XsrUiSemanticRole.Button) with
        {
            Rect = new(0, 0, 180, 48),
            ImageSource = "lucide/power",
            Text = "WWWWWWWWWWWWWWWWWWWWiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiii",
            CapsuleExpansionProgress = 1,
            VisualStyle = new XsrUiVisualStyle
            {
                HoverExpand = true,
                Background = new(255, 255, 255),
                Foreground = new(0, 0, 0),
                FontSize = 13,
                Surface = XsrUiSurfaceKind.Solid,
            }.Snapshot(),
            Scroll = new(0, 0, 124, 48, 600, 48, false),
        };
        var control = new AvaloniaUiSceneNodeControl(_ => { }, _ => { }, reducedMotion: () => true);
        try
        {
            using var start = Draw(0);
            using var end = Draw(260);
            int changedCaptionPixels = 0;
            for (int y = 10; y < 35; y++)
                for (int x = 16; x < 124; x++)
                    if (start.GetPixel(x, y) != end.GetPixel(x, y)) changedCaptionPixels++;
            AssertTrue(changedCaptionPixels > 20);
            for (int y = 8; y < 40; y++)
                for (int x = 144; x < 175; x++)
                    AssertEqual(start.GetPixel(x, y), end.GetPixel(x, y));
        }
        finally { control.ReleasePresentation(); }
        Console.WriteLine("PASS: actual capsule caption ink scrolls horizontally while the trailing icon stays fixed");

        SKBitmap Draw(double offset)
        {
            control.Apply(node with { Scroll = node.Scroll!.Value with { OffsetX = offset } });
            control.Measure(new Size(180, 48)); control.Arrange(new Rect(0, 0, 180, 48));
            using var target = new RenderTargetBitmap(new PixelSize(180, 48));
            using (var drawing = target.CreateDrawingContext()) control.Render(drawing);
            return ReadBrandPixels(target);
        }
    }
}
