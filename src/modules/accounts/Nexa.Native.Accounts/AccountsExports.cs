using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Nexa.Native.Accounts.Wire;
using Nexa.Native.Ui;

namespace Nexa.Native.Accounts;

internal static class AccountsExports
{
    private sealed class Entry(AccountsSession session)
    { internal AccountsSession Session { get; } = session; internal Task? Retirement; }
    private static readonly ConcurrentDictionary<long, Entry> Sessions = new();
    private static readonly byte[] IdentityBytes = Encoding.UTF8.GetBytes(AccountProtocol.Identity);
    private static readonly object Admission = new();

    [UnmanagedCallersOnly(EntryPoint = "nexa_accounts_identity_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Identity(byte* output, int capacity)
    {
        if (capacity < 0 || output == null && capacity != 0) return -1;
        if (output != null && capacity >= IdentityBytes.Length) IdentityBytes.CopyTo(new Span<byte>(output, IdentityBytes.Length));
        return IdentityBytes.Length;
    }
    [UnmanagedCallersOnly(EntryPoint = "nexa_accounts_open_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Open(NativeUiCallTable* peer, NativeUiCallTable* output)
    {
        if (peer == null || output == null) return -1;
        try
        {
            _ = new NativeUiCallClient(*peer);
            lock (Admission)
            {
                if (Sessions.Count >= 32) return -1;
                var session = new AccountsSession(*peer); var table = session.Table;
                if (!Sessions.TryAdd(table.Context, new(session))) throw new InvalidOperationException();
                *output = table; return 0;
            }
        }
        catch { return -1; }
    }
    [UnmanagedCallersOnly(EntryPoint = "nexa_accounts_close_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Close(long context)
    {
        try
        {
            if (!Sessions.TryGetValue(context, out var entry)) return 1;
            lock (entry)
            {
                entry.Retirement ??= Task.Run(async () => await entry.Session.DisposeAsync().ConfigureAwait(false));
                if (!entry.Retirement.IsCompleted) return 0;
                if (entry.Retirement.IsFaulted) { _ = entry.Retirement.Exception; return -1; }
                Sessions.TryRemove(context, out _); return 1;
            }
        }
        catch { return -1; }
    }
}
