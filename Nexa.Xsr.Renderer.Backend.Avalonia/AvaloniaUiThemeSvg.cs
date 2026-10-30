using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nexa.Core.Xml;
using XAttribute = Nexa.Core.Xml.XmlSnapshotProperty;
using XElement = Nexa.Core.Xml.XmlSnapshotElement;
using XProcessingInstruction = Nexa.Core.Xml.XmlSnapshotProcessingInstruction;
using XText = Nexa.Core.Xml.XmlSnapshotText;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>A bounded SVG geometry dialect: no external references, scripts, CSS, filters or native file reads.</summary>
internal sealed partial class AvaloniaUiThemeSvg
{
    private sealed record Shape(Geometry? Geometry, IBrush? Fill, Pen? Stroke, Matrix Transform, double Opacity);
    private readonly Shape[] _shapes;
    private readonly Rect _viewBox;
    private readonly DrawingGroup? _tileDrawing;
    private AvaloniaUiThemeSvg(Rect viewBox, Shape[] shapes, bool nativeGeometry)
    {
        _viewBox = viewBox; _shapes = shapes;
        if (nativeGeometry)
        {
            _tileDrawing = new DrawingGroup();
            using DrawingContext context = _tileDrawing.Open();
            Rect tile = new(0, 0, viewBox.Width, viewBox.Height);
            context.DrawRectangle(Brushes.Transparent, null, tile);
            Draw(context, tile, "stretch");
        }
    }
    internal int ShapeCount => _shapes.Length;

