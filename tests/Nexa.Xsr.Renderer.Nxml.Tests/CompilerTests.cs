using Nexa.Xsr.Renderer;
using Nexa.Xsr;
namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private static void ExternalControlCatalogIsCompleteAndDeterministic()
    {
        AssertSequence(
            ["Button", "ContentHost", "Dialog", "HorizontalPager", "Image", "Navigation", "NavigationItem", "Notification", "Page", "Progress", "Shell", "StackPanel", "Text", "TextInput", "TitleBar", "VerticalPager"],
            NxmlControlCatalog.Names.ToArray());

        if (NxmlControlCatalog.Names is IList<string> names)
        {
            AssertThrows<NotSupportedException>(() => names[0] = "Mutated");
        }
    }

    private static void CompileFeedbackSurfaceControls()
    {
        NxmlHostIr notification = Compile("""
            <Notification xmlns="N" Key="notice" Label="Info notification" Orientation="Horizontal">
              <Text Content="Saved" />
            </Notification>
            """);
        AssertEqual("Notification", notification.Root.Kind);
        AssertEqual(NxmlRuntimeRecipe.StackLayout, notification.Root.Recipe);
        AssertEqual(XsrUiSemanticRole.Status, notification.Root.Role);
        AssertEqual("Info notification", notification.Root.Label);

        NxmlHostIr dialog = Compile("""
            <Dialog xmlns="N" Key="dialog" Label="Confirm" Orientation="Vertical">
              <Button Label="Continue" Command="dialog.accept" />
            </Dialog>
            """);
        AssertEqual("Dialog", dialog.Root.Kind);
        AssertEqual(NxmlRuntimeRecipe.StackLayout, dialog.Root.Recipe);
        AssertEqual(XsrUiSemanticRole.Dialog, dialog.Root.Role);
        AssertEqual("Confirm", dialog.Root.Label);
    }

    private static void CompileSimplePage()
    {
        NxmlHostIr ir = Compile("""
            <Page xmlns="N">
              <StackPanel Orientation="Horizontal" Spacing="4">
                <Text Content="ready" />
              </StackPanel>
            </Page>
            """);

        AssertEqual("Page", ir.Root.Kind);
        AssertEqual(NxmlRuntimeRecipe.Element, ir.Root.Recipe);
        AssertEqual("StackPanel", ir.Root.Children[0].Kind);
        AssertEqual(NxmlRuntimeRecipe.StackLayout, ir.Root.Children[0].Recipe);
        AssertEqual(XsrUiSemanticRole_None(), ir.Root.Children[0].Role);
        AssertEqual(XsrUiOrientation.Horizontal, ir.Root.Children[0].Orientation);
        AssertEqual(4, ir.Root.Children[0].Spacing);
        AssertEqual("Text", ir.Root.Children[0].Children[0].Kind);
        AssertEqual(NxmlRuntimeRecipe.Text, ir.Root.Children[0].Children[0].Recipe);
        AssertEqual("ready", ir.Root.Children[0].Children[0].Content);
    }

    private static void CompileTextStateBinding()
    {
        NxmlHostIr ir = Compile("""
            <Page xmlns="N">
              <Text Content="{state account.name}" />
            </Page>
            """);

        NxmlIrNode text = ir.Root.Children[0];
        AssertNull(text.Content);
        AssertEqual(1, text.Bindings.Count);
        AssertEqual("account.name", text.Bindings[0].State.Value);
        AssertEqual(XsrUiStateProperty.Text, text.Bindings[0].Property);
        AssertTrue(text.Bindings[0].DirtyKinds.HasFlag(XsrUiDirtyKinds.Paint));
    }

    private static void CompileVisibilityBinding()
    {
        NxmlHostIr ir = Compile("""
            <Page xmlns="N">
              <StackPanel IsVisible="{state panel.open}" Scroll="true" />
            </Page>
            """);

        NxmlIrNode stack = ir.Root.Children[0];
        AssertTrue(stack.Scrollable);
        AssertEqual(1, stack.Bindings.Count);
        AssertEqual("panel.open", stack.Bindings[0].State.Value);
        AssertEqual(XsrUiStateProperty.Visibility, stack.Bindings[0].Property);
        AssertEqual(XsrUiDirtyKinds.State, stack.Bindings[0].DirtyKinds);
    }

    private static void CompileButtonDefaultsAndCommand()
    {
        NxmlHostIr ir = Compile("""
            <Page xmlns="N">
              <Button Label="Save" Command="app.save" />
            </Page>
            """);

        NxmlIrNode button = ir.Root.Children[0];
        AssertEqual(NxmlRuntimeRecipe.CommandInput, button.Recipe);
        AssertTrue(button.Clickable);
        AssertTrue(button.Focusable);
        XsrSemanticId command = button.Command!.Value;
        AssertEqual("app.save", command.Value);
        AssertEqual(XsrUiSemanticRole_Button(), button.Role);
    }

    private static void CompileShellControlsAndStretchContract()
    {
        NxmlHostIr ir = Compile("""
            <Shell xmlns="N">
              <TitleBar />
              <StackPanel Orientation="Horizontal" StretchLastChild="true">
                <Navigation>
                  <NavigationItem Label="主页" Content="主页" Icon="lucide/play" Command="ui.navigation.home" />
                </Navigation>
                <ContentHost />
              </StackPanel>
            </Shell>
            """);

        AssertEqual(NxmlRuntimeRecipe.StackLayout, ir.Root.Recipe);
        AssertTrue(ir.Root.StretchLastChild);
        AssertEqual(XsrUiSemanticRole.TitleBar, ir.Root.Children[0].Role);
        NxmlIrNode body = ir.Root.Children[1];
        AssertTrue(body.StretchLastChild);
        AssertEqual(XsrUiSemanticRole.Navigation, body.Children[0].Role);
        AssertEqual(XsrUiSemanticRole.NavigationItem, body.Children[0].Children[0].Role);
        AssertEqual("主页", body.Children[0].Children[0].Content);
        AssertEqual("lucide/play", body.Children[0].Children[0].ImageSource);
    }

    private static void CompileThicknessAndSize()
    {
        NxmlHostIr ir = Compile("""
            <Page xmlns="N">
              <Text Content="x" Width="80" Height="24" Margin="4" Padding="1,2,3,4" />
            </Page>
            """);

        NxmlIrNode text = ir.Root.Children[0];
        AssertEqual(80, text.Width!.Value);
        AssertEqual(24, text.Height!.Value);
        AssertEqual(4, text.Margin.Left);
        AssertEqual(1, text.Padding.Left);
        AssertEqual(2, text.Padding.Top);
        AssertEqual(3, text.Padding.Right);
        AssertEqual(4, text.Padding.Bottom);
    }

    private static void CompileWeightedConstrainedAlignment()
    {
        NxmlHostIr ir = Compile("""
            <Page xmlns="N">
              <StackPanel Weight="0.92" MinWidth="240" MaxWidth="360"
                          MinHeight="100" MaxHeight="420"
                          HorizontalAlignment="End" VerticalAlignment="Center" />
            </Page>
            """);

        NxmlIrNode stack = ir.Root.Children[0];
        AssertEqual(0.92, stack.Weight);
        AssertEqual(240, stack.MinWidth!.Value);
        AssertEqual(360, stack.MaxWidth!.Value);
        AssertEqual(100, stack.MinHeight!.Value);
        AssertEqual(420, stack.MaxHeight!.Value);
        AssertEqual(XsrUiAlignment.End, stack.HorizontalAlignment);
        AssertEqual(XsrUiAlignment.Center, stack.VerticalAlignment);
    }

    private static void CompileSeparatesEntityKeysFromSemanticLabels()
    {
        NxmlHostIr ir = Compile("""
            <Page xmlns="N" Key="LaunchPage" Label="启动页">
              <Button Key="LaunchButton" Label="{state action.label}"
                      Content="{state action.label}" Command="ui.launch.primary" />
            </Page>
            """);

        AssertEqual("LaunchPage", ir.Root.Key);
        AssertEqual("启动页", ir.Root.Label);
        NxmlIrNode button = ir.Root.Children[0];
        AssertEqual("LaunchButton", button.Key);
        AssertNull(button.Label);
        AssertEqual(2, button.Bindings.Count);
        AssertTrue(button.Bindings.Any(binding =>
            binding.Property == XsrUiStateProperty.Text
            && binding.State.Value == "action.label"));
        AssertTrue(button.Bindings.Any(binding =>
            binding.Property == XsrUiStateProperty.SemanticLabel
            && binding.State.Value == "action.label"));
    }

    private static void CompileRejectsDuplicateEntityKeys()
    {
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N" Key="Duplicate">
              <Text Key="Duplicate" Content="x" />
            </Page>
            """));
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <Text Key="bad key" Content="x" />
            </Page>
            """));
    }

    private static void CompileRejectsUnknownElementAndProperty()
    {
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <Grid />
            </Page>
            """));
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <Text Whatever="1" />
            </Page>
            """));
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <StackPanel Command="app.x" />
            </Page>
            """));
    }

    private static void CompileRejectsMalformedValues()
    {
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <Text Content="x" Width="eight" />
            </Page>
            """));
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <StackPanel Orientation="Diagonal" />
            </Page>
            """));
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <Button Command="{state app.save}" />
            </Page>
            """));
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <Image />
            </Page>
            """));
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <Text Content="x" Width="NaN" />
            </Page>
            """));
    }

    private static void CompileRejectsBindingsAbsentFromTheControlModel()
    {
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <Text Content="x" Width="{state layout.width}" />
            </Page>
            """));
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <Image Source="{state image.source}" />
            </Page>
            """));
    }

    private static void CompileEnforcesGeneratedChildPolicy()
    {
        AssertThrows<NxmlCompileException>(() => Compile("""
            <Page xmlns="N">
              <Text Content="outer">
                <Button Label="invalid child" />
              </Text>
            </Page>
            """));
    }

    private static XsrUiSemanticRole XsrUiSemanticRole_None() => XsrUiSemanticRole.None;

    private static XsrUiSemanticRole XsrUiSemanticRole_Button() => XsrUiSemanticRole.Button;

    private static NxmlHostIr Compile(string text)
    {
        NxmlDocument document = NxmlParser.Parse(text.Replace("xmlns=\"N\"", "xmlns=\"https://nexa.dev/nxml/2026\""));
        return NxmlCompiler.Compile(document);
    }
}
