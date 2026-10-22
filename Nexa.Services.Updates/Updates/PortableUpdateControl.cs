using Nexa.Platform.Updates;

namespace Nexa.Services.Updates;

/// <summary>Ordinary-user portable composition of the signed slot transaction. No helper.</summary>
public sealed class PortableUpdateControl(string root, UpdateBuildIdentity running,
    IUpdateSignatureVerifier verifier, IUpdateReleaseSource source) : IAutomaticUpdateControl
{
    private const string BaselineName = "portable-baseline.journal";

    public Task<AutomaticUpdateStatus> ReadAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var directory = PortableUpdateDirectory.Open(root);
        _ = Baseline(directory);
        return ReadStatus(directory);
    }, token);

    public async Task<AutomaticUpdateStatus> InstallAsync(string version, string channel, CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromMinutes(20));
        return await Task.Run(async () =>
        {
            using var directory = PortableUpdateDirectory.Open(root);
            var transaction = new AutomaticUpdateTransaction(directory, Baseline(directory), verifier, source);
            await transaction.InstallAsync(version, channel, budget.Token).ConfigureAwait(false);
            return ReadStatus(directory);
        }, budget.Token).ConfigureAwait(false);
    }

    public Task<AutomaticUpdateStatus> RollbackAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var directory = PortableUpdateDirectory.Open(root);
        new AutomaticUpdateTransaction(directory, Baseline(directory), verifier, source).Rollback();
        return ReadStatus(directory);
    }, token);

    public void Restart() => PortableUpdateInstallation.Restart(root);

    private UpdateBuildIdentity Baseline(IUpdateDirectory directory)
    {
        using var file = directory.OpenState(BaselineName);
        string[]? saved = UpdateTransactionJournal.Read(file);
        if (saved is null)
        {
            _ = UpdateHighWaterJournal.ParseVersion(running.Version);
            UpdateTransactionJournal.Append(file, running.Version, running.RuntimeId, running.RuntimeVariant, running.Configuration);
            return running;
        }
        if (saved.Length != 4 || !UpdateVersion.TryParse(saved[0], out var version) || version.ToString() != saved[0]
            || saved[1] != running.RuntimeId || saved[2] != running.RuntimeVariant || saved[3] != running.Configuration)
            throw new InvalidDataException("便携更新的原始安装身份不匹配。");
        return running with { Version = saved[0] };
    }

    private static AutomaticUpdateStatus ReadStatus(IUpdateDirectory directory)
    {
        string[]? status = ReadOptional(directory, AutomaticUpdateTransaction.StatusName);
        string[]? active = ReadOptional(directory, AutomaticUpdateTransaction.ActivationName);
        if (status is { Length: 3 } && status[2] == "complete" && active is { Length: 4 } && active[0] != status[0])
            return new(active[0], null, "rolledback", true, active[2].Length != 0);
        return new(status is { Length: 3 } ? status[0] : null, status is { Length: 3 } ? status[1] : null,
            status is { Length: 3 } ? status[2] : "idle", true, active is { Length: 4 } && active[2].Length != 0);
    }

    private static string[]? ReadOptional(IUpdateDirectory directory, string name)
    {
        try { using var file = directory.OpenRead(name); return UpdateTransactionJournal.Read(file); }
        catch (FileNotFoundException) { return null; }
    }
}
