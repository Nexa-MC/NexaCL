using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexa.Native.Media;

internal static unsafe class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "nexa_media_inspect_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Inspect(byte* input, int length, int mode, int slim, int* width, int* height, byte* error, int capacity)
    {
        if (width == null || height == null) return -1;
        *width = 0; *height = 0;
        if (error != null && capacity > 0) error[0] = 0;
        try
        {
            if (mode is < 0 or > 5 || slim is < 0 or > 1 || length < 0
                || length > (mode == 1 ? 16 : 1) * 1_048_576 || (length > 0 && input == null))
                throw new ArgumentException("Invalid media engine input.");
            ReadOnlySpan<byte> bytes = new(input, length);
            PngImage? image = mode switch
            {
                0 => PngImage.TryCreate(bytes),
                1 => PngImage.TryCreatePreview(bytes),
                2 => PngImage.TryCreateResourceIcon(bytes),
                3 => WardrobeSkinValidator.Validate(bytes.ToArray(), slim != 0),
                4 => WardrobeSkinValidator.ValidateTexture(bytes.ToArray(), AccountWardrobeTextureKind.Skin, slim != 0),
                _ => WardrobeSkinValidator.ValidateTexture(bytes.ToArray(), AccountWardrobeTextureKind.Cape),
            };
            if (image is null) return NativeEngineAbi.Output("Unsupported or malformed image.", error, capacity, 1);
            *width = image.Width; *height = image.Height;
            return 0;
        }
        catch (InvalidDataException failure) { return NativeEngineAbi.Output(failure.Message, error, capacity, 1); }
        catch (Exception failure) { return NativeEngineAbi.Output(failure.Message, error, capacity, -1); }
    }
}
