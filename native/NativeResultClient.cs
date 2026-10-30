#if NEXA_EXTERNAL_MODULES
using System.Runtime.InteropServices;

namespace Nexa.Native.Interop;

internal sealed unsafe class NativeResultClient
{
    private readonly nint _library;
    private readonly int _inputLimit, _outputLimit;
    private readonly delegate* unmanaged[Cdecl]<byte*, int, int*, long> _execute;
    private readonly delegate* unmanaged[Cdecl]<long, byte*, int, int> _copy;
    private readonly delegate* unmanaged[Cdecl]<long, void> _release;

    internal NativeResultClient(string module, string prefix, int inputLimit, int outputLimit)
    {
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        _library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, module + suffix));
        _execute = (delegate* unmanaged[Cdecl]<byte*, int, int*, long>)NativeLibrary.GetExport(_library, prefix + "_execute_v1");
        _copy = (delegate* unmanaged[Cdecl]<long, byte*, int, int>)NativeLibrary.GetExport(_library, prefix + "_copy_v1");
        _release = (delegate* unmanaged[Cdecl]<long, void>)NativeLibrary.GetExport(_library, prefix + "_release_v1");
        _inputLimit = inputLimit; _outputLimit = outputLimit;
    }
    internal byte[] Execute(ReadOnlySpan<byte> request)
    {
        if (request.Length > _inputLimit) throw new InvalidDataException("Native request exceeds its wire budget.");
        int length = 0;
        long id;
        fixed (byte* pointer = request) id = _execute(pointer, request.Length, &length);
        if (id == 0) throw new InvalidDataException("Native engine rejected the request.");
        try
        {
            if (length < 1 || length > _outputLimit) throw new InvalidDataException("Invalid native response length.");
            byte[] output = new byte[length];
            fixed (byte* pointer = output)
                if (_copy(id, pointer, length) != 0) throw new InvalidDataException("Native result is unavailable.");
            return output;
        }
        finally { _release(id); }
    }
}
#endif
