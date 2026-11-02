using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Tasks;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal sealed class DesktopInstallExitCoordinator(
    XsrStateStore store, XsrCommandRouter commands, DesktopFeedbackService feedback, Action close, XsrCommandRouter? javaCommands = null,
    Func<string, string>? localize = null)
{
    private int _busy;
    private volatile bool _approved;
    private volatile bool _stopFailed;

    public bool CanClose()
    {
        if (_approved) return true;
        if (Volatile.Read(ref _busy) != 0) return false;
        var active = store.ReadCollection<TaskCenterEntry>(store.Resolve(TaskCenterStateContract.EntriesKey)).Items
            .Where(item => !item.IsTerminal && (item.TaskId.StartsWith("install:", StringComparison.Ordinal)
                || item.TaskId.StartsWith("install-recovery:", StringComparison.Ordinal)
                || item.TaskId.StartsWith("java-install:", StringComparison.Ordinal)
                || item.TaskId.StartsWith("modpack:", StringComparison.Ordinal))).ToArray();
        if (active.Length == 0 && !_stopFailed) return true;
        if (Interlocked.Exchange(ref _busy, 1) != 0) return false;
        if (active.Length == 0 && _stopFailed)
        {
            feedback.ShowDialog("install.exit.retry", ProductPresentationText.Raw("native.install.exit.retry-title"), ProductPresentationText.Raw("native.install.exit.retry-message"),
                ProductPresentationText.Raw("native.install.exit.retry-exit"), ProductPresentationText.Raw("native.install.exit.remain"), accepted =>
                {
                    if (accepted) _ = StopAsync(false); else Interlocked.Exchange(ref _busy, 0);
                });
            return false;
        }
        bool modifications = active.Any(item => item.Title.StartsWith("修改", StringComparison.Ordinal)
            || item.Title.StartsWith("继续修改", StringComparison.Ordinal)
            || item.TaskId.StartsWith("install-recovery:queue:", StringComparison.Ordinal));
        Guid dialogId = Guid.Empty;
        dialogId = feedback.ShowDialog("install.exit", ProductPresentationText.Raw("native.install.exit.title"), ProductPresentationText.Raw("native.install.exit.message"),
            ProductPresentationText.Raw("native.install.exit.pause-exit"), ProductPresentationText.Raw("native.install.exit.remain"), accepted =>
            {
                if (accepted) _ = StopAsync(true); else Interlocked.Exchange(ref _busy, 0);
            }, ProductPresentationText.Raw(modifications ? "native.install.exit.rollback-exit" : "native.install.exit.cancel-exit"), () => { if (feedback.DismissDialog(dialogId)) _ = StopAsync(false); });
        return false;
    }

    private async Task StopAsync(bool pause)
    {
        try
        {
            if (!commands.TryResolve(MinecraftInstallRoutes.Stop, out var route)) throw new InvalidOperationException(ProductPresentationText.Term("native.install.exit.stop-route-missing", localize));
            var result = await Task.Run(async () => await commands.Dispatch(route, new MinecraftInstallStopCommand(pause)).Completion.ConfigureAwait(false)).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                _stopFailed = true;
                feedback.Error(result.Error?.Code == MinecraftErrors.InvalidRequestCode
                    ? result.Error.Message : ProductPresentationText.Raw("native.install.exit.stop-failed"));
                return;
            }
            if (javaCommands is not null && javaCommands.TryResolve(JavaInstallRoutes.Stop, out var javaRoute))
            {
                var javaResult = await Task.Run(async () => await javaCommands.Dispatch(javaRoute, new JavaInstallStopCommand(pause)).Completion.ConfigureAwait(false)).ConfigureAwait(false);
                if (!javaResult.IsSuccess)
                {
                    _stopFailed = true;
                    feedback.Error(javaResult.Error?.Message ?? ProductPresentationText.Raw("native.install.exit.java-stop-pending"));
                    return;
                }
            }
            _approved = true;
            close();
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { _stopFailed = true; feedback.Error(ProductPresentationText.Format("NativeInstallStopFailure", localize, ("error", error.Message)), localizeMessage: false); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}