    internal static AvaloniaUiThemeSvg Parse(ReadOnlyMemory<byte> bytes, bool nativeGeometry = true)
    {
        if (bytes.Length is < 1 or > 1048576) throw new InvalidDataException("SVG exceeds its byte budget.");
        XmlSnapshotDocument document = XmlSnapshotReader.ParseBytes(bytes.Span, new(4 * 1024 * 1024, 4096, 25));
        XElement root = document.Root ?? throw new InvalidDataException("SVG root is missing.");
        if (root.Name.LocalName != "svg" || root.Name.NamespaceName is not ("" or "http://www.w3.org/2000/svg"))
            throw new InvalidDataException("SVG root is invalid.");
        if (document.DescendantNodes().OfType<XProcessingInstruction>().Any()) throw new InvalidDataException("SVG processing instructions are forbidden.");
        XElement[] elements = root.DescendantsAndSelf().ToArray();
        if (elements.Length > 4096 || elements.Any(e => e.Ancestors().Count() > 24)) throw new InvalidDataException("SVG exceeds its structure budget.");
        var transforms = new Dictionary<XElement, Matrix>(elements.Length);
        foreach (XElement e in elements)
        {
            if (e.Name.Namespace != root.Name.Namespace) throw new InvalidDataException("Foreign SVG content is forbidden.");
            if (e != root && e.Name.LocalName == "svg") throw new InvalidDataException("Nested SVG viewports are not supported.");
            if (e.Name.LocalName is not ("svg" or "g" or "defs" or "linearGradient" or "radialGradient") && e.HasElements)
                throw new InvalidDataException("SVG geometry and metadata cannot contain nested elements.");
            if (e.Parent?.Name.LocalName is "linearGradient" or "radialGradient" && e.Name.LocalName != "stop")
                throw new InvalidDataException("SVG gradients can only contain stops.");
            string allowed = e.Name.LocalName switch
            {
                "svg" => "viewBox width height preserveAspectRatio",
                "g" => "",
                "defs" => "",
                "title" or "desc" => "",
                "path" => "d fill-rule",
                "rect" => "x y width height rx ry",
                "circle" => "cx cy r",
                "ellipse" => "cx cy rx ry",
                "line" => "x1 y1 x2 y2",
                "polyline" or "polygon" => "points",
                "linearGradient" => "id x1 y1 x2 y2 gradientUnits",
                "radialGradient" => "id cx cy r fx fy gradientUnits",
                "stop" => "offset stop-color stop-opacity",
                _ => throw new InvalidDataException("Unsupported SVG element: " + e.Name.LocalName),
            };
            var attributes = (allowed + " id fill stroke stroke-width stroke-linecap stroke-linejoin opacity fill-opacity stroke-opacity transform").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            foreach (XAttribute a in e.Attributes())
                if (!a.IsNamespaceDeclaration && (a.Name.NamespaceName.Length != 0 || !attributes.Contains(a.Name.LocalName)))
                    throw new InvalidDataException("Unsupported SVG attribute: " + a.Name);
            foreach (XAttribute number in e.Attributes().Where(a => new[] { "x", "y", "width", "height", "rx", "ry", "cx", "cy", "r", "fx", "fy", "x1", "x2", "y1", "y2", "stroke-width", "opacity", "fill-opacity", "stroke-opacity", "stop-opacity", "offset" }.Contains(a.Name.LocalName, StringComparer.Ordinal)))
            {
                double value = Scalar(number.Value, percentage: e.Name.LocalName is "linearGradient" or "radialGradient" or "stop");
                if (number.Name.LocalName is "opacity" or "fill-opacity" or "stroke-opacity" or "stop-opacity" && value is < 0 or > 1)
                    throw new InvalidDataException("SVG opacity is outside its budget.");
            }
            if (e.Name.LocalName == "path") ValidatePathData(e);
            Matrix local = ParseTransform(e.Attribute("transform")?.Value);
            Matrix inherited = e.Parent is { } parent ? transforms[parent] : Matrix.Identity;
            transforms.Add(e, ValidateTransform(local * inherited));
            if (e.Name.LocalName is not ("title" or "desc") && e.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
                throw new InvalidDataException("SVG text requires the font/text capability and is not part of the safe vector dialect.");
        }
        double[] vb = root.Attribute("viewBox") is { } box ? Numbers(box.Value, 4) : [0, 0, Number(root, "width", 24), Number(root, "height", 24)];
        if (vb.Length != 4 || vb[2] is < .000001 or > 8192 || vb[3] is < .000001 or > 8192) throw new InvalidDataException("Invalid SVG viewBox.");
        var gradients = new Dictionary<string, IBrush>(StringComparer.Ordinal);
        foreach (XElement gradient in elements.Where(e => e.Name.LocalName is "linearGradient" or "radialGradient"))
        {
            string id = gradient.Attribute("id")?.Value ?? throw new InvalidDataException("Gradient identity is missing.");
            if (id.Length is < 1 or > 128 || gradients.ContainsKey(id)) throw new InvalidDataException("Gradient identity is invalid or duplicated.");
            if (gradient.Attribute("gradientUnits")?.Value is { } units && units != "objectBoundingBox") throw new InvalidDataException("Only objectBoundingBox SVG gradients are supported.");
            XElement[] stops = gradient.Elements().ToArray();
            if (stops.Length is < 2 or > 32 || stops.Any(s => s.Name.LocalName != "stop")) throw new InvalidDataException("Invalid gradient stops.");
            GradientStops colors = [];
            double previous = -1;
            foreach (XElement stop in stops)
            {
                double position = Scalar(stop.Attribute("offset")?.Value ?? "0", percentage: true);
                if (position < previous || position is < 0 or > 1) throw new InvalidDataException("Gradient stops must be ordered and bounded.");
                previous = position;
                colors.Add(new GradientStop(Alpha(Color.Parse(stop.Attribute("stop-color")?.Value ?? "black"), Number(stop, "stop-opacity", 1)), position));
            }
            IBrush brush;
            if (gradient.Name.LocalName == "linearGradient")
                brush = new LinearGradientBrush { StartPoint = Relative(gradient, "x1", "y1", 0, 0), EndPoint = Relative(gradient, "x2", "y2", 1, 0), GradientStops = colors };
            else
            {
                double radius = Scalar(gradient.Attribute("r")?.Value ?? "50%", true);
                if (radius is <= 0 or > 2) throw new InvalidDataException("Invalid SVG gradient radius.");
                brush = new RadialGradientBrush { Center = Relative(gradient, "cx", "cy", .5, .5), GradientOrigin = Relative(gradient, "fx", "fy", .5, .5), RadiusX = new RelativeScalar(radius, RelativeUnit.Relative), RadiusY = new RelativeScalar(radius, RelativeUnit.Relative), GradientStops = colors };
            }
            gradients.Add(id, brush);
        }
        foreach (XElement element in elements)
        {
            if (element.Attribute("fill") is { } fill) Paint(fill.Value, gradients, 1);
            if (element.Attribute("stroke") is { } stroke) Paint(stroke.Value, gradients, 1);
        }
        List<Shape> shapes = [];
        foreach (XElement e in elements)
        {
            if (e.Ancestors().Any(a => a.Name.LocalName == "defs") || e.Name.LocalName is "svg" or "g" or "defs" or "linearGradient" or "radialGradient" or "stop" or "title" or "desc") continue;
            Geometry? geometry = nativeGeometry ? e.Name.LocalName switch
            {
                "path" => Path(e),
                "rect" => Rectangle(e),
                "circle" => new EllipseGeometry(new Rect(Number(e, "cx", 0) - Number(e, "r", 0), Number(e, "cy", 0) - Number(e, "r", 0), Positive(e, "r") * 2, Positive(e, "r") * 2)),
                "ellipse" => new EllipseGeometry(new Rect(Number(e, "cx", 0) - Number(e, "rx", 0), Number(e, "cy", 0) - Number(e, "ry", 0), Positive(e, "rx") * 2, Positive(e, "ry") * 2)),
                "line" => new LineGeometry(new Point(Number(e, "x1", 0), Number(e, "y1", 0)), new Point(Number(e, "x2", 0), Number(e, "y2", 0))),
                _ => Polygon(e),
            } : null;
            IBrush? fill = Paint(Inherited(e, "fill") ?? "black", gradients, Scalar(Inherited(e, "fill-opacity") ?? "1"));
            IBrush? stroke = Paint(Inherited(e, "stroke") ?? "none", gradients, Scalar(Inherited(e, "stroke-opacity") ?? "1"));
            double width = Scalar(Inherited(e, "stroke-width") ?? "1");
            if (width is < 0 or > 256) throw new InvalidDataException("Invalid SVG stroke width.");
            Pen? pen = stroke is null ? null : new Pen(stroke, width)
            {
                LineCap = (Inherited(e, "stroke-linecap") ?? "butt") switch { "butt" => PenLineCap.Flat, "round" => PenLineCap.Round, "square" => PenLineCap.Square, _ => throw new InvalidDataException("Unsupported stroke cap.") },
                LineJoin = (Inherited(e, "stroke-linejoin") ?? "miter") switch { "miter" => PenLineJoin.Miter, "round" => PenLineJoin.Round, "bevel" => PenLineJoin.Bevel, _ => throw new InvalidDataException("Unsupported stroke join.") },
            };
            Matrix transform = transforms[e];
            double opacity = 1;
            foreach (XElement ancestor in e.AncestorsAndSelf().Reverse())
            {
                opacity *= Scalar(ancestor.Attribute("opacity")?.Value ?? "1");
            }
            if (opacity is < 0 or > 1) throw new InvalidDataException("Invalid SVG opacity.");
            shapes.Add(new(geometry, fill, pen, transform, opacity));
        }
        if (shapes.Count == 0) throw new InvalidDataException("SVG contains no drawable geometry.");
        return new(new Rect(vb[0], vb[1], vb[2], vb[3]), shapes.ToArray(), nativeGeometry);
    }

    internal void Draw(DrawingContext context, Rect target, string fitMode = "contain")
    {
        if (fitMode == "tile")
        {
            // The backend repeats one prepared vector picture. Work is bounded by shape
            // count and does not grow with viewport area or a tiny authored viewBox.
            var brush = new DrawingBrush(_tileDrawing ?? throw new InvalidOperationException("SVG native geometry is not prepared."))
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.Fill,
                SourceRect = new RelativeRect(0, 0, _viewBox.Width, _viewBox.Height, RelativeUnit.Absolute),
                DestinationRect = new RelativeRect(target.X, target.Y, _viewBox.Width, _viewBox.Height, RelativeUnit.Absolute),
            };
            context.DrawRectangle(brush, null, target);
            return;
        }
        double scale = fitMode switch
        {
            "contain" => Math.Min(target.Width / _viewBox.Width, target.Height / _viewBox.Height),
            "cover" => Math.Max(target.Width / _viewBox.Width, target.Height / _viewBox.Height),
            "stretch" => 1,
            _ => throw new InvalidDataException("Unsupported SVG image fit."),
        };
        double sx = fitMode == "stretch" ? target.Width / _viewBox.Width : scale, sy = fitMode == "stretch" ? target.Height / _viewBox.Height : scale;
        Matrix fit = Matrix.CreateTranslation(-_viewBox.X, -_viewBox.Y) * Matrix.CreateScale(sx, sy)
            * Matrix.CreateTranslation(target.X + (target.Width - _viewBox.Width * sx) / 2, target.Y + (target.Height - _viewBox.Height * sy) / 2);
        using var clip = context.PushClip(target);
        foreach (Shape shape in _shapes)
        {
            using var transform = context.PushTransform(shape.Transform * fit);
            using var opacity = context.PushOpacity(shape.Opacity);
            context.DrawGeometry(shape.Fill, shape.Stroke, shape.Geometry ?? throw new InvalidOperationException("SVG native geometry is not prepared."));
        }
    }

