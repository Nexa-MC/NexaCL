using Nexa.Services.Minecraft.Java;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

public static class JavaManualDownloadRuntime
{
#if NEXA_NATIVE_SERVICES
    public static void Register(XsrQueryRouterBuilder queries, XsrCommandRouterBuilder commands,
        Nexa.Native.Minecraft.Client.NativeMinecraftClient minecraft)
    {
        queries.Register<JavaManualPreviewQuery, JavaManualPreview>(JavaManualDownloadContract.Preview, minecraft.PreviewJavaAsync);
        queries.Register<JavaManualStatusQuery, JavaManualReceipt>(JavaManualDownloadContract.Status, minecraft.JavaManualStatusAsync);
        commands.Register<JavaManualInstallCommand>(JavaManualDownloadContract.Install, minecraft.InstallManualJavaAsync);
        commands.Register<JavaManualCancelCommand>(JavaManualDownloadContract.Cancel, minecraft.CancelManualJavaAsync);
    }
#endif
    public static JavaManualDownloadService Register(XsrQueryRouterBuilder queries, XsrCommandRouterBuilder commands,
        IJavaRuntimeMetadataProvider metadata, IConfirmedJavaRuntimeInstaller installer, IJavaRuntimeLocator locator,
        IJavaRuntimeRegistrationStore registrations, string runtimeRoot)
    {
        var service = new JavaManualDownloadService(metadata, installer, locator, registrations, runtimeRoot, JavaRuntimeInstaller.DetectPlatform());
        Register(queries, commands, service); return service;
    }
    public static void Register(XsrQueryRouterBuilder queries, XsrCommandRouterBuilder commands, JavaManualDownloadService service)
    {
        ArgumentNullException.ThrowIfNull(queries); ArgumentNullException.ThrowIfNull(commands); ArgumentNullException.ThrowIfNull(service);
        queries.Register<JavaManualPreviewQuery, JavaManualPreview>(JavaManualDownloadContract.Preview, service.PreviewAsync);
        queries.Register<JavaManualStatusQuery, JavaManualReceipt>(JavaManualDownloadContract.Status, service.StatusAsync);
        commands.Register<JavaManualInstallCommand>(JavaManualDownloadContract.Install, service.InstallAsync);
        commands.Register<JavaManualCancelCommand>(JavaManualDownloadContract.Cancel, service.CancelAsync);
    }
}
