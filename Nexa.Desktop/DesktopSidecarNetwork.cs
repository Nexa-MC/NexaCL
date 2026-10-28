using Nexa.Services.Composition;
using Nexa.Services.Minecraft.Process;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop;

/// <summary>Explicit identity/profile grants; no account or progression rules live here.</summary>
internal sealed class DesktopSidecarNetwork : IAsyncDisposable
{
    private readonly Lazy<CloudflareApiClient?> _identity;
    private readonly SidecarAuthenticatedNetworkPump _pump;
    private readonly HashSet<Guid> _seen = [];
    private uint _starts;
    internal DesktopSidecarNetwork(SidecarSupervisor supervisor, XsrStateStore state)
    {
        _identity = new(() => CloudflareApiClient.TryCreate(() => typeof(Program).Assembly.GetManifestResourceStream("Nexa.Desktop.Assets.api-client.pfx")));
        _pump = new(supervisor, "Nexa.Cloud.Market.Sidecar", "nexa-account-activity",
            new Uri("https://pcln.top/api/v1/launcher/activity-events"), () => _identity.Value?.Client, () => Sample(state));
    }
    private byte[] Sample(XsrStateStore state)
    {
        uint active = 0;
        if (state.TryResolve(MinecraftProcessStateComposition.SessionsKey, out var id))
        {
            var snapshots = state.ReadCollection<MinecraftProcessSnapshot>(id).Items;
            var present = snapshots.Select(p => p.SessionId).ToHashSet();
            _seen.IntersectWith(present);
            foreach (var process in snapshots.Where(p => p.ProcessId > 0))
            {
                if (_seen.Add(process.SessionId)) _starts++;
                if (process.State is MinecraftProcessState.Created or MinecraftProcessState.Running) active++;
            }
        }
        using var payload = new SidecarPayloadWriter();
        payload.WriteUInt32(1, active); payload.WriteUInt32(2, _starts); return payload.ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        await _pump.DisposeAsync().ConfigureAwait(false);
        if (_identity.IsValueCreated) _identity.Value?.Dispose();
    }
}
