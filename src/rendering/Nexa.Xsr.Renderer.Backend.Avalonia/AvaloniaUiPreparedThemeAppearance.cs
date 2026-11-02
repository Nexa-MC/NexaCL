using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

/// <summary>Prepared native resources. Activation transfers ownership to the platform host.</summary>
public sealed class AvaloniaUiPreparedThemeAppearance : IDisposable
{
    private readonly Dictionary<string, Bitmap> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AvaloniaUiThemeSvg> _vectors = new(StringComparer.Ordinal);
    internal AvaloniaUiBrandImages? Brand { get; private set; }
    internal AvaloniaUiBrandImages? WindowIcons { get; private set; }
    internal AvaloniaUiBrandImages? TrayIcons { get; private set; }
    public AvaloniaUiThemeStartupSnapshot? StartupSnapshot { get; private set; }
    public AvaloniaUiThemeAppearance Appearance { get; }
    public AvaloniaUiThemeTextLayout? TextLayout { get; private set; }
    public bool IsNativePrepared { get; private set; }
    public bool IsDisposed { get; private set; }
    public bool IsRetired { get; private set; }
    private readonly object _lifetimeGate = new();
    private int _nativeBorrows;
    public long DecodedBitmapBytes { get; private set; }
    internal bool IsTransferred { get; set; }

    internal AvaloniaUiPreparedThemeAppearance(AvaloniaUiThemeAppearance appearance, bool deferNative = false)
    {
        Appearance = appearance.Snapshot();
        foreach (AvaloniaUiThemeResource resource in (Appearance.Resources ?? []).Concat(new[] { Appearance.BrandLight, Appearance.BrandDark, Appearance.WindowIconLight, Appearance.WindowIconDark, Appearance.TrayIconLight, Appearance.TrayIconDark, Appearance.SplashIconLight, Appearance.SplashIconDark }.OfType<AvaloniaUiThemeResource>()))
        {
            if (resource.MediaType == "image/svg+xml") AvaloniaUiThemeSvg.Parse(resource.Content, nativeGeometry: false);
            else if (resource.MediaType is "font/ttf" or "font/otf")
            {
                AvaloniaUiThemeFontData.Validate(resource.Content.Span);
            }
        }
        if (!deferNative) PrepareNative();
    }

