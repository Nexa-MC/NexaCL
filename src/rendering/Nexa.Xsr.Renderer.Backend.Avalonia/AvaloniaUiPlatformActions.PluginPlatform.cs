using System.Buffers.Binary;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiPlatformActions
{
    public Task<(string? Text, byte[]? Image)> ReadExtensionClipboardAsync(CancellationToken token = default) => Dispatcher.UIThread.CheckAccess()
        ? ReadExtensionClipboardOnUiAsync(token) : Dispatcher.UIThread.InvokeAsync(() => ReadExtensionClipboardOnUiAsync(token));
    private async Task<(string? Text, byte[]? Image)> ReadExtensionClipboardOnUiAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var clipboard = _owner?.Clipboard ?? throw new InvalidOperationException("The native clipboard is not ready.");
        using var data = await clipboard.TryGetDataAsync().ConfigureAwait(true);
        if (data is null) return (null, null);
        string? text = await data.TryGetTextAsync().ConfigureAwait(true);
        if (text?.Length > 65536) throw new InvalidDataException("Clipboard text exceeds its budget.");
        using var bitmap = await data.TryGetBitmapAsync().ConfigureAwait(true);
        byte[]? image = null;
        if (bitmap is not null)
        {
            if (bitmap.PixelSize.Width is < 1 or > 4096 || bitmap.PixelSize.Height is < 1 or > 4096 || (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 4 * 1024 * 1024)
                throw new InvalidDataException("Clipboard image exceeds its pixel budget.");
            using var output = new MemoryStream(); bitmap.Save(output, PngBitmapEncoderOptions.Default);
            if (output.Length > 16 * 1024 * 1024) throw new InvalidDataException("Clipboard PNG exceeds its byte budget.");
            image = output.ToArray();
        }
        token.ThrowIfCancellationRequested(); return (text, image);
    }
    public Task WriteExtensionClipboardAsync(string? text, byte[]? image, CancellationToken token = default) => Dispatcher.UIThread.CheckAccess()
        ? WriteExtensionClipboardOnUiAsync(text, image, token) : Dispatcher.UIThread.InvokeAsync(() => WriteExtensionClipboardOnUiAsync(text, image, token));
    private async Task WriteExtensionClipboardOnUiAsync(string? text, byte[]? image, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (text?.Length > 65536) throw new InvalidDataException("Clipboard text exceeds its budget.");
        var clipboard = _owner?.Clipboard ?? throw new InvalidOperationException("The native clipboard is not ready."); Bitmap? bitmap = null;
        if (image is not null)
        {
            if (image.Length is < 33 or > 16 * 1024 * 1024 || !image.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || !image.AsSpan(12, 4).SequenceEqual("IHDR"u8))
                throw new InvalidDataException("Clipboard image must be a bounded PNG.");
            uint width = BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(16, 4)), height = BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(20, 4));
            if (width is 0 or > 4096 || height is 0 or > 4096 || (ulong)width * height > 4 * 1024 * 1024) throw new InvalidDataException("Clipboard image exceeds its pixel budget.");
            using var input = new MemoryStream(image, false); bitmap = new(input);
        }
        try
        {
            if (text is null && bitmap is null) await clipboard.ClearAsync().ConfigureAwait(true);
            else
            {
                var item = new DataTransferItem();
                if (text is not null) item.SetText(text);
                if (bitmap is not null) item.SetBitmap(bitmap);
                var data = new DataTransfer(); data.Add(item);
                await clipboard.SetDataAsync(data).ConfigureAwait(true);
            }
            var previous = _clipboardScreenshot; _clipboardScreenshot = bitmap; previous?.Dispose();
            try { await clipboard.FlushAsync().ConfigureAwait(true); } catch (NotSupportedException) { }
        }
        catch { if (!ReferenceEquals(_clipboardScreenshot, bitmap)) bitmap?.Dispose(); throw; }
    }
    public async Task<(string Id, int Width, int Height, double Scale)[]> ReadExtensionDisplaysAsync(CancellationToken token = default) => await Dispatcher.UIThread.InvokeAsync(() =>
    {
        token.ThrowIfCancellationRequested(); var screens = _owner?.Screens ?? throw new InvalidOperationException("The native screens are not ready.");
        return screens.All.Take(32).Select((s, i) => (i.ToString(System.Globalization.CultureInfo.InvariantCulture), s.Bounds.Width, s.Bounds.Height, s.Scaling)).ToArray();
    });
}
