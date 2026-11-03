using System.Globalization;
using Nexa.Services.Minecraft.Install;
namespace Nexa.Desktop.Ui;

internal static class ModpackOptionalSelection
{
    internal static async Task<IReadOnlyList<string>?> ChooseAsync(DesktopFeedbackService feedback, MinecraftModpackPreview pack,
        Func<string, string>? localize, CancellationToken token)
    {
        List<string> excluded = [];
        int index = 0;
        foreach (var item in pack.OptionalEntries)
        {
            token.ThrowIfCancellationRequested();
            var decision = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Guid id = default;
            string title = ProductPresentationText.Format("NativeOptionalFileTitle", localize,
                ("index", (++index).ToString(CultureInfo.CurrentCulture)), ("count", pack.OptionalEntries.Count.ToString(CultureInfo.CurrentCulture)));
            id = feedback.ShowDialog("modpack.optional.choice", title, item.Name,
                ProductPresentationText.Raw("native.drop.optional-include"), ProductPresentationText.Raw("native.drop.optional-cancel"),
                accepted => decision.TrySetResult(accepted ? true : null), ProductPresentationText.Raw("native.drop.optional-skip"), () =>
                { feedback.DismissDialog(id); decision.TrySetResult(false); }, localizeMessage: false, localizeTitle: false);
            using var cancelled = token.Register(() => { feedback.DismissDialog(id); decision.TrySetCanceled(token); });
            bool? include = await decision.Task.ConfigureAwait(false);
            if (include is null) return null;
            if (!include.Value) excluded.Add(item.Id);
        }
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        string summary = ProductPresentationText.Format("NativeOptionalSummary", localize,
            ("name", pack.Name), ("required", pack.RequiredFiles.ToString(CultureInfo.CurrentCulture)),
            ("selected", (pack.OptionalEntries.Count - excluded.Count).ToString(CultureInfo.CurrentCulture)), ("count", pack.OptionalEntries.Count.ToString(CultureInfo.CurrentCulture)));
        Guid confirmId = feedback.ShowDialog("modpack.optional.confirm", ProductPresentationText.Raw("native.drop.optional-confirm"), summary,
            ProductPresentationText.Raw("native.drop.modpack-install"), ProductPresentationText.Raw("native.common.cancel"),
            accepted => confirmation.TrySetResult(accepted), localizeMessage: false);
        using var stop = token.Register(() => { feedback.DismissDialog(confirmId); confirmation.TrySetCanceled(token); });
        return await confirmation.Task.ConfigureAwait(false) ? excluded : null;
    }
}
