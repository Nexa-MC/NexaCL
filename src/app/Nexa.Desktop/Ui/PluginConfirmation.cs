using Nexa.Plugin.Hosting;

namespace Nexa.Desktop.Ui;

internal sealed class PluginConfirmation(DesktopFeedbackService feedback) : IActionConfirmation
{
    public ValueTask<bool> ConfirmAsync(ActionConfirmationRequest request, CancellationToken cancellation) => new(AskAsync(
        DesktopOverlayText.Raw("plugin.action"), request.Caller.PluginId + " → " + request.Provider.PluginId + "\n" + request.Action.Id.Value
        + "\n" + request.Request.Target.Kind.Value + ": " + request.Request.Target.Entity?.Value, cancellation));

    internal async Task<bool> AskAsync(string title, string message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid id = feedback.ShowDialog("plugin.confirm." + Guid.NewGuid().ToString("N"), title, message,
            DesktopOverlayText.Raw("plugin.allow"), DesktopOverlayText.Raw("plugin.cancel"), accepted => result.TrySetResult(accepted), localizeMessage: true, localizeTitle: true);
        using var registration = token.Register(() => { result.TrySetCanceled(token); feedback.DismissDialog(id); });
        try { return await result.Task.ConfigureAwait(false); }
        finally { feedback.DismissDialog(id); }
    }
}
