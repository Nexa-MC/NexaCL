#if NEXA_NATIVE_SERVICES
using Nexa.Native.Minecraft.Client;

namespace Nexa.Services.Foundation;

public sealed partial class FoundationHost
{
    private readonly object _nativeMinecraftGate = new();
    private NativeMinecraftClient? _nativeMinecraft;
    private HttpClient? _nativeMinecraftHttp;
    private HttpClient? _nativeMinecraftStrictHttp;
    private bool _nativeMinecraftClosed;

    public NativeMinecraftClient NativeMinecraft
    {
        get
        {
            lock (_nativeMinecraftGate)
            {
                ObjectDisposedException.ThrowIf(_nativeMinecraftClosed, this);
                if (_nativeMinecraft is not null) return _nativeMinecraft;
                string javaRoot = JavaManagedRuntimeRoots.Count == 0 ? Path.Combine(AppContext.BaseDirectory, "runtime") : JavaManagedRuntimeRoots[^1];
                var client = new NativeMinecraftClient(javaRoot,
                    CacheDirectory is null ? null : Path.Combine(CacheDirectory, "native-minecraft"),
                    Path.Combine(AppContext.BaseDirectory, "Nexa.Jvm.Host" + (OperatingSystem.IsWindows() ? ".exe" : "")),
                    replicas: StateStore,
                    failure: error => Logging.Warn("Minecraft", "Native Minecraft callback failed: " + error.GetType().Name));
                _nativeMinecraft = client; Tasks.AttachExternalSource(client);
                client.AttachTasks((revision, entries) => Tasks.PublishExternalSnapshot(client, revision, entries));
                _nativeMinecraftHttp = CreateHttpClient(); _nativeMinecraftHttp.Timeout = Timeout.InfiniteTimeSpan;
                _nativeMinecraftStrictHttp = CreateHttpClient(allowAutoRedirect: false); _nativeMinecraftStrictHttp.Timeout = Timeout.InfiniteTimeSpan;
                client.AttachHttp(_nativeMinecraftHttp, _nativeMinecraftStrictHttp);
                client.AttachDownloads(NetworkHttp.Client, _nativeMinecraftHttp);
                client.AttachJavaRegistry(JavaRegistrations, JavaManagedRuntimeRoots.Count == 0 ? [javaRoot] : JavaManagedRuntimeRoots);
                client.AttachSettings(Settings, SettingsPolicy);
                client.AttachLibraryPersistence(new MinecraftLibraryPersistence(Settings));
                client.AttachWork(Work);
                client.AttachAccounts(Accounts);
                client.AttachObservations(ObservationHistory);
                return client;
            }
        }
    }
    private async ValueTask RetireNativeMinecraftAsync()
    {
        NativeMinecraftClient? client;
        lock (_nativeMinecraftGate) { _nativeMinecraftClosed = true; client = _nativeMinecraft; }
        if (client is not null) await client.DisposeAsync().ConfigureAwait(false);
        _nativeMinecraftHttp?.Dispose();
        _nativeMinecraftStrictHttp?.Dispose();
    }
}
#endif
