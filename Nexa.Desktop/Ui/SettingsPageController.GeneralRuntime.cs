using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private XsrUiEntityId _generalProtocolStatus;
    private DesktopProtocolObservation? _generalProtocolObservation;

    private void BuildGeneralRuntimeStates()
    {
        _generalProtocolStatus = default; _generalProtocolObservation = null;
        if (_instanceDirectory is not null || _selected != "general") return;
        var observation = DesktopProtocolRegistration.Observation;
        _generalProtocolStatus = _dynamic.Append(_sections, DesktopMarkup.Dynamic("SettingsGeneralRuntime.d.nxml", ProtocolProjection(observation)));
        _generalProtocolObservation = observation;
    }

    private static Dictionary<string, string> ProtocolProjection(DesktopProtocolObservation observation)
    {
        string timestamp = observation.ObservedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        string details = string.Join(" ", new[] { timestamp, observation.Detail }.Where(value => !string.IsNullOrEmpty(value)));
        return new()
        {
            ["idle"] = Flag(!observation.Attempted),
            ["pending"] = Flag(observation.Attempted && observation.Pending),
            ["success"] = Flag(observation.Attempted && !observation.Pending && observation.Succeeded),
            ["failure"] = Flag(observation.Attempted && !observation.Pending && !observation.Succeeded),
            ["details"] = details,
            ["hasDetails"] = Flag(details.Length > 0),
        };
    }

    private void UpdateGeneralRuntimeStates()
    {
        if (!_visible || _instanceDirectory is not null || _selected != "general"
            || !_generalProtocolStatus.IsAssigned || !_shell.Tree.IsAlive(_generalProtocolStatus)) return;
        var observed = DesktopProtocolRegistration.Observation;
        if (Equals(_generalProtocolObservation, observed)) return;
        _generalProtocolObservation = observed;
        _dynamic.Reload(_generalProtocolStatus, DesktopMarkup.Dynamic("SettingsGeneralRuntime.d.nxml", ProtocolProjection(observed)));
    }
}
