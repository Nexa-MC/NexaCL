using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Nexa.Native.Network;

/// <summary>Duplicate the OS handle so the originating runtime can unregister its asynchronous I/O context.</summary>
internal static partial class SocketTransfer
{
    internal static unsafe SafeSocketHandle Duplicate(Socket socket)
    {
        if (!OperatingSystem.IsWindows())
        {
            // The runtime's Unix portability shim uses F_DUPFD_CLOEXEC on Linux and macOS.
            int descriptor = Dup(socket.SafeHandle);
            if (descriptor < 0) throw new IOException("Could not transfer the connected socket.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
            return new SafeSocketHandle(descriptor, ownsHandle: true);
        }
        // The Windows connection must never have been IOCP-bound in this runtime; duplication cannot detach it.
        // WSAPROTOCOL_INFOW contains no pointer-sized fields and is 628 bytes on x64 and ARM64.
        byte* info = stackalloc byte[628];
        if (DuplicateWindows(socket.SafeHandle, (uint)Environment.ProcessId, info) != 0)
            throw new SocketException(Marshal.GetLastPInvokeError());
        nint handle = CreateWindows(-1, -1, -1, info, 0, 0x01 | 0x80); // OVERLAPPED | NO_HANDLE_INHERIT
        if (handle == -1) throw new SocketException(Marshal.GetLastPInvokeError());
        return new SafeSocketHandle(handle, ownsHandle: true);
    }

    [LibraryImport("System.Native", EntryPoint = "SystemNative_Dup", SetLastError = true)]
    private static partial int Dup(SafeSocketHandle socket);
    [LibraryImport("ws2_32.dll", EntryPoint = "WSADuplicateSocketW", SetLastError = true)]
    private static unsafe partial int DuplicateWindows(SafeSocketHandle socket, uint process, byte* info);
    [LibraryImport("ws2_32.dll", EntryPoint = "WSASocketW", SetLastError = true)]
    private static unsafe partial nint CreateWindows(int family, int type, int protocol, byte* info, uint group, uint flags);
}
