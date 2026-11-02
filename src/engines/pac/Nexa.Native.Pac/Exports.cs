using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexa.Native.Pac;

internal static unsafe class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "nexa_pac_evaluate_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Evaluate(byte* input, int length, nint canceled, nint context, byte* output, int capacity)
    {
        try
        {
            if (canceled == 0) throw new InvalidDataException("Missing PAC cancellation callback.");
            using var reader = new BinaryReader(new MemoryStream(NativeEngineAbi.Input(input, length)));
            string source = NativeEngineAbi.ReadString(reader);
            var destination = new Uri(NativeEngineAbi.ReadString(reader), UriKind.Absolute);
            int count = reader.ReadInt32();
            if (count is < 0 or > 8) throw new InvalidDataException("Invalid PAC DNS cache size.");
            var resolved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                string host = NativeEngineAbi.ReadString(reader), address = NativeEngineAbi.ReadString(reader);
                resolved.Add(host, address.Length == 0 ? null : address);
            }
            NativeEngineAbi.End(reader);
            bool IsCanceled() => ((delegate* unmanaged[Cdecl]<nint, int>)canceled)(context) != 0;
            string result = PacEngine.Evaluate(source, destination, resolved, IsCanceled);
            if (result.Length is 0 or > 4096) throw new InvalidDataException("Invalid PAC result size.");
            return NativeEngineAbi.Output(result, output, capacity, 0);
        }
        catch (PacEngine.LookupRequiredException lookup) { return NativeEngineAbi.Output(lookup.Host, output, capacity, 1); }
        catch (OperationCanceledException) { return NativeEngineAbi.Output("", output, capacity, 2); }
        catch (Exception error) { return NativeEngineAbi.Output(error.Message, output, capacity, -1); }
    }
}
