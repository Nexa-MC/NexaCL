using Nexa.Native.Minecraft.Http;
using Nexa.Native.Minecraft.Services;
using Nexa.Native.Network.Client;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private readonly NetworkHttpStreamHandler _strictHostHttp;
    private readonly RemoteMinecraftSettings _settings;
    private MinecraftInstallService? _installation;
    private HttpClient? _installationHttp;
    private bool _installationStopped;
    private IInstallCatalogSource? _installationCatalogSource;
    private Nexa.Services.RegionalPolicy? _installationRegion;
    private HttpClient CreateInstallationHttp(bool redirects)
    {
        var handler = redirects ? _hostHttp : _strictHostHttp;
#if NEXA_NATIVE_SERVICES
        return NativeHttpClients.CreateDescribed(handler, async (address, token) =>
        {
            var source = await _downloads.DescribeAsync(address, token).ConfigureAwait(false);
            return source with { AllowAutoRedirect = redirects };
        }, disposeHandler: false);
#else
        return new(handler, disposeHandler: false);
#endif
    }
    private async Task<byte[]> ExecuteInstallationAsync(int operation, ServiceWireReader reader, CancellationToken token)
    {
        using var writer = new ServiceWireWriter();
        if (operation == MinecraftServiceProtocol.InstallConfigure)
        {
            string country = reader.String() ?? ""; string? apiKey = reader.String(); reader.End();
            if (country.Length > 2 || country.Any(c => !char.IsAsciiLetter(c)) || apiKey?.Length > 4096)
                throw new InvalidDataException("Invalid installation transport configuration.");
            await _initialization.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing), this);
                if (_installation is not null) throw new InvalidOperationException("Minecraft installation is already configured.");
                _installationHttp = CreateInstallationHttp(true);
                var region = Nexa.Services.RegionalPolicy.Resolve(country);
                _installationRegion = region;
                _installationCatalogSource = new HttpInstallCatalogSource(_installationHttp, apiKey, region);
                _installation = new(_tasks, _sessionDownloads, new InstallationCatalogSource(this),
                    _installationHttp, loaderInstaller: new ForgeInstallService(_sessionDownloads, _installationHttp, regionalPolicy: region), hostStore: _store)
                { SettingsPolicies = _settings, SettingsTransactions = _settings, HttpClientFactory = CreateInstallationHttp, CountryPolicy = region, CurseForgeApiKey = apiKey };
                Remediations.BindInstall(_installation);
                _localJars = new(_tasks, _store, _installation); _folderImports = new(_tasks);
                _installation.Installed += root => PublishInstallEvent(root);
                _installation.Renamed += (root, previous, current) => PublishInstallEvent(root, previous, current);
            }
            finally { _initialization.Release(); }
            return writer.Finish();
        }
        var installation = _installation ?? throw new InvalidOperationException("Minecraft installation is not configured.");
        switch (operation)
        {
            case MinecraftServiceProtocol.InstallEditRead:
                string editRoot = reader.String() ?? throw new InvalidDataException("Missing Minecraft edit root.");
                string editInstance = reader.String() ?? throw new InvalidDataException("Missing Minecraft instance."); reader.End();
                writer.MinecraftInstallEditSnapshot(await MinecraftInstallEditService.ReadAsync(new(editRoot, editInstance), token).ConfigureAwait(false)); break;
            case MinecraftServiceProtocol.InstallRun:
                var command = reader.MinecraftInstallCommand() ?? throw new InvalidDataException("Missing Minecraft install command."); reader.End();
                var result = await installation.InstallAsync(command, token).ConfigureAwait(false);
                writer.XsrError(result.Error); if (result.IsSuccess) writer.MinecraftInstallResult(result.Value); break;
            case MinecraftServiceProtocol.InstallModpack:
                var modpack = reader.MinecraftModpackCommand() ?? throw new InvalidDataException("Missing Minecraft modpack command."); reader.End();
                var imported = await installation.InstallModpackAsync(modpack, token).ConfigureAwait(false);
                writer.XsrError(imported.Error); if (imported.IsSuccess) writer.MinecraftInstallResult(imported.Value); break;
            case MinecraftServiceProtocol.InstallStop:
                bool pause = reader.Boolean(); reader.End();
                var stopped = await installation.StopAsync(new(pause), token).ConfigureAwait(false);
                if (stopped.IsSuccess) _installationStopped = true;
                writer.XsrError(stopped.Error); break;
            case MinecraftServiceProtocol.InstallRecover:
                var recovery = reader.MinecraftInstallRecoveryCommand() ?? throw new InvalidDataException("Missing Minecraft recovery command."); reader.End();
                writer.XsrError((await installation.RecoverPendingAsync(recovery, token).ConfigureAwait(false)).Error); break;
            default: throw new InvalidDataException("Unknown Minecraft installation operation.");
        }
        return writer.Finish();
    }
    private async Task RetireInstallationAsync()
    {
        if (_installation is { } installation)
        {
            if (!_installationStopped)
            {
                var stopped = await installation.StopAsync(new(Pause: true)).ConfigureAwait(false);
                if (!stopped.IsSuccess) throw new IOException("Minecraft installation could not physically retire: " + stopped.Error!.Message);
            }
        }
    }
}