    internal void PrepareNative()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (IsNativePrepared) return;
        TextLayout = new(Appearance.Generation, Appearance.Resources);
        try
        {
            foreach (AvaloniaUiThemeResource resource in Appearance.Resources ?? [])
            {
                if (resource.MediaType == "image/svg+xml") _vectors.Add(resource.ResourceId, AvaloniaUiThemeSvg.Parse(resource.Content));
                else if (resource.MediaType.StartsWith("image/", StringComparison.Ordinal)) _images.Add(resource.ResourceId, RetainBitmap(Decode(resource)));
            }
            AvaloniaUiBrandImages? Pair(AvaloniaUiThemeResource? light, AvaloniaUiThemeResource? dark)
            {
                if (light is null || dark is null) return null;
                Bitmap first = RetainBitmap(Decode(light, maximumWidth: 512, maximumHeight: 512));
                try { return AvaloniaUiBrandImages.LoadOwned(first, RetainBitmap(Decode(dark, maximumWidth: 512, maximumHeight: 512))); }
                catch { first.Dispose(); throw; }
            }
            Brand = Pair(Appearance.BrandLight, Appearance.BrandDark) ?? AvaloniaUiBrandImages.TryLoadProduct();
            WindowIcons = Pair(Appearance.WindowIconLight, Appearance.WindowIconDark);
            TrayIcons = Pair(Appearance.TrayIconLight, Appearance.TrayIconDark);
            foreach (AvaloniaUiBrandImages icons in new[] { Brand, WindowIcons, TrayIcons }.OfType<AvaloniaUiBrandImages>())
            { _ = icons.ResolveIcon(false); _ = icons.ResolveIcon(true); }
            using (AvaloniaUiBrandImages? splash = Pair(Appearance.SplashIconLight, Appearance.SplashIconDark))
            {
                var source = splash ?? Brand;
                byte[] Png(bool dark)
                {
                    if (source is null) return [];
                    using var output = new MemoryStream();
                    Bitmap bitmap = source.Resolve(dark);
                    double scale = Math.Min(128d / bitmap.PixelSize.Width, 128d / bitmap.PixelSize.Height);
                    using Bitmap scaled = bitmap.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)Math.Round(bitmap.PixelSize.Width * scale)),
                        Math.Max(1, (int)Math.Round(bitmap.PixelSize.Height * scale))), BitmapInterpolationMode.HighQuality);
                    scaled.Save(output, PngBitmapEncoderOptions.Default); return output.ToArray();
                }
                StartupSnapshot = new AvaloniaUiThemeStartupSnapshot(Appearance.Generation, Png(false), Png(true), Appearance.StartupColors, Appearance.SplashMotion).Snapshot();
            }
            IsNativePrepared = true;
        }
        catch { Dispose(); throw; }
    }

    private Bitmap RetainBitmap(Bitmap bitmap)
    {
        long bytes = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
        if (bytes > 64L * 1048576 - DecodedBitmapBytes)
        { bitmap.Dispose(); throw new InvalidDataException("Theme native bitmaps exceed their combined decoded budget."); }
        DecodedBitmapBytes += bytes;
        return bitmap;
    }

    internal static Bitmap Decode(AvaloniaUiThemeResource resource, int maximumWidth = 2048, int maximumHeight = 8192)
    {
        if (resource.MediaType == "image/svg+xml") return AvaloniaUiThemeSvg.Parse(resource.Content).Rasterize();
        using var input = new MemoryStream(resource.Content.ToArray(), writable: false);
        int decodeWidth, decodeHeight;
        bool fitHeight;
        using SKData encoded = SKData.CreateCopy(resource.Content.Span);
        using (SKCodec? codec = SKCodec.Create(encoded))
        {
            if (codec is null || codec.Info.Width is <= 0 or > 8192 || codec.Info.Height is <= 0 or > 8192
                || (long)codec.Info.Width * codec.Info.Height > 32 * 1048576)
                throw new InvalidDataException("Theme raster exceeds its source dimension budget.");
            SKEncodedImageFormat expected = resource.MediaType switch
            {
                "image/png" => SKEncodedImageFormat.Png,
                "image/jpeg" => SKEncodedImageFormat.Jpeg,
                "image/webp" => SKEncodedImageFormat.Webp,
                "image/gif" => SKEncodedImageFormat.Gif,
                "image/bmp" => SKEncodedImageFormat.Bmp,
                "image/x-icon" => SKEncodedImageFormat.Ico,
                _ => throw new InvalidDataException("Unsupported theme raster type."),
            };
            if (expected == SKEncodedImageFormat.Gif && codec.FrameCount > 1)
                throw new InvalidDataException("Animated GIF requires a dedicated animation capability.");
            decodeWidth = Math.Min(maximumWidth, codec.Info.Width);
            decodeHeight = Math.Min(maximumHeight, codec.Info.Height);
            fitHeight = (double)decodeHeight / codec.Info.Height < (double)decodeWidth / codec.Info.Width;
            if (codec.EncodedFormat != expected) throw new InvalidDataException("Theme raster does not match its admitted media type.");
        }
        input.Position = 0;
        // Decode to a bounded target before retaining native surfaces. The admitted byte budget is independent.
        Bitmap bitmap = fitHeight ? Bitmap.DecodeToHeight(input, decodeHeight, BitmapInterpolationMode.HighQuality)
            : Bitmap.DecodeToWidth(input, decodeWidth, BitmapInterpolationMode.HighQuality);
        if (bitmap.PixelSize.Width is <= 0 or > 2048 || bitmap.PixelSize.Height is <= 0 or > 8192
            || (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 16 * 1048576)
        { bitmap.Dispose(); throw new InvalidDataException("Theme raster exceeds its decoded pixel budget."); }
        return bitmap;
    }

    internal bool DrawImage(DrawingContext context, string identity, Rect rect, string fit, double opacity = 1)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        using var fade = context.PushOpacity(Math.Clamp(opacity, 0, 1));
        if (_vectors.TryGetValue(identity, out AvaloniaUiThemeSvg? svg)) { svg.Draw(context, rect, fit); return true; }
        if (!_images.TryGetValue(identity, out Bitmap? bitmap)) return false;
        double iw = bitmap.PixelSize.Width, ih = bitmap.PixelSize.Height;
        Rect source = new(0, 0, iw, ih), destination = rect;
        if (fit is "cover" or "contain")
        {
            double scale = fit == "cover" ? Math.Max(rect.Width / iw, rect.Height / ih) : Math.Min(rect.Width / iw, rect.Height / ih);
            destination = new Rect(rect.X + (rect.Width - iw * scale) / 2, rect.Y + (rect.Height - ih * scale) / 2, iw * scale, ih * scale);
        }
        else if (fit == "tile")
        {
            var brush = new ImageBrush(bitmap)
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.Fill,
                SourceRect = new RelativeRect(0, 0, iw, ih, RelativeUnit.Absolute),
                DestinationRect = new RelativeRect(rect.X, rect.Y, iw, ih, RelativeUnit.Absolute),
            };
            context.DrawRectangle(brush, null, rect);
            return true;
        }
        else if (fit != "stretch") throw new InvalidDataException("Unsupported theme image fit.");
        using var clip = context.PushClip(rect);
        context.DrawImage(bitmap, source, destination);
        return true;
    }

    internal void ReturnBrand(AvaloniaUiBrandImages? brand) { Brand = brand; }
    internal (AvaloniaUiBrandImages? Window, AvaloniaUiBrandImages? Tray) TakeNativeIcons()
    { var result = (WindowIcons, TrayIcons); WindowIcons = null; TrayIcons = null; return result; }
    internal void ReturnNativeIcons(AvaloniaUiBrandImages? window, AvaloniaUiBrandImages? tray) { WindowIcons = window; TrayIcons = tray; }
    internal AvaloniaUiBrandImages? TakeBrand() { AvaloniaUiBrandImages? brand = Brand; Brand = null; return brand; }
    internal void Borrow()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            _nativeBorrows++;
        }
    }
    internal void ReleaseBorrow()
    {
        lock (_lifetimeGate)
        {
            if (_nativeBorrows <= 0) throw new InvalidOperationException("Native resource borrow imbalance.");
            _nativeBorrows--;
            if (IsRetired && _nativeBorrows == 0) Destroy();
        }
    }
    public void Dispose()
    {
        // Owner retirement runs on the activation actor while controls release on the UI
        // thread. Serialize the final release as well as admission to native destruction.
        lock (_lifetimeGate)
        {
            if (IsRetired) return;
            IsRetired = true;
            if (_nativeBorrows == 0) Destroy();
        }
    }
    private void Destroy()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Brand?.Dispose(); Brand = null;
        WindowIcons?.Dispose(); WindowIcons = null; TrayIcons?.Dispose(); TrayIcons = null; StartupSnapshot = null;
        foreach (Bitmap bitmap in _images.Values) bitmap.Dispose();
        _images.Clear(); _vectors.Clear(); DecodedBitmapBytes = 0; TextLayout?.Dispose(); TextLayout = null;
    }
}
