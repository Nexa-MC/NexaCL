using Nexa.Services.Downloads;
using Nexa.Services.Network;
using Nexa.Services.Resources;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId NetworkTraceRefresh = XsrSemanticId.Parse("ui.settings.network.trace.refresh");
    private static readonly XsrSemanticId NetworkProbeStart = XsrSemanticId.Parse("ui.settings.network.probe.start");
    private Task<XsrResult<IReadOnlyList<NetworkRequestTrace>>>? _networkTraceReading;
    private XsrUiEntityId _networkDiagnosticsWorkspace, _networkProbeButton;
    private readonly Dictionary<string, List<XsrUiEntityId>> _networkDiagnosticViews = new(StringComparer.Ordinal);
    private CancellationTokenSource? _networkDiagnosticsStop;
    private long _resourceResolutionReadingRevision = -1;
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
        _networkProbeActions.Clear(); _networkDiagnosticViews.Clear(); _networkProbeButton = default;
        _networkDiagnosticsWorkspace = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsNetworkDiagnostics.d.nxml",
            new Dictionary<string, string> { ["mirrors"] = Flag(_region.IsMainlandChina) }));
        RefreshNetworkDiagnostics();
    }

    private void RefreshNetworkDiagnostics()
    {
        if (!_visible || _selected != "network" || !_shell.Tree.IsAlive(_networkDiagnosticsWorkspace)) return;
        var slots = CatalogNodes(_networkDiagnosticsWorkspace);
        var action = DesktopMarkup.Dynamic("SettingsCustomNetworkProbe.d.nxml", new Dictionary<string, string>
        {
            ["caption"] = CustomCopy(_networkProbeReading is null ? "network.probeStart" : "network.probePending"),
            ["enabled"] = Flag(_networkProbeReading is null),
        });
        if (_shell.Tree.IsAlive(_networkProbeButton)) _dynamic.Reload(_networkProbeButton, action);
        else _networkProbeButton = _dynamic.Append(slots["NetworkProbe.Action"], action);
        _networkProbeActions.Add(_networkProbeButton);
        var probes = _networkProbeSnapshot?.Endpoints.Select(observed => NetworkEndpointFragment(observed.Host, observed.ObservedAt,
            observed.StatusCode, observed.ElapsedMilliseconds, observed.ErrorKind)).ToArray() ?? [];
        RefreshNetworkRows("NetworkProbe.Rows", probes);
        var observations = _networkTraceRows.GroupBy(entry => entry.Host, StringComparer.Ordinal).Select(group => group.MaxBy(entry => entry.Sequence)!).Take(16)
            .Select(observed => NetworkEndpointFragment(observed.Host, observed.Timestamp, observed.StatusCode, observed.ElapsedMilliseconds, observed.ErrorKind)).ToArray();
        RefreshNetworkRows("NetworkObservedEndpoints.Rows", observations.Length == 0 ? [DesktopMarkup.Dynamic("SettingsNetworkEndpointsEmpty.d.nxml")] : observations);
        ReadResourceResolution();
        if (_region.IsMainlandChina)
        {
            var resolution = _resourceResolutionSnapshot?.LastResolution;
            RefreshNetworkRows("NetworkMirrorResolution.Rows", [resolution is null ? DesktopMarkup.Dynamic("SettingsNetworkResolutionEmpty.d.nxml")
                : DesktopMarkup.Dynamic("SettingsCustomNetworkResolution.d.nxml", new Dictionary<string, string>
                {
                    ["provider"] = resolution.Provider.ToString(), ["timestamp"] = NetworkTimestamp(resolution.ObservedAt),
                    ["candidates"] = string.Join(CustomRaw("network.candidateSeparator"), resolution.CandidateHosts),
                    ["selected"] = resolution.SelectedHost ?? CustomCopy("network.unknown"),
                }, _shell.Renderer.LocalizeText)]);
        }
        var transfers = _networkDownloadRows.Select(transfer => DesktopMarkup.Dynamic("SettingsCustomNetworkDownload.d.nxml", new Dictionary<string, string>
        {
            ["timestamp"] = NetworkTimestamp(_networkDownloadObservedAt),
            ["host"] = Uri.TryCreate(transfer.Source, UriKind.Absolute, out var address) && address.Scheme is "https" or "http" ? address.IdnHost : CustomCopy("network.unknown"),
            ["stage"] = transfer.Stage.ToString(),
            ["downloaded"] = transfer.DownloadedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["total"] = transfer.TotalBytes >= 0 ? transfer.TotalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) : CustomCopy("network.unknown"),
            ["speed"] = transfer.BytesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, _shell.Renderer.LocalizeText)).ToArray();
        RefreshNetworkRows("NetworkDownloadDiagnostics.Rows", transfers.Length == 0 ? [DesktopMarkup.Dynamic("SettingsNetworkDownloadsEmpty.d.nxml")] : transfers);
        var trace = _networkTraceRows.Reverse().Take(32).Select(entry => DesktopMarkup.Dynamic("SettingsCustomNetworkTrace.d.nxml", new Dictionary<string, string>
        {
            ["timestamp"] = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture),
            ["host"] = entry.Host,
            ["kind"] = entry.Kind,
            ["status"] = entry.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? entry.ErrorKind ?? "",
            ["elapsed"] = entry.ElapsedMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
        }, _shell.Renderer.LocalizeText)).ToArray();
        RefreshNetworkRows("NetworkDiagnostics.Rows", _networkTraceLoaded && trace.Length == 0 ? [DesktopMarkup.Dynamic("SettingsNetworkTraceEmpty.d.nxml")] : trace);
    }

    private static string NetworkTimestamp(DateTimeOffset timestamp) => timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    private Nexa.Xsr.Renderer.Nxml.NxmlHostIr NetworkEndpointFragment(string host, DateTimeOffset at, int? status, double elapsed, string? error)
        => DesktopMarkup.Dynamic("SettingsCustomNetworkEndpoint.d.nxml", new Dictionary<string, string>
        {
            ["host"] = host,
            ["timestamp"] = NetworkTimestamp(at),
            ["status"] = status?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? error ?? "",
            ["elapsed"] = elapsed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
        }, _shell.Renderer.LocalizeText);

    private void RefreshNetworkRows(string slot, Nexa.Xsr.Renderer.Nxml.NxmlHostIr[] fragments)
    {
        if (!_networkDiagnosticViews.TryGetValue(slot, out var rows)) _networkDiagnosticViews[slot] = rows = [];
        while (rows.Count > fragments.Length) { _dynamic.Remove(rows[^1]); rows.RemoveAt(rows.Count - 1); }
        var target = CatalogNodes(_networkDiagnosticsWorkspace)[slot];
        for (int index = 0; index < fragments.Length; index++)
        {
            if (index < rows.Count) _dynamic.Reload(rows[index], fragments[index]);
            else rows.Add(_dynamic.Append(target, fragments[index]));
        }
    }

    private void StartNetworkProbe(DesktopUiIntent intent)
    {
        if (!_visible || _disposed || _instanceDirectory is not null || _selected != "network" || _networkProbeReading is not null
            || !_networkProbeActions.Contains(intent.Source) || !_shell.Tree.IsAlive(intent.Source)
            || _shell.Tree.GetComponent<XsrUiInput>(intent.Source)?.Enabled != true
            || !_queries.TryResolve(NetworkDiagnosticsContract.Probe, out var route)) return;
        _networkDiagnosticsStop ??= new();
        _networkProbeStop = CancellationTokenSource.CreateLinkedTokenSource(_networkDiagnosticsStop.Token);
        _networkProbeReading = _queries.QueryAsync<NetworkManualProbeQuery, NetworkManualProbeSnapshot>(route, new(), cancellationToken: _networkProbeStop.Token).AsTask();
        WakeOnPlatformCompletion(_networkProbeReading);
        RefreshNetworkDiagnostics();
    }

    private void CancelNetworkProbe()
    {
        _networkProbeStop?.Cancel(); _networkProbeStop?.Dispose(); _networkProbeStop = null;
        if (_networkProbeReading is { } probe) ObserveRetiredNetwork(probe);
        _networkProbeReading = null; _networkProbeActions.Clear();
        _networkDiagnosticsStop?.Cancel(); _networkDiagnosticsStop?.Dispose(); _networkDiagnosticsStop = null;
        if (_networkTraceReading is { } trace) ObserveRetiredNetwork(trace);
        if (_resourceResolutionReading is { } resolution) ObserveRetiredNetwork(resolution);
        _networkTraceReading = null; _resourceResolutionReading = null; _resourceResolutionRevision = -1;
    }

    private static void ObserveRetiredNetwork(Task task)
        => _ = task.ContinueWith(static retired => _ = retired.Exception, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private void ReadNetworkDiagnostics()
    {
        if (!_visible || _instanceDirectory is not null || _selected != "network" || _networkTraceReading is not null) return;
        if (_queries.TryResolve(NetworkDiagnosticsContract.Trace, out var route))
        {
            _networkDiagnosticsStop ??= new();
            _networkTraceReading = _queries.QueryAsync<NetworkTraceQuery, IReadOnlyList<NetworkRequestTrace>>(route, new(), cancellationToken: _networkDiagnosticsStop.Token).AsTask();
            WakeOnPlatformCompletion(_networkTraceReading);
        }
        _resourceResolutionRevision = -1; ReadResourceResolution();
    }
    private void ReadResourceResolution()
    {
        if (!_region.IsMainlandChina || _resourceResolutionReading is not null || _resourceResolutionRevision == (_values?.Revision ?? 0)
            || _resourceQueries is null || !_resourceQueries.TryResolve(ResourceCatalogContract.NetworkPolicy, out var route)) return;
        _resourceResolutionRevision = _values?.Revision ?? 0;
        _resourceResolutionReadingRevision = _resourceResolutionRevision;
        _networkDiagnosticsStop ??= new();
        _resourceResolutionReading = _resourceQueries.QueryAsync<ResourceNetworkPolicyQuery, ResourceNetworkPolicySnapshot>(route, new(), cancellationToken: _networkDiagnosticsStop.Token).AsTask();
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
                if (_catalog is not null) RefreshNetworkDiagnostics();
            }
        }
        if (_resourceResolutionReading is { IsCompleted: true } resolutionRead)
        {
            _resourceResolutionReading = null;
            if (_resourceResolutionReadingRevision == (_values?.Revision ?? 0) && resolutionRead.IsCompletedSuccessfully && resolutionRead.Result.IsSuccess)
                _resourceResolutionSnapshot = resolutionRead.Result.Value!;
            else _resourceResolutionRevision = -1;
            if (_catalog is not null) RefreshNetworkDiagnostics();
        }
        if (_networkProbeReading is { IsCompleted: true } probe)
        {
            _networkProbeReading = null; _networkProbeStop?.Dispose(); _networkProbeStop = null;
            if (probe.IsCompletedSuccessfully && probe.Result.IsSuccess) _networkProbeSnapshot = probe.Result.Value!;
            else if (!probe.IsCanceled) _feedback.Error(CustomCopy("network.probeFailed"));
            if (_catalog is not null) RefreshNetworkDiagnostics();
        }
        if (_networkProbeActions.Count == 0) RefreshNetworkDiagnostics();
        ReadResourceResolution();
        if (_networkTraceReading is not { IsCompleted: true } read) return;
        _networkTraceReading = null;
        if (!_visible || _selected != "network") return;
        if (read.IsCompletedSuccessfully && read.Result.IsSuccess) _networkTraceRows = read.Result.Value!;
        else _feedback.Error(CustomCopy("network.traceFailed"));
        _networkTraceLoaded = true;
        if (_catalog is not null) RefreshNetworkDiagnostics();
    }
}
