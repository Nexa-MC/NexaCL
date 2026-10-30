#if NEXA_EXTERNAL_MODULES
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nexa.Native.Interop;

namespace Nexa.Services.Network;

internal static partial class PacEvaluator
{
    private static unsafe class NativePacClient
    {
        private static readonly nint Library = NativeEngineClient.Load("nexa-pac");
        private static readonly delegate* unmanaged[Cdecl]<byte*, int, nint, nint, byte*, int, int> Run =
            (delegate* unmanaged[Cdecl]<byte*, int, nint, nint, byte*, int, int>)NativeLibrary.GetExport(Library, "nexa_pac_evaluate_v1");

        internal static string Evaluate(string source, Uri destination, Dictionary<string, string?> resolved, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var request = new MemoryStream();
            using var writer = new BinaryWriter(request);
            NativeEngineClient.WriteString(writer, source);
            NativeEngineClient.WriteString(writer, destination.AbsoluteUri);
            writer.Write(resolved.Count);
            foreach (var entry in resolved)
            {
                NativeEngineClient.WriteString(writer, entry.Key);
                NativeEngineClient.WriteString(writer, entry.Value ?? "");
            }
            byte[] input = request.ToArray();
            byte[] output = new byte[16 * 1024 + 1];
            GCHandle cancellation = GCHandle.Alloc(token);
            try
            {
                int status;
                fixed (byte* data = input, result = output)
                    status = Run(data, input.Length, (nint)(delegate* unmanaged[Cdecl]<nint, int>)&IsCanceled,
                        GCHandle.ToIntPtr(cancellation), result, output.Length);
                token.ThrowIfCancellationRequested();
                string message = NativeEngineClient.Message(output);
                return status switch
                {
                    0 => message,
                    1 => throw new LookupRequired(message),
                    2 => throw new OperationCanceledException(token),
                    _ => throw new InvalidDataException("PAC engine failed: " + message),
                };
            }
            finally { cancellation.Free(); }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static int IsCanceled(nint context)
        {
            try { return GCHandle.FromIntPtr(context).Target is CancellationToken token && token.IsCancellationRequested ? 1 : 0; }
            catch { return 1; }
        }
    }
}
#endif
