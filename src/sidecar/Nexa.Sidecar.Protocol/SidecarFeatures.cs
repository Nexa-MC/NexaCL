namespace Nexa.Sidecar.Protocol;

/// <summary>Append-only optional features negotiated independently of the protocol version.</summary>
[Flags]
public enum SidecarFeatures : uint
{
    None = 0,
    BinaryPayloads = 1,
    Streams = 2,
    Health = 4,
    Unregistration = 8,
    Nxml = 16,
    AutomaticFunctions = 32,
    Pages = 64,
    All = BinaryPayloads | Streams | Health | Unregistration | Nxml | AutomaticFunctions | Pages,
}
