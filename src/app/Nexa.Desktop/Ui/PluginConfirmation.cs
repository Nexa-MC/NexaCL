using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;

namespace Nexa.Desktop.Ui;

internal sealed class PluginConfirmation(DesktopFeedbackService feedback) : IActionConfirmation
{
    internal async Task<bool?> AskInstallAsync(string message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid id = feedback.ShowDialog("plugin.install." + Guid.NewGuid().ToString("N"), DesktopOverlayText.Raw("plugin.developer-load"),
            message, DesktopOverlayText.Raw("plugin.remember"), DesktopOverlayText.Raw("plugin.cancel"),
            accepted => result.TrySetResult(accepted ? true : null), DesktopOverlayText.Raw("plugin.once"), () => result.TrySetResult(false));
        using var registration = token.Register(() => { result.TrySetCanceled(token); feedback.DismissDialog(id); });
        try { return await result.Task.ConfigureAwait(false); }
        finally { feedback.DismissDialog(id); }
    }
    public ValueTask<bool> ConfirmAsync(ActionConfirmationRequest request, CancellationToken cancellation) => new(AskAsync(
        DesktopOverlayText.Raw("plugin.action"), request.Caller.PluginId + " → " + request.Provider.PluginId + "\n" + request.Action.Id.Value
        + "\n" + request.Action.Title.MessageKey + "\n" + request.Request.Target.Kind.Value + ": " + request.Request.Target.Entity?.Value
        + "\n" + Describe(request.Request.Inputs), cancellation));

    private static string Describe(AbiValue value)
    {
        var text = new System.Text.StringBuilder();
        Append(value, 0); return text.ToString();
        void Append(AbiValue current, int depth)
        {
            if (depth > 16 || text.Length > 65536) throw new InvalidDataException("Confirmation exceeds its display budget.");
            switch (current)
            {
                case AbiValue.Map map:
                    foreach (var field in map.Value) { text.Append(' ', depth).Append(field.Key).Append(": "); Append(field.Value, depth + 1); text.AppendLine(); }
                    break;
                case AbiValue.Array array:
                    foreach (var item in array.Value) { Append(item, depth + 1); text.AppendLine(); }
                    break;
                case AbiValue.Utf8String item: text.Append(item.Value); break;
                case AbiValue.Bool item: text.Append(item.Value); break;
                case AbiValue.Null: text.Append("null"); break;
                default: text.Append(current); break;
            }
        }
    }

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
