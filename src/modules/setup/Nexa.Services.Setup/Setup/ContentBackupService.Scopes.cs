using System.Text;

namespace Nexa.Services.Setup;

public sealed partial class ContentBackupService
{
    /// <summary>Composition passes an admitted instance directory, never a guest-supplied path.</summary>
    public ContentBackupService ForInstance(string instanceDirectory)
    {
        string instance = Absolute(instanceDirectory);
        if (OperatingSystem.IsWindows()) instance = instance.ToUpperInvariant();
        string scope = Hash(Encoding.UTF8.GetBytes(instance));
        return new(Path.Combine(_root, "instance-stores", scope), _isIdle, _clock)
        {
            CloneFile = CloneFile,
            CaptureSnapshot = CaptureSnapshot
        };
    }

    public async Task<InstanceBackupSnapshot> ReadScopeAsync(CancellationToken token = default)
    {
        await using var lease = await AcquireAsync(token).ConfigureAwait(false);
        return await ReadScopeUnderLeaseAsync(token).ConfigureAwait(false);
    }

    private async Task<InstanceBackupSnapshot> ReadScopeUnderLeaseAsync(CancellationToken token)
    {
        var manifests = await ReadAllAsync(token).ConfigureAwait(false);
        var retention = await ReadRetentionUnderLeaseAsync(token).ConfigureAwait(false);
        string revision = Hash(Encoding.UTF8.GetBytes(string.Join('\n', manifests.OrderBy(x => x.Identity, StringComparer.Ordinal).Select(x => Encode(x).ToJsonString()))
            + FormattableString.Invariant($"\n{retention.Enabled}:{retention.KeepCount}:{retention.KeepDays}")));
        return new(manifests.AsReadOnly(), retention, revision);
    }
}
