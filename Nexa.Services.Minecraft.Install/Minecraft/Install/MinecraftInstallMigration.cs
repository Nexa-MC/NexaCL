using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Minecraft.Install;

public sealed partial class MinecraftInstallService
{
    /// <summary>Retires verified terminal receipts before a new migration root changes path.</summary>
    internal static async Task PrepareMigrationRootAsync(string root, CancellationToken token)
    {
        root = Nexa.Core.PathIdentity.Normalize(root);
        string parent = Path.GetFileName(Path.GetDirectoryName(root)) ?? "";
        const string prefix = ".nexa-migration-stage-";
        if (!parent.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(parent[prefix.Length..], "N", out _))
            throw new InvalidDataException("只能整理当前迁移的暂存实例。");
        RecoveryBlobStore.CheckLinks(root);
        string jobs = Path.Combine(root, ".nexa-install-jobs");
        RecoveryBlobStore.CheckLinks(jobs);
        if (!Directory.Exists(jobs)) throw new InvalidDataException("迁入实例缺少已完成安装记录。");
        string[] stages = Directory.GetFileSystemEntries(jobs);
        if (stages.Length != 1) throw new InvalidDataException("迁入实例安装记录数量不一致。");
        string stage = stages[0];
        var plan = await InstallTaskJournal.ReadAsync(root, stage, token).ConfigureAwait(false);
        if (await InstallTaskJournal.ReadStatusAsync(stage, plan, token).ConfigureAwait(false) != InstallTaskStatus.Completed)
            throw new InvalidDataException("迁入实例安装尚未完成，不能移动恢复记录。");
        string taskDirectory = Path.Combine(stage, ".task");
        string[] expected = ["execution.lock", "plan.json", "status.json"];
        RecoveryBlobStore.CheckLinks(taskDirectory);
        if (!Directory.GetFileSystemEntries(stage).SequenceEqual([taskDirectory], Nexa.Core.PathIdentity.Comparer))
            throw new InvalidDataException("迁入实例仍有未整理的安装文件。");
        string[] files = Directory.GetFileSystemEntries(taskDirectory);
        if (files.Length != expected.Length || files.Any(path => !expected.Contains(Path.GetFileName(path), StringComparer.Ordinal)))
            throw new InvalidDataException("迁入实例仍有未知安装记录。");
        foreach (string file in files) RecoveryBlobStore.CheckLinks(file);
        using (var lease = new FileStream(Path.Combine(taskDirectory, "execution.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            token.ThrowIfCancellationRequested();
            File.Delete(Path.Combine(taskDirectory, "plan.json"));
            File.Delete(Path.Combine(taskDirectory, "status.json"));
        }
        File.Delete(Path.Combine(taskDirectory, "execution.lock"));
        Directory.Delete(taskDirectory); Directory.Delete(stage); Directory.Delete(jobs);
    }
}
