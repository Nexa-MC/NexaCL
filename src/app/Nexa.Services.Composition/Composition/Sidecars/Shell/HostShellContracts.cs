using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public sealed record HostShellEmpty;
public sealed record HostHttpsRequest(string Address);
public sealed record HostDirectoryRequest(string Path);
public sealed record HostDisplay(string Id, int Width, int Height, double Scale);
public sealed record HostDisplays(HostDisplay[] Items);
public sealed record SidecarShellActions(
    Func<string, CancellationToken, Task> OpenHttps,
    Func<string, CancellationToken, Task> RevealDirectory,
    Func<CancellationToken, Task<HostDisplay[]>> Displays);
public static class SidecarShellRoutes
{
    public static readonly XsrSemanticId OpenHttps = XsrSemanticId.Parse("shell.open_https");
    public static readonly XsrSemanticId RevealDirectory = XsrSemanticId.Parse("shell.reveal_directory");
    public static readonly XsrSemanticId Displays = XsrSemanticId.Parse("shell.displays");
}
