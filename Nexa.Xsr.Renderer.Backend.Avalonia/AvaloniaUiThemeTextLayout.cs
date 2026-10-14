using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.TextFormatting;
using Nexa.Xsr.Renderer;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>Native shaping and measurement shared with paint and caret placement. Contains no external reads.</summary>
public sealed class AvaloniaUiThemeTextLayout : IXsrUiTextLayoutProvider, IDisposable
{
    private sealed class ResourceFontCollection(Uri key) : FontCollectionBase { public override Uri Key => key; }
    private readonly Dictionary<string, FontFamily> _families = new(StringComparer.Ordinal);
    private readonly Dictionary<XsrUiTextLayoutRequest, XsrUiTextLayoutMetrics> _metrics = [];
    private readonly Dictionary<(string? Family, FontWeight Weight, FontStyle Style), Typeface> _faces = [];
    private readonly Uri _collectionKey;
    private bool _registered, _disposed;
    public long Revision { get; }
    internal int CachedMeasurements => _metrics.Count;

    public AvaloniaUiThemeTextLayout(long revision, IReadOnlyList<AvaloniaUiThemeResource>? resources = null)
    {
        _ = revision;
        _collectionKey = new Uri("fonts:ntp-" + Guid.NewGuid().ToString("N"));
        var fonts = (resources ?? []).Where(r => r.MediaType is "font/ttf" or "font/otf").ToArray();
        using (var identity = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            identity.AppendData(Encoding.UTF8.GetBytes(AvaloniaUiTypefaceCache.GetDefault(FontWeight.Normal).FontFamily.ToString()));
            foreach (AvaloniaUiThemeResource font in fonts.OrderBy(f => f.ResourceId, StringComparer.Ordinal))
            {
                identity.AppendData(Encoding.UTF8.GetBytes(font.ResourceId + "\0"));
                identity.AppendData(SHA256.HashData(font.Content.Span));
            }
            Revision = BinaryPrimitives.ReadInt64LittleEndian(identity.GetHashAndReset().AsSpan(0, 8)) & long.MaxValue;
        }
        if (fonts.Length > 32) throw new InvalidDataException("Theme font count exceeds its budget.");
        var collection = new ResourceFontCollection(_collectionKey);
        try
        {
            foreach (AvaloniaUiThemeResource font in fonts)
            {
                AvaloniaUiThemeFontData.Validate(font.Content.Span);
                using var stream = new MemoryStream(font.Content.ToArray(), writable: false);
                if (!collection.TryAddGlyphTypeface(stream, out GlyphTypeface? glyph)) throw new InvalidDataException("Theme font cannot be loaded by the native backend.");
                _families.Add(font.ResourceId, new FontFamily(_collectionKey.AbsoluteUri + "#" + glyph.FamilyName));
            }
            if (fonts.Length != 0) { FontManager.Current.AddFontCollection(collection); _registered = true; }
            else ((IDisposable)collection).Dispose();
        }
        catch { ((IDisposable)collection).Dispose(); throw; }
    }

    internal Typeface Resolve(string? identity, FontWeight weight, FontStyle style = FontStyle.Normal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faces.TryGetValue((identity, weight, style), out Typeface cached)) return cached;
        Typeface face;
        if (identity is not null && _families.TryGetValue(identity, out FontFamily? resource))
            face = new Typeface(resource, style, weight);
        else if (!string.IsNullOrWhiteSpace(identity) && identity.Length <= 256 && !identity.Any(c => c is '/' or '\\' or ':' or '#')
            && FontManager.Current.SystemFonts.Any(f => string.Equals(f.Name, identity, StringComparison.OrdinalIgnoreCase)))
            face = new Typeface(new FontFamily(identity), style, weight);
        else face = AvaloniaUiTypefaceCache.GetDefault(weight, style);
        if (!FontManager.Current.TryGetGlyphTypeface(face, out _)) face = AvaloniaUiTypefaceCache.GetDefault(weight, style);
        if (_faces.Count >= 512) _faces.Clear();
        _faces.Add((identity, weight, style), face);
        return face;
    }

    internal FormattedText Format(string text, string? family, double size, double weight, IBrush foreground,
        double maximumWidth = double.PositiveInfinity, int maximumLines = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            Resolve(family, (FontWeight)(int)Math.Clamp(weight, 100, 900)), size, foreground);
        if (double.IsFinite(maximumWidth) && maximumWidth > 0) formatted.MaxTextWidth = maximumWidth;
        if (maximumLines > 0) formatted.MaxLineCount = maximumLines;
        return formatted;
    }

    public XsrUiTextLayoutMetrics Measure(XsrUiTextLayoutRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.Text.Length > 65536 || !double.IsFinite(request.FontSize) || request.FontSize is <= 0 or > 512
            || !double.IsFinite(request.FontWeight) || request.FontWeight is < 100 or > 900)
            throw new ArgumentException("Text request exceeds its bounded layout contract.", nameof(request));
        if (!request.IsPassword && _metrics.TryGetValue(request, out XsrUiTextLayoutMetrics? cached)) return cached;
        FormattedText formatted = Format(request.Text, request.FontFamily, request.FontSize, request.FontWeight,
            Brushes.Black, request.MaximumWidth, request.MaximumLines);
        foreach (XsrUiTextRun run in request.TextRuns ?? [])
        {
            if (run.Start < 0 || run.Length <= 0 || run.Start > request.Text.Length - run.Length) continue;
            FontWeight weight = run.Bold ? FontWeight.Bold : (FontWeight)(int)Math.Clamp(request.FontWeight, 100, 900);
            FontStyle style = run.Italic ? FontStyle.Italic : FontStyle.Normal;
            formatted.SetFontFamily(Resolve(request.FontFamily, weight, style).FontFamily, run.Start, run.Length);
            formatted.SetFontWeight(weight, run.Start, run.Length); formatted.SetFontStyle(style, run.Start, run.Length);
        }
        double[]? caret = request.IncludeCaretPositions ? Carets(request.Text, request.FontFamily, request.FontSize, request.FontWeight) : null;
        var metrics = new XsrUiTextLayoutMetrics(new XsrUiSize(formatted.WidthIncludingTrailingWhitespace, formatted.Height), caret);
        if (!request.IsPassword)
        {
            if (_metrics.Count >= 1024) _metrics.Clear();
            _metrics.Add(request, metrics);
        }
        return metrics;
    }

    internal double[] Carets(string text, string? family, double size, double weight)
    {
        using var layout = new TextLayout(text, Resolve(family, (FontWeight)(int)Math.Clamp(weight, 100, 900)), size, Brushes.Black);
        double[] positions = new double[text.Length + 1];
        foreach (int boundary in StringInfo.ParseCombiningCharacters(text).Append(text.Length))
        {
            Rect rect = layout.HitTestTextPosition(boundary);
            positions[boundary] = rect.X;
        }
        // Non-boundaries (surrogates/combining sequences) map to the previous legal caret.
        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToHashSet();
        for (int i = 1; i < positions.Length; i++) if (!boundaries.Contains(i)) positions[i] = positions[i - 1];
        return positions;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _metrics.Clear(); _faces.Clear(); _families.Clear();
        if (_registered) { FontManager.Current.RemoveFontCollection(_collectionKey); _registered = false; }
    }
}
