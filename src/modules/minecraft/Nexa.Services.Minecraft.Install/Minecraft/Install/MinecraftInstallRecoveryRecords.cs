using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Install;

public sealed partial class MinecraftInstallService
{
    public static async ValueTask<XsrResult<MinecraftInstallRecoveryListing>> ListRecoveryRecordsAsync(MinecraftInstallRecoveryListQuery query, CancellationToken token = default)
    {
        try { return XsrResult.Success(new MinecraftInstallRecoveryListing(await RecoveryRecords(query.RootDirectories, token).ConfigureAwait(false))); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return XsrResult.Failure<MinecraftInstallRecoveryListing>(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        { return XsrResult.Failure<MinecraftInstallRecoveryListing>(MinecraftErrors.InvalidRequest(error.Message)); }
    }
    private static string RecoveryRevision(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static async Task<IReadOnlyList<MinecraftInstallRecoveryRecord>> RecoveryRecords(IReadOnlyList<string> roots, CancellationToken token)
    {
        if (roots.Count > 64 || roots.Any(r => !Path.IsPathFullyQualified(r))) throw new InvalidDataException("Invalid recovery roots.");
        var records = new List<MinecraftInstallRecoveryRecord>(); int visited = 0;
        foreach (string root in roots.Distinct(Nexa.Core.PathIdentity.Comparer))
        {
            foreach (var pack in await ReadModpackJobsAsync(root, token).ConfigureAwait(false))
            {
                string identity = "modpack:" + Path.GetFileName(pack.Stage);
                records.Add(new(root, identity, pack.Command.Pack.InstanceId, RecoveryRevision(identity + ":" + pack.TrustVersion + ":" + pack.Prepared)));
            }
            foreach (var (folder, kind) in new[] { (".nexa-install-jobs", "new"), (".nexa-modify", "modify") })
            {
                string directory = Path.Combine(root, folder); RecoveryBlobStore.CheckLinks(directory);
                if (!Directory.Exists(directory)) continue;
                foreach (string stage in Directory.EnumerateDirectories(directory))
                {
                    token.ThrowIfCancellationRequested();
                    if (++visited > 4096) throw new InvalidDataException("Too many recovery records.");
                    if (!Guid.TryParseExact(Path.GetFileName(stage), "N", out _)) continue;
                    var plan = await InstallTaskJournal.ReadAsync(root, stage, token).ConfigureAwait(false);
                    var status = await InstallTaskJournal.ReadStatusAsync(stage, plan, token).ConfigureAwait(false);
                    if (status is InstallTaskStatus.Completed or InstallTaskStatus.RolledBack or InstallTaskStatus.Dismissed) continue;
                    string identity = kind + ":" + plan.Id.ToString("N");
                    records.Add(new(root, identity, plan.Command.InstanceName ?? plan.Command.GameVersion,
                        RecoveryRevision(identity + ":" + plan.CreatedAt.ToString("O") + ":" + status)));
                }
            }
            if (records.Count > 256) throw new InvalidDataException("Too many pending recovery records.");
        }
        return records.AsReadOnly();
    }
    public async ValueTask<XsrResult> ApplyRecoveryRecordAsync(MinecraftInstallRecoveryRecordCommand command, CancellationToken token = default)
    {
        try
        {
            lock (_executionGate)
            {
                if (_stopping) return XsrResult.Failure(XsrRuntimeErrors.Cancelled());
                if (!Path.IsPathFullyQualified(command.RootDirectory)) throw new InvalidDataException("Invalid recovery root.");
                _recoveryRoots.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(command.RootDirectory)));
            }
            await _recoveryDiscoveryGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (_executionGate) if (_stopping) return XsrResult.Failure(XsrRuntimeErrors.Cancelled());
                var record = (await RecoveryRecords([command.RootDirectory], token).ConfigureAwait(false)).SingleOrDefault(r => r.Identity == command.Identity);
                if (record is null || record.Revision != command.ExpectedRevision) throw new IOException("Recovery record changed.");
                var parts = command.Identity.Split(':');
                if (parts.Length != 2 || !Guid.TryParseExact(parts[1], "N", out Guid id)) throw new InvalidDataException("Invalid recovery identity.");
                if (parts[0] == "modpack")
                {
                    var pack = (await ReadModpackJobsAsync(command.RootDirectory, token).ConfigureAwait(false)).Single(p => Path.GetFileName(p.Stage) == parts[1]);
                    if (command.Discard) await pack.DismissAsync().ConfigureAwait(false);
                    else
                    {
                        var result = await InstallModpackCoreAsync(pack.Command, token, pack).ConfigureAwait(false);
                        return result.IsSuccess ? XsrResult.Success() : XsrResult.Failure(result.Error!);
                    }
                }
                else
                {
                    bool fresh = parts[0] == "new";
                    string stage = Path.Combine(command.RootDirectory, fresh ? ".nexa-install-jobs" : ".nexa-modify", parts[1]);
                    if (command.Discard) await DismissInstallationAsync(command.RootDirectory, stage).ConfigureAwait(false);
                    else
                    {
                        var plan = await InstallTaskJournal.ReadAsync(command.RootDirectory, stage, token).ConfigureAwait(false);
                        if (await InstallTaskJournal.ReadStatusAsync(stage, plan, token).ConfigureAwait(false) == InstallTaskStatus.RollbackRequested)
                            await RollbackInstallationAsync(command.RootDirectory, id, fresh, token).ConfigureAwait(false);
                        else await ResumeInstallationAsync(command.RootDirectory, id, fresh, token).ConfigureAwait(false);
                    }
                }
                return XsrResult.Success();
            }
            finally { _recoveryDiscoveryGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        { return XsrResult.Failure(MinecraftErrors.InvalidRequest(error.Message)); }
    }
}
