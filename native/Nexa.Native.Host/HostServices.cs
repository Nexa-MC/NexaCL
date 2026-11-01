using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Nexa.Native.Host.Services;
using Nexa.Native.Ui;

namespace Nexa.Native.Host;

internal static class HostServices
{
    private sealed record Module(nint Open, nint Close);
    private sealed class Lease(int module, long context, nint close, nint retirement, long client)
    {
        internal readonly int Module = module;
        internal readonly long Context = context, Client = client;
        internal readonly nint Close = close, Retirement = retirement;
        internal bool OwnerClosed;
    }
    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<long, Lease> Leases = new();
    private static readonly Dictionary<int, Module> Modules = [];
    private static string? _directory;
    private static bool _closing;
    private static long _next;
    private static Task? _retirement;
    internal static int Count => Leases.Count;

    internal static void Configure(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Host admission requires an absolute installation directory.");
        string canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(_closing, typeof(HostServices));
            if (_directory is not null && !string.Equals(_directory, canonical, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("Host installation admission is immutable.");
            _directory = canonical;
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", canonical);
        }
    }

    internal static unsafe NativeUiCallTable Open(int moduleId, ReadOnlySpan<byte> identity, NativeUiCallTable peer, nint retirement, long client, out long lease)
    {
        lease = 0;
        _ = new NativeUiCallClient(peer);
        var descriptor = HostServiceCatalog.Get(moduleId);
        if (!identity.SequenceEqual(Encoding.UTF8.GetBytes(descriptor.Identity)) || retirement == 0 || client <= 0)
            throw new InvalidDataException("Incompatible Host service admission.");
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(_closing, typeof(HostServices));
            if (_directory is null) throw new InvalidOperationException("Host installation is not configured.");
            if (Leases.Count >= 128) throw new IOException("Host service lease budget exceeded.");
            if (!Modules.TryGetValue(moduleId, out var module))
            {
                string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
                nint image = NativeLibrary.Load(Path.Combine(_directory, "nexa-" + descriptor.Name + suffix));
                var readIdentity = (delegate* unmanaged[Cdecl]<byte*, int, int>)NativeLibrary.GetExport(image, descriptor.Prefix + "_identity_v1");
                nint open = NativeLibrary.GetExport(image, descriptor.Prefix + "_open_v1"), close = NativeLibrary.GetExport(image, descriptor.Prefix + "_close_v1");
                byte[] expected = Encoding.UTF8.GetBytes(descriptor.Identity), actual = new byte[expected.Length];
                if (readIdentity(null, 0) != actual.Length) throw new InvalidDataException("Incompatible native " + descriptor.Name + " identity.");
                fixed (byte* output = actual)
                    if (readIdentity(output, actual.Length) != actual.Length || !actual.AsSpan().SequenceEqual(expected))
                        throw new InvalidDataException("Incompatible native " + descriptor.Name + " identity.");
                module = new(open, close); Modules.Add(moduleId, module);
            }
            long id = checked(++_next);
            NativeUiCallTable owner = default;
            if (((delegate* unmanaged[Cdecl]<NativeUiCallTable*, NativeUiCallTable*, int>)module.Open)(&peer, &owner) != 0)
                throw new IOException("Native " + descriptor.Name + " service admission failed.");
            // IDs belong to Host; module contexts can overlap between independent runtimes.
            Leases[id] = new(moduleId, owner.Context, module.Close, retirement, client);
            lease = id; return owner;
        }
    }

    internal static unsafe int CloseOwner(long id)
    {
        if (!Leases.TryGetValue(id, out var lease)) return 1;
        lock (lease)
        {
            if (lease.OwnerClosed) return 1;
            int status = ((delegate* unmanaged[Cdecl]<long, int>)lease.Close)(lease.Context);
            if (status == 1) lease.OwnerClosed = true;
            return status;
        }
    }
    internal static void Complete(long id)
    {
        if (!Leases.TryGetValue(id, out var lease)) return;
        lock (lease)
        {
            if (!lease.OwnerClosed) throw new InvalidOperationException("A Host lease cannot retire before its owner.");
            Leases.TryRemove(id, out _);
        }
    }
    internal static Task ShutdownAsync()
    {
        lock (Gate)
        {
            _closing = true;
            return _retirement ??= Task.Run(RetireAsync);
        }
    }
    private static unsafe int PollClient(Lease lease) => ((delegate* unmanaged[Cdecl]<long, int>)lease.Retirement)(lease.Client);
    private static async Task RetireAsync()
    {
        var remaining = Leases.Values.OrderBy(static entry => entry.Module switch { 4 => 0, 3 => 1, 1 => 2, _ => 3 }).ToArray();
        foreach (var lease in remaining)
        {
            int delay = 1;
            while (true)
            {
                int status = PollClient(lease);
                if (status == 1) break;
                if (status != 0) throw new IOException("A native service client failed physical retirement; handles remain retained.");
                await Task.Delay(delay).ConfigureAwait(false); delay = Math.Min(delay * 2, 32);
            }
        }
        if (!Leases.IsEmpty) throw new IOException("Native service clients did not acknowledge physical retirement.");
    }
}
