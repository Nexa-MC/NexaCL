namespace Nexa.Services.Updates;

public sealed record SidecarUpdateItem(string Id, string CurrentVersion, string? AvailableVersion, string Phase, bool CanInstall, double Progress = 0);
public sealed record SidecarUpdateSnapshot(string Phase, IReadOnlyList<SidecarUpdateItem> Items);
public interface ISidecarUpdateControl
{
    SidecarUpdateSnapshot Snapshot { get; }
    Task CheckAsync(string channel, CancellationToken token);
    Task InstallAsync(string packageId, CancellationToken token);
}
