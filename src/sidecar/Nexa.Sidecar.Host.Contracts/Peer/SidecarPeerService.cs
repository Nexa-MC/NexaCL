using Nexa.Sidecar.Host.Peer.Data;
using Nexa.Sidecar.Host.Peer.Wire;
using Nexa.Sidecar.Protocol;

namespace Nexa.Sidecar.Host;

/// <summary>Decode a native service query received on the authenticated Host connection.</summary>
public static class SidecarPeerService
{
    public const int PayloadLimit = 60 * 1024;

    /// <summary>The Host supplies caller identity. The service must authorize that caller before applying its payload.</summary>
    public static HostPeerEnvelope DecodeRequest(SidecarBinaryValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.CodecId != SidecarWireCodecs.GeneratedDto || value.Length > SidecarWireCodecs.MaximumValueLength)
            throw new InvalidDataException("Invalid peer service envelope.");
        using var reader = new ServiceWireReader(value.ToArray());
        var envelope = reader.HostPeerEnvelope() ?? throw new InvalidDataException("Missing peer service envelope.");
        reader.End();
        if (envelope.CallerPackage.Length is < 1 or > 256 || !Guid.TryParseExact(envelope.CallerSession, "N", out var session)
            || session == Guid.Empty || envelope.Payload.Length > PayloadLimit)
            throw new InvalidDataException("Invalid peer service identity or payload.");
        return envelope;
    }
}
