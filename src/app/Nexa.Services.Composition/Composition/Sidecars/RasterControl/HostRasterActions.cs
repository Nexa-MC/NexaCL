using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostRasterActions(
    Func<SidecarHostSession, HostRasterBegin, CancellationToken, ValueTask> Begin,
    Func<SidecarHostSession, HostRasterAppend, CancellationToken, ValueTask> Append,
    Func<SidecarHostSession, HostRasterRef, CancellationToken, ValueTask> Seal,
    Func<SidecarHostSession, HostRasterSet, CancellationToken, ValueTask> Replace,
    Func<SidecarHostSession, HostRasterRef, CancellationToken, ValueTask> Release);
