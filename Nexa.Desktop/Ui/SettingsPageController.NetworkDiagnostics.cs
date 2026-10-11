using Nexa.Services.Downloads;
using Nexa.Services.Network;
using Nexa.Services.Resources;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId NetworkTraceRefresh = XsrSemanticId.Parse("ui.settings.network.trace.refresh");
    private static readonly XsrSemanticId NetworkProbeStart = XsrSemanticId.Parse("ui.settings.network.probe.start");
    private Task<XsrResult<IReadOnlyList<NetworkRequestTrace>>>? _networkTraceReading;
    private IReadOnlyList<NetworkRequestTrace> _networkTraceRows = [];
    private bool _networkTraceLoaded;
    private CancellationTokenSource? _networkProbeStop;
    private Task<XsrResult<NetworkManualProbeSnapshot>>? _networkProbeReading;
    private NetworkManualProbeSnapshot? _networkProbeSnapshot;
    private readonly HashSet<XsrUiEntityId> _networkProbeActions = [];
    private Task<XsrResult<ResourceNetworkPolicySnapshot>>? _resourceResolutionReading;
    private ResourceNetworkPolicySnapshot? _resourceResolutionSnapshot;
    private long _resourceResolutionRevision = -1;
    private long _networkDownloadRevision = -1;
    private DateTimeOffset _networkDownloadObservedAt;
    private DownloadTransferView[] _networkDownloadRows = [];

    private void BuildNetworkDiagnostics()
    {
        if (_instanceDirectory is not null || _selected != "network") return;
        _networkProbeActions.Clear();
        var root = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsNetworkDiagnostics.d.nxml"));
        var slots = new Dictionary<string, XsrUiEntityId>(StringComparer.Ordinal);
        _shell.Tree.Walk(root, entity => { slots[_shell.Tree.Name(entity)] = entity; return true; });
        XsrUiEntityId Load(string slot, string fragment) => _dynamic.Append(slots[slot], DesktopMarkup.Dynamic(fragment));
        void Observation(string slot, string value)
        {
            var row = _dynamic.Append(slots[slot], DesktopMarkup.Dynamic("SettingsNetworkObservation.d.nxml", new Dictionary<string, string> { ["value"] = value }));
            DesktopLiteralText.Preserve(_shell.Tree, row);
        }
        void Endpoint(string slot, string host, DateTimeOffset at, int? status, double elapsed, string? error) => Observation(slot,
            host + " · " + at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            + " · " + (status?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? error ?? "")
            + " · " + elapsed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms");
        var start = Load("NetworkProbe.Action", _networkProbeReading is null ? "SettingsNetworkProbeStart.d.nxml" : "SettingsNetworkProbePending.d.nxml");
        _shell.Tree.GetComponent<XsrUiInput>(start)!.Enabled = _networkProbeReading is null;
        _networkProbeActions.Add(start);
        if (_networkProbeSnapshot is { } probed)
            foreach (var observed in probed.Endpoints) Endpoint("NetworkProbe.Rows", observed.Host, observed.ObservedAt, observed.StatusCode, observed.ElapsedMilliseconds, observed.ErrorKind);
        foreach (var observed in _networkTraceRows.GroupBy(entry => entry.Host, StringComparer.Ordinal).Select(group => group.MaxBy(entry => entry.Sequence)!).Take(16))
            Endpoint("NetworkObservedEndpoints.Rows", observed.Host, observed.Timestamp, observed.StatusCode, observed.ElapsedMilliseconds, observed.ErrorKind);
        if (_networkTraceRows.Count == 0) Load("NetworkObservedEndpoints.Rows", "SettingsNetworkEndpointsEmpty.d.nxml");
        ReadResourceResolution();
        string source = _values?.Values.FirstOrDefault(item => item.Key == "network.resource-source")?.Value.Value ?? "follow-request";
        Load("NetworkMirrorResolution.Policy", source switch
        { "official-first" => "SettingsNetworkOfficialFirst.d.nxml", "mirrors-first" => "SettingsNetworkMirrorsFirst.d.nxml", _ => "SettingsNetworkFollowRequest.d.nxml" });
        if (_resourceResolutionSnapshot?.LastResolution is { } resolution)
            Observation("NetworkMirrorResolution.Rows", resolution.Provider + " · " + resolution.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                + " · " + string.Join(" → ", resolution.CandidateHosts) + " · " + (resolution.SelectedHost ?? "—"));
        else Load("NetworkMirrorResolution.Rows", "SettingsNetworkResolutionEmpty.d.nxml");
        if (_networkDownloadRows.Length == 0) Load("NetworkDownloadDiagnostics.Rows", "SettingsNetworkDownloadsEmpty.d.nxml");
        foreach (var transfer in _networkDownloadRows)
        {
            string host = Uri.TryCreate(transfer.Source, UriKind.Absolute, out var address) && address.Scheme is "https" or "http" ? address.IdnHost : "—";
            Observation("NetworkDownloadDiagnostics.Rows", _networkDownloadObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                + " · " + host + " · " + transfer.Stage + " · " + transfer.DownloadedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "/" + (transfer.TotalBytes >= 0 ? transfer.TotalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—")
                + " B · " + transfer.BytesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B/s");
        }
        foreach (var entry in _networkTraceRows.Reverse().Take(32))
            Observation("NetworkDiagnostics.Rows", $"{entry.Timestamp.ToLocalTime():HH:mm:ss} · {entry.Host} · {entry.Kind} · "
                + (entry.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? entry.ErrorKind ?? "")
                + $" · {entry.ElapsedMilliseconds:F1} ms");
        if (_networkTraceLoaded && _networkTraceRows.Count == 0) Load("NetworkDiagnostics.Rows", "SettingsNetworkTraceEmpty.d.nxml");
    }

    private void StartNetworkProbe(DesktopUiIntent intent)
    {
        if (!_visible || _disposed || _instanceDirectory is not null || _selected != "network" || _networkProbeReading is not null
            || !_networkProbeActions.Contains(intent.Source) || !_shell.Tree.IsAlive(intent.Source)
            || _shell.Tree.GetComponent<XsrUiInput>(intent.Source)?.Enabled != true
            || !_queries.TryResolve(NetworkDiagnosticsContract.Probe, out var route)) return;
        _networkProbeStop = new();
        _networkProbeReading = _queries.QueryAsync<NetworkManualProbeQuery, NetworkManualProbeSnapshot>(route, new(), cancellationToken: _networkProbeStop.Token).AsTask();
        WakeOnPlatformCompletion(_networkProbeReading);
        BuildSections();
    }

    private void CancelNetworkProbe()
    {
        _networkProbeStop?.Cancel(); _networkProbeStop?.Dispose(); _networkProbeStop = null;
        _networkProbeReading = null; _networkProbeActions.Clear();
    }

    private void ReadNetworkDiagnostics()
    {
        if (!_visible || _instanceDirectory is not null || _selected != "network" || _networkTraceReading is not null) return;
        if (_queries.TryResolve(NetworkDiagnosticsContract.Trace, out var route))
        {
            _networkTraceReading = _queries.QueryAsync<NetworkTraceQuery, IReadOnlyList<NetworkRequestTrace>>(route, new()).AsTask();
            WakeOnPlatformCompletion(_networkTraceReading);
        }
        _resourceResolutionRevision = -1; ReadResourceResolution();
    }
    private void ReadResourceResolution()
    {
        if (_resourceResolutionReading is not null || _resourceResolutionRevision == (_values?.Revision ?? 0)
            || _resourceQueries is null || !_resourceQueries.TryResolve(ResourceCatalogContract.NetworkPolicy, out var route)) return;
        _resourceResolutionRevision = _values?.Revision ?? 0;
        _resourceResolutionReading = _resourceQueries.QueryAsync<ResourceNetworkPolicyQuery, ResourceNetworkPolicySnapshot>(route, new()).AsTask();
        WakeOnPlatformCompletion(_resourceResolutionReading);
    }

    private void UpdateNetworkDiagnostics()
    {
        if (!_visible || _instanceDirectory is not null || _selected != "network") { CancelNetworkProbe(); return; }
        if (_store.TryResolve(DownloadStateContract.TransfersKey, out var transfers))
        {
            var active = _store.ReadCollection<DownloadTransferView>(transfers);
            if (active.Revision != _networkDownloadRevision)
            {
                _networkDownloadRevision = active.Revision;
                _networkDownloadObservedAt = DateTimeOffset.UtcNow;
                _networkDownloadRows = active.Items.Take(16).ToArray();
                if (_catalog is not null) BuildSections();
            }
        }
        if (_resourceResolutionReading is { IsCompleted: true } resolutionRead)
        {
            _resourceResolutionReading = null;
            if (resolutionRead.IsCompletedSuccessfully && resolutionRead.Result.IsSuccess) _resourceResolutionSnapshot = resolutionRead.Result.Value!;
            if (_catalog is not null) BuildSections();
        }
        if (_networkProbeReading is { IsCompleted: true } probe)
        {
            _networkProbeReading = null; _networkProbeStop?.Dispose(); _networkProbeStop = null;
            if (probe.IsCompletedSuccessfully && probe.Result.IsSuccess) _networkProbeSnapshot = probe.Result.Value!;
            else if (!probe.IsCanceled) _feedback.Error("网络探测未完成。");
            if (_catalog is not null) BuildSections();
        }
        if (_networkTraceReading is not { IsCompleted: true } read) return;
        _networkTraceReading = null;
        if (!_visible || _selected != "network") return;
        if (read.IsCompletedSuccessfully && read.Result.IsSuccess) _networkTraceRows = read.Result.Value!;
        else _feedback.Error("无法读取网络诊断记录。");
        _networkTraceLoaded = true;
        if (_catalog is not null) BuildSections();
    }
}
