

using Nexa.Xsr;

namespace Nexa.Services.Updates;

public sealed record NexaUpdateQuery(string CurrentVersion, string RuntimeIdentifier, string Channel);
public sealed record NexaUpdateOffer(string Version, string InstallerUrl, string PortableUrl, string ReleaseUrl);
public sealed record NexaUpdateStatus(NexaUpdateOffer? Offer);
public static class NexaUpdateContract
{
    public static readonly XsrSemanticId Check = XsrSemanticId.Parse("nexa.update.check");
    public static XsrError Unavailable { get; } = new(XsrErrorKind.Unavailable,
        XsrSemanticId.Parse("nexa.update.unavailable"), "暂时无法检查更新，请稍后重试。");
}