    internal Bitmap Rasterize(int size = 256)
    {
        if (size is < 16 or > 512) throw new ArgumentOutOfRangeException(nameof(size));
        using var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (DrawingContext context = target.CreateDrawingContext()) Draw(context, new Rect(0, 0, size, size));
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
        return new Bitmap(stream);
    }

    private static void ValidatePathData(XElement element)
    {
        string data = element.Attribute("d")?.Value ?? throw new InvalidDataException("SVG path data is missing.");
        if (data.Length is < 1 or > 131072 || data.Any(c => !char.IsWhiteSpace(c) && !char.IsDigit(c) && "MmLlHhVvCcSsQqTtAaZzEe+-,.".IndexOf(c) < 0))
            throw new InvalidDataException("Invalid SVG path declaration.");
        MatchCollection numbers = NumberPattern().Matches(data);
        if (numbers.Count > 65536) throw new InvalidDataException("SVG path exceeds its geometry budget.");
        foreach (Match number in numbers) _ = Scalar(number.Value);
    }

    private static Geometry Path(XElement e)
    {
        string data = e.Attribute("d")?.Value ?? throw new InvalidDataException("SVG path data is missing.");
        if (data.Length is < 1 or > 131072) throw new InvalidDataException("SVG path exceeds its budget.");
        string rule = e.Attribute("fill-rule")?.Value ?? "nonzero";
        string prefix = rule switch { "evenodd" => "F0 ", "nonzero" => "F1 ", _ => throw new InvalidDataException("Unsupported SVG fill-rule.") };
        Geometry geometry = Geometry.Parse(prefix + data);
        return geometry;
    }
    private static RectangleGeometry Rectangle(XElement e)
    {
        double rx = Number(e, "rx", 0), ry = Number(e, "ry", rx);
        if (rx < 0 || ry < 0) throw new InvalidDataException("Invalid SVG rectangle radius.");
        return new RectangleGeometry(new Rect(Number(e, "x", 0), Number(e, "y", 0), Positive(e, "width"), Positive(e, "height")), rx, ry);
    }
    private static StreamGeometry Polygon(XElement e)
    {
        double[] points = Numbers(e.Attribute("points")?.Value ?? "", 4096);
        if (points.Length < 4 || points.Length % 2 != 0) throw new InvalidDataException("Invalid SVG points.");
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(new Point(points[0], points[1]), isFilled: e.Name.LocalName == "polygon");
        for (int i = 2; i < points.Length; i += 2) context.LineTo(new Point(points[i], points[i + 1]));
        context.EndFigure(e.Name.LocalName == "polygon");
        return geometry;
    }
    private static string? Inherited(XElement e, string name) => e.AncestorsAndSelf().Select(a => a.Attribute(name)?.Value).FirstOrDefault(v => v is not null);
    private static IBrush? Paint(string text, Dictionary<string, IBrush> gradients, double opacity)
    {
        if (opacity is < 0 or > 1) throw new InvalidDataException("Invalid SVG paint opacity.");
        if (text == "none") return null;
        if (text.StartsWith("url(#", StringComparison.Ordinal) && text.EndsWith(')'))
        {
            if (opacity != 1) throw new InvalidDataException("Gradient paint opacity must use element opacity.");
            return gradients.TryGetValue(text[5..^1], out IBrush? brush) ? brush : throw new InvalidDataException("SVG gradient reference is missing.");
        }
        return new SolidColorBrush(Alpha(Color.Parse(text), opacity));
    }
    private static Color Alpha(Color color, double opacity) => Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
    private static double Positive(XElement e, string name) { double v = Number(e, name, 0); return v > 0 ? v : throw new InvalidDataException("SVG dimension must be positive."); }
    private static double Number(XElement e, string name, double fallback) => e.Attribute(name) is { } a ? Scalar(a.Value) : fallback;
    private static RelativePoint Relative(XElement e, string x, string y, double dx, double dy) => new(e.Attribute(x) is { } ax ? Scalar(ax.Value, true) : dx, e.Attribute(y) is { } ay ? Scalar(ay.Value, true) : dy, RelativeUnit.Relative);
    private static double Scalar(string text, bool percentage = false)
    {
        bool percent = percentage && text.EndsWith('%');
        double value = double.Parse(percent ? text[..^1] : text, NumberStyles.Float, CultureInfo.InvariantCulture) / (percent ? 100 : 1);
        return double.IsFinite(value) && Math.Abs(value) <= 1000000 ? value : throw new InvalidDataException("SVG number is outside its budget.");
    }
    private static double[] Numbers(string text, int maximum)
    {
        var values = NumberPattern().Matches(text);
        if (values.Count > maximum || NumberPattern().Replace(text, "").Any(c => !char.IsWhiteSpace(c) && c != ',')) throw new InvalidDataException("Invalid SVG number list.");
        return values.Select(m => Scalar(m.Value)).ToArray();
    }
    private static Matrix ParseTransform(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Matrix.Identity;
        if (text.Length > 2048) throw new InvalidDataException("SVG transform exceeds its budget.");
        var matches = TransformPattern().Matches(text);
        if (matches.Count > 32 || TransformPattern().Replace(text, "").Any(c => !char.IsWhiteSpace(c) && c != ',')) throw new InvalidDataException("Invalid SVG transform.");
        Matrix result = Matrix.Identity;
        foreach (Match m in matches)
        {
            double[] n = Numbers(m.Groups[2].Value, 6);
            Matrix value = m.Groups[1].Value switch
            {
                "matrix" when n.Length == 6 => new Matrix(n[0], n[1], n[2], n[3], n[4], n[5]),
                "translate" when n.Length is 1 or 2 => Matrix.CreateTranslation(n[0], n.Length == 2 ? n[1] : 0),
                "scale" when n.Length is 1 or 2 => Matrix.CreateScale(n[0], n.Length == 2 ? n[1] : n[0]),
                "rotate" when n.Length == 1 => Matrix.CreateRotation(n[0] * Math.PI / 180),
                "rotate" when n.Length == 3 => Matrix.CreateTranslation(-n[1], -n[2]) * Matrix.CreateRotation(n[0] * Math.PI / 180) * Matrix.CreateTranslation(n[1], n[2]),
                _ => throw new InvalidDataException("Unsupported SVG transform."),
            };
            result = ValidateTransform(value * result);
        }
        return result;
    }
    private static Matrix ValidateTransform(Matrix matrix)
    {
        foreach (double coefficient in new[] { matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32 })
            if (!double.IsFinite(coefficient) || Math.Abs(coefficient) > 1000000)
                throw new InvalidDataException("Composed SVG transform is outside its finite budget.");
        return matrix;
    }
    [GeneratedRegex(@"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
    [GeneratedRegex(@"([A-Za-z]+)\s*\(([^()]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex TransformPattern();
}
