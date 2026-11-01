using Nexa.Xsr;

namespace Nexa.Services.Settings;

public sealed record SettingsRenameReceipt(string Source, string Destination, string Identity);

/// <summary>Canonical settings mutation at the durable Minecraft rename boundary.</summary>
public interface ISettingsRenameTransactions
{
    ValueTask<XsrResult<SettingsRenameReceipt>> PrepareRenameAsync(string source, string destination, CancellationToken token = default);
    ValueTask<XsrResult> ApplyRenameAsync(SettingsRenameReceipt receipt, bool reverse, CancellationToken token = default);
}
