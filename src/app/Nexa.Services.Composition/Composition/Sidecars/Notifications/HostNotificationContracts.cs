using Nexa.Xsr;

namespace Nexa.Services.Composition.Sidecars;

public enum HostNotificationLevel { Info = 1, Warning = 2, Error = 3 }
public sealed record HostNotification(HostNotificationLevel Level, string Title, string Body);
public static class SidecarNotificationsRoutes
{
    public static readonly XsrSemanticId Publish = XsrSemanticId.Parse("interaction.notifications.publish");
}
