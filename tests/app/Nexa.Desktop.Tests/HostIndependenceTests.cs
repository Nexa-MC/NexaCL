using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void HostCompositionIsPluginIndependent()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        AssertTrue(fixture.Shell.NavigationItems.Select(item => item.Id)
            .SequenceEqual(XsrUiShell.DefaultNavigationItems.Select(item => item.Id)));
        AssertFalse(AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name is { } name
            && (name.StartsWith("Nexa.Plugin.", StringComparison.Ordinal)
                || name.StartsWith("PluginSidecar", StringComparison.Ordinal))));
        AssertTrue(fixture.Shell.Stage.Navigation.Current.IsAssigned);
    }
}
