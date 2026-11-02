namespace Nexa.Services.Minecraft.Install;

public sealed partial class MinecraftInstallService
{
    private static async Task DismissInstallationAsync(string root, string stage)
    {
        var plan = await InstallTaskJournal.ReadAsync(root, stage, CancellationToken.None).ConfigureAwait(false);
        string lockPath = Path.Combine(stage, InstallTaskJournal.DirectoryName, "execution.lock");
        Management.RecoveryBlobStore.CheckLinks(lockPath);
        await using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var status = await InstallTaskJournal.ReadStatusAsync(stage, plan, CancellationToken.None).ConfigureAwait(false);
        if (status == InstallTaskStatus.RollbackRequested)
            throw new IOException("回滚尚未完成，请先完成回滚再清理任务。");
        if (status == InstallTaskStatus.Pending)
            await InstallTaskJournal.WriteStatusAsync(stage, plan, InstallTaskStatus.Dismissed, CancellationToken.None).ConfigureAwait(false);
    }
}
