using Nexa.Desktop.Ui;
using Nexa.UI.Next;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void PageInsetsSurviveControllerTransitions()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var shell = fixture.Shell;
        shell.Renderer.ReducedMotion = true;
        AssertTrue(shell.IsNavigationExpanded);
        var home = shell.Stage.Navigation.Current;
        using var settings = new SettingsPageController(shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        using var resources = new ResourcesPageController(shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Store, _ => { });
        using var wardrobe = new WardrobePageController(shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        foreach (var page in new[] { settings.Page, resources.Page, wardrobe.Page, home,
                     wardrobe.Page, settings.Page, home, resources.DetailPage, home })
        {
            shell.Stage.Navigation.Replace(page);
            for (int frame = 0; frame < 2; frame++)
            {
                var scene = shell.Render(new(1000, 650));
                var padding = shell.Tree.GetComponent<XsrUiElement>(shell.Content)!.Padding;
                AssertEqual(new XsrUiThickness(20, 16, 20, 16), padding);
                if (page == home)
                {
                    var content = scene.Nodes.Single(node => node.Entity == shell.Content).Rect;
                    var card = FindByKey(shell, scene, "CardAccount").Rect;
                    AssertClose(140, card.X);
                    AssertClose(68, card.Y);
                }
            }
        }
    }
}
