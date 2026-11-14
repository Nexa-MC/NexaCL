using Nexa.Desktop.Ui;
using Nexa.Services.Composition.Sidecars;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop;

/// <summary>Native Sidecar feedback carries a Host-owned source label and literal content.</summary>
internal static class DesktopSidecarNotifications
{
    internal static void Publish(DesktopFeedbackService feedback, SidecarHostCaller caller, HostNotification notification, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string source = DesktopOverlayText.Format("SidecarConfirmationTitle", new() { ["package"] = caller.PackageName });
        feedback.Notify(notification.Level switch
        {
            HostNotificationLevel.Info => DesktopNotificationLevel.Info,
            HostNotificationLevel.Warning => DesktopNotificationLevel.Warn,
            HostNotificationLevel.Error => DesktopNotificationLevel.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(notification))
        }, source + "\n" + notification.Title + "\n" + notification.Body, localizeMessage: false);
    }
}
