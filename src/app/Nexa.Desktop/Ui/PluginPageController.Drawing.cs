using System.Globalization;
using Nexa.Core.Media;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using SkiaSharp;

namespace Nexa.Desktop.Ui;

internal sealed partial class PluginPageController
{
    private readonly Dictionary<EntityId, (XsrUiEntityId Entity, Revision Revision)> _drawingNodes = [];
    private readonly Dictionary<EntityId, Revision> _drawingRejected = [];
    private void FrameDrawings()
    {
        var drawings = Application.Drawings();
        foreach (var key in _drawingRejected.Keys.Where(key => !drawings.Any(d => d.Surface.Id == key)).ToArray()) _drawingRejected.Remove(key);
        foreach (var key in _drawingNodes.Keys.Where(key => !drawings.Any(d => d.Surface.Id == key)).ToArray())
        {
            _dynamic.Remove(_drawingNodes[key].Entity);
            _drawingNodes.Remove(key); _drawingRejected.Remove(key);
        }
        foreach (var drawing in drawings)
        {
            if (_drawingRejected.GetValueOrDefault(drawing.Surface.Id) == drawing.Surface.Revision) continue;
            var roots = _dialogs.Values.Where(d => d.State.Owner == drawing.Owner && d.State.Reference.ViewScope == drawing.Surface.ViewScope).Select(d => d.Document.Root)
                .Concat(_pages.Values.Where(p => p.State.Owner == drawing.Owner && drawing.Surface.ViewScope == drawing.Owner.LoadInstanceId).Select(p => p.Document.Root));
            string key = drawing.Name.Value[(drawing.Name.Value.IndexOf(':') + 1)..];
            var slots = roots.SelectMany(root => Names(root).Where(p => p.Key == key).Select(p => p.Value)).ToArray();
            if (slots.Length != 1) continue;
            var prior = _drawingNodes.GetValueOrDefault(drawing.Surface.Id);
            if (prior.Revision == drawing.Surface.Revision && _shell.Tree.IsAlive(prior.Entity)) continue;
            try
            {
                var raster = Rasterize(drawing);
                if (!_shell.Tree.IsAlive(prior.Entity))
                {
                    var entity = _dynamic.Append(slots[0], DesktopMarkup.Dynamic("PluginDrawing.d.nxml", new Dictionary<string, string>
                    {
                        ["key"] = "plugin-surface-" + drawing.Surface.Id.Value,
                        ["label"] = drawing.Name.Value,
                        ["width"] = drawing.Width.ToString("R", CultureInfo.InvariantCulture),
                        ["height"] = drawing.Height.ToString("R", CultureInfo.InvariantCulture)
                    }));
                    prior = (entity, drawing.Surface.Revision);
                }
                _shell.Tree.SetComponent(prior.Entity, raster); _drawingNodes[drawing.Surface.Id] = (prior.Entity, drawing.Surface.Revision);
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { _drawingRejected[drawing.Surface.Id] = drawing.Surface.Revision; _feedback.Warn(Copy("plugin.page-rejected") + ": " + drawing.Name.Value + (_developer() ? ": " + error.Message : "")); }
        }
    }
    private XsrUiRasterImage Rasterize(PluginDrawing drawing)
    {
        using var bitmap = new SKBitmap((int)Math.Ceiling(drawing.Width), (int)Math.Ceiling(drawing.Height), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
        foreach (var command in drawing.Commands)
        {
            var values = command.Coordinates.Select(n => (float)n).ToArray();
            using var paint = new SKPaint { Color = SKColor.Parse(command.Color), IsAntialias = true };
            switch (command.Kind)
            {
                case DrawCommandKind.Line:
                    paint.StrokeWidth = values.Length == 5 ? Math.Clamp(values[4], .1f, 128) : 1; canvas.DrawLine(values[0], values[1], values[2], values[3], paint); break;
                case DrawCommandKind.Rectangle: canvas.DrawRect(values[0], values[1], values[2], values[3], paint); break;
                case DrawCommandKind.Ellipse: canvas.DrawOval(SKRect.Create(values[0], values[1], values[2], values[3]), paint); break;
                case DrawCommandKind.Clip: canvas.ClipRect(SKRect.Create(values[0], values[1], values[2], values[3])); break;
                case DrawCommandKind.Transform:
                    canvas.Concat(new SKMatrix(values[0], values[2], values[4], values[1], values[3], values[5], 0, 0, 1)); break;
                case DrawCommandKind.Text:
                    using (var font = new SKFont(SKTypeface.Default, Math.Clamp(values[2], 1, 128))) canvas.DrawText(command.Text, values[0], values[1], font, paint); break;
                case DrawCommandKind.Path:
                    using (var path = new SKPath())
                    {
                        path.MoveTo(values[0], values[1]); for (int i = 2; i < values.Length; i += 2) path.LineTo(values[i], values[i + 1]);
                        paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1; canvas.DrawPath(path, paint);
                    }
                    break;
                case DrawCommandKind.Image:
                    byte[] bytes = Application.CopyBlob(drawing.Owner, command.Image!);
                    try
                    {
                        using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data);
                        if (codec is null || codec.Info.Width is < 1 or > 4096 || codec.Info.Height is < 1 or > 4096 || (long)codec.Info.Width * codec.Info.Height > 4 * 1024 * 1024)
                            throw new InvalidDataException("plugin.image.invalid");
                        using var image = SKBitmap.Decode(codec); if (image is null) throw new InvalidDataException("plugin.image.invalid");
                        canvas.DrawBitmap(image, SKRect.Create(values[0], values[1], values[2], values[3]), paint);
                    }
                    finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
                    break;
            }
        }
        using var snapshot = SKImage.FromBitmap(bitmap); using var encoded = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        var result = PngImage.TryCreatePreview(encoded.AsSpan()) ?? throw new InvalidDataException("plugin.surface.invalid");
        return new(result, []) { FitToBounds = true };
    }
    private void DrawingObserved(XsrUiEntityId entity, XsrUiInputObservation observation)
    {
        if (_closed || _application is null || _feedback.Snapshot().Dialog is not null) return;
        var modal = _shell.Stage.Overlays.LastOrDefault(overlay => _shell.Tree.GetComponent<XsrUiOverlayLayer>(overlay)?.IsModal == true);
        if (modal.IsAssigned && !_dialogs.Values.Any(d => d.Chrome == modal && d.Document.Owns(entity))) return;
        var pair = _drawingNodes.FirstOrDefault(p => p.Value.Entity == entity);
        if (pair.Key is null || !Enum.TryParse<InputEventKind>(observation.Kind, out var kind)) return;
        // The renderer supplied a visible, enabled target; unrelated Host input is never forwarded.
        Application.DrawingInput(pair.Key, kind, observation.Phase, observation.X, observation.Y, observation.Text);
    }
}
