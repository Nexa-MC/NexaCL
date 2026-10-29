using Nexa.Services.Logging;
using Nexa.Services.Updates;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop;

internal static class SidecarStartup
{
    public static SidecarSupervisor Create(LogService log, XsrFunctionPatchAdmission? functionPatches = null,
        XsrSignalAdmission? signals = null, XsrUiPatchAdmission? uiPatches = null, XsrUiModuleAdmission? uiModules = null,
        XsrNxmlModuleRuntime? nxmlModules = null, IReadOnlyDictionary<string, string>? packageOverrides = null)
    {
        using Stream key = typeof(SidecarStartup).Assembly.GetManifestResourceStream("Nexa.Desktop.SidecarReleaseKey.asc")
            ?? throw new InvalidDataException("The pinned Sidecar verification key is missing.");
        using var reader = new StreamReader(key);
        var verifier = new UpdateGpgVerifier(reader.ReadToEnd());
        return new(verifier.VerifyAsync, (name, status) => log.Info("Sidecar", $"{name}: {status}"))
        { PackageOverrides = packageOverrides ?? new Dictionary<string, string>(), FunctionPatchAdmission = functionPatches, AutomaticFunctionAdmission = new(), SignalAdmission = signals, UiPatchAdmission = uiPatches, UiModuleAdmission = uiModules, NxmlModules = nxmlModules };
    }

    public static SidecarUpdateService CreateUpdater(HttpClient client, string root, string version)
    {
        using Stream key = typeof(SidecarStartup).Assembly.GetManifestResourceStream("Nexa.Desktop.SidecarReleaseKey.asc")!;
        using var reader = new StreamReader(key);
        string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        string rid = os + "-" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        return new(client, new UpdateGpgVerifier(reader.ReadToEnd()), Path.Combine(root, "sidecar-updates"), AppContext.BaseDirectory, rid, version);
    }

    public static async Task<IReadOnlyDictionary<string, string>> ResolveUpdatesAsync(SidecarUpdateService updater, LogService log)
    {
        try { return await updater.ActivePackagesAsync().ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            log.Warn("Sidecar", "Unable to read installed Sidecar updates; using bundled packages: " + error.Message);
            return new Dictionary<string, string>();
        }
    }

    public static async Task StartAsync(SidecarSupervisor supervisor, LogService log)
    {
        try { await supervisor.StartAsync(AppContext.BaseDirectory).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { log.Warn("Sidecar", "Sidecar discovery failed: " + error.Message); }
    }

    public static Lifetime StartLifetime(SidecarSupervisor supervisor, LogService log, bool enabled = true) => new(supervisor, enabled ? StartAsync(supervisor, log) : Task.CompletedTask);

    /// <summary>Joins discovery diagnostics before the Desktop releases logging/state dependencies.</summary>
    internal sealed class Lifetime(SidecarSupervisor supervisor, Task startup) : IAsyncDisposable, IDisposable
    {
        internal Task InitialReady => startup;
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
        public async ValueTask DisposeAsync()
        {
            await supervisor.ShutdownAsync().ConfigureAwait(false);
            await startup.ConfigureAwait(false);
        }
    }
}
