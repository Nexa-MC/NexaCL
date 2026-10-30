#if NEXA_EXTERNAL_MODULES
using System.Runtime.InteropServices;
using Nexa.Native.Interop;

namespace Nexa.Services.Updates;

internal static class NativeCryptoClient
{
    internal static async Task VerifyAsync(string key, string fingerprint, Stream content, Stream signature, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var envelope = new MemoryStream();
        byte[] buffer = new byte[128 * 1024];
        while (true)
        {
            int read = await signature.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) break;
            if (read > 1024 * 1024 - envelope.Length) throw new InvalidDataException("GPG 签名封套超过大小限制。");
            envelope.Write(buffer, 0, read);
        }
        using var session = new Session(key, fingerprint, envelope.ToArray());
        token.ThrowIfCancellationRequested();
        while (true)
        {
            int read = await content.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) break;
            session.Update(buffer.AsSpan(0, read));
        }
        token.ThrowIfCancellationRequested();
        session.Finish();
        token.ThrowIfCancellationRequested();
    }

    private sealed unsafe class Session : IDisposable
    {
        private static readonly nint Library = NativeEngineClient.Load("nexa-crypto");
        private static readonly delegate* unmanaged[Cdecl]<byte*, int, nint*, byte*, int, int> Create =
            (delegate* unmanaged[Cdecl]<byte*, int, nint*, byte*, int, int>)NativeLibrary.GetExport(Library, "nexa_crypto_create_v1");
        private static readonly delegate* unmanaged[Cdecl]<nint, byte*, int, byte*, int, int> Feed =
            (delegate* unmanaged[Cdecl]<nint, byte*, int, byte*, int, int>)NativeLibrary.GetExport(Library, "nexa_crypto_update_v1");
        private static readonly delegate* unmanaged[Cdecl]<nint, byte*, int, int> Complete =
            (delegate* unmanaged[Cdecl]<nint, byte*, int, int>)NativeLibrary.GetExport(Library, "nexa_crypto_finish_v1");
        private static readonly delegate* unmanaged[Cdecl]<nint, void> Release =
            (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(Library, "nexa_crypto_release_v1");
        private nint _handle;

        internal Session(string key, string fingerprint, byte[] envelope)
        {
            using var request = new MemoryStream();
            using var writer = new BinaryWriter(request);
            NativeEngineClient.WriteString(writer, key);
            NativeEngineClient.WriteString(writer, fingerprint);
            writer.Write(envelope.Length); writer.Write(envelope);
            byte[] input = request.ToArray();
            Span<byte> error = stackalloc byte[4096];
            nint handle = 0;
            fixed (byte* data = input, message = error)
                Check(Create(data, input.Length, &handle, message, error.Length), error);
            _handle = handle;
        }

        internal void Update(ReadOnlySpan<byte> input)
        {
            Span<byte> error = stackalloc byte[4096];
            fixed (byte* data = input, message = error)
                Check(Feed(_handle, data, input.Length, message, error.Length), error);
        }

        internal void Finish()
        {
            Span<byte> error = stackalloc byte[4096];
            fixed (byte* message = error) Check(Complete(_handle, message, error.Length), error);
        }

        private static void Check(int status, ReadOnlySpan<byte> message)
        {
            if (status != 0) throw new InvalidDataException("GPG 签名或公钥数据无效。" + NativeEngineClient.Message(message));
        }

        public void Dispose()
        {
            if (_handle != 0) { Release(_handle); _handle = 0; }
        }
    }
}
#endif
