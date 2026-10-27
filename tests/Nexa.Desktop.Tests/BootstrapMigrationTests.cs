using Nexa.Desktop.Ui;
using Nexa.Services.Accounts;
using Nexa.Services.Composition;
using Nexa.Services.Setup;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.State;
namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void AccountOwnedRowsRetainDraftsAndRejectHiddenSources()
    {
        string candidateRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nexa-account-form-" + Guid.NewGuid().ToString("N")));
        string candidateDirectory = Path.Combine(candidateRoot, new string('a', 70)); Directory.CreateDirectory(candidateDirectory);
        string path = Path.Combine(candidateDirectory, "设置 {state private}.json"); File.WriteAllText(path, "{}");
        try
        {
            using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), imports: new LegacyProfileImport(() => [path]));
            SeedFixtureOwner(fixture);
            var shell = fixture.Shell; shell.Renderer.ReducedMotion = true;
            var language = new UiLocalizationCatalog(); language.SetLanguage("zh-Hans"); shell.Renderer.TextLocalizer = language.Translate;
            Emit(fixture.Intents, "ui.account.import"); var scene = shell.Render(new(1000, 650));
            var slot = FindEntity(shell, "ImportCandidates"); AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(slot) is not null);
            AssertTrue(SpinWait.SpinUntil(() =>
            {
                scene = shell.Render(new(1000, 650));
                return shell.Tree.Children(slot).Any(row => shell.Tree.GetComponent<XsrUiSemantic>(row)?.Label == path);
            }, TimeSpan.FromSeconds(3)));
            var choice = shell.Tree.Children(slot).Single(row => shell.Tree.GetComponent<XsrUiSemantic>(row)?.Label == path);
            AssertTrue(shell.Tree.Name(choice).Length <= 128); AssertEqual(slot, shell.Tree.Parent(choice));
            var input = FindEntity(shell, "ImportPath"); shell.Renderer.SetTextInputValue(input, path); shell.Renderer.Focus(input, showIndicator: false); shell.Renderer.EditText(XsrUiTextEdit.Left);
            var draft = shell.Tree.GetComponent<XsrUiTextInput>(input)!; int caret = draft.SelectionEnd;
            language.SetLanguage("en"); scene = shell.Render(new(850, 520));
            AssertEqual(choice, shell.Tree.Children(slot).Single(row => shell.Tree.GetComponent<XsrUiSemantic>(row)?.Label == path));
            AssertEqual(input, shell.Renderer.Focused); AssertEqual(path, draft.ReadDraft()); AssertEqual(caret, draft.SelectionEnd);
            AssertEqual("Use discovered profiles", shell.Tree.GetComponent<XsrUiText>(choice)!.Content);
            Emit(fixture.Intents, "ui.account.close"); shell.Render(new(1000, 650));
            Emit(fixture.Intents, "ui.account.add"); shell.Render(new(1000, 650)); Emit(fixture.Intents, "ui.account.offline"); shell.Render(new(1000, 650));
            Emit(fixture.Intents, "ui.account.choice", choice); shell.Render(new(1000, 650));
            AssertEqual("", fixture.Store.Read<string>(fixture.Store.Resolve(AccountFormState.ImportPath)).Value ?? "");
            Emit(fixture.Intents, "ui.account.back"); shell.Render(new(1000, 650)); Emit(fixture.Intents, "ui.account.third-party"); scene = shell.Render(new(1000, 650));
            var secret = FindEntity(shell, "AuthPassword"); shell.Renderer.SetTextInputValue(secret, "not-a-real-password");
            language.SetLanguage("zh-Hant"); shell.Render(new(820, 520)); AssertEqual("not-a-real-password", shell.Tree.GetComponent<XsrUiTextInput>(secret)!.ReadDraft());
            Emit(fixture.Intents, "ui.account.close"); shell.Render(new(1000, 650)); AssertEqual("", shell.Tree.GetComponent<XsrUiTextInput>(secret)!.ReadDraft());
        }
        finally { Directory.Delete(candidateRoot, true); }
    }
    private static void FirstRunOwnedWorkspaceRetainsTransitionsAndRetiresPicker()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var shell = fixture.Shell; shell.Renderer.ReducedMotion = true;
        var language = new UiLocalizationCatalog(); language.SetLanguage("en"); shell.Renderer.TextLocalizer = language.Translate;
        string locator = Path.Combine(fixture.TemporaryDirectory, "setup-locator.json");
        var service = new FirstRunService(Path.Combine(fixture.TemporaryDirectory, "data"), locator);
        var picker = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var setup = new FirstRunController(shell, fixture.Intents, fixture.Store, FirstRunRuntimeComposer.Compose(service),
            service.Read() with { TelemetryRequired = true }, () => picker.Task, () => { });
        var scene = shell.Render(new(1000, 650));
        var title = FindEntity(shell, "SetupTitle"); var transition = shell.Tree.GetComponent<XsrUiTransition>(title)!;
        var workspace = FindEntity(shell, "SetupFrame"); AssertTrue(shell.Tree.GetComponent<NxmlDynamicSlot>(shell.Tree.Parent(workspace)) is not null);
        Emit(fixture.Intents, "ui.setup.next"); scene = shell.Render(new(820, 520));
        string original = FindByKey(shell, scene, "SetupPath").Text!;
        Emit(fixture.Intents, "ui.setup.browse"); shell.Render(new(820, 520));
        shell.Stage.Navigation.Replace(fixture.Controller.Versions.Page); shell.Render(new(820, 520));
        shell.Stage.Navigation.Replace(setup.Page); shell.Render(new(820, 520));
        picker.SetResult(Path.Combine(fixture.TemporaryDirectory, "stale directory"));
        AssertTrue(SpinWait.SpinUntil(() =>
        {
            scene = shell.Render(new(820, 520));
            return FindByKey(shell, scene, "SetupBrowse").IsEnabled;
        }, TimeSpan.FromSeconds(3)));
        AssertEqual(original, FindByKey(shell, scene, "SetupPath").Text);
        Emit(fixture.Intents, "ui.setup.next"); scene = shell.Render(new(1000, 650));
        AssertEqual(2, setup.Step); AssertFalse(HasKey(shell, scene, "SetupPrivate"));
        var share = FindByKey(shell, scene, "SetupShare"); AssertFalse(share.IsEnabled);
        AssertTrue(share.Text!.Contains("Required", StringComparison.OrdinalIgnoreCase));
        Emit(fixture.Intents, "ui.setup.private"); scene = shell.Render(new(1000, 650)); AssertFalse(HasKey(shell, scene, "SetupPrivate"));
        language.SetLanguage("zh-Hant"); scene = shell.Render(new(820, 520));
        AssertEqual(title, FindEntity(shell, "SetupTitle")); AssertTrue(ReferenceEquals(transition, shell.Tree.GetComponent<XsrUiTransition>(title)));
        AssertEqual("2", FindByKey(shell, scene, "SetupTitle").TransitionKey); AssertEqual(fixture.Store.Resolve(FirstRunState.Step), transition.BoundKey); AssertFalse(File.Exists(locator));
        Emit(fixture.Intents, "ui.setup.next"); scene = shell.Render(new(820, 520));
        AssertTrue(FindByKey(shell, scene, "SetupSummary").Text!.Contains(original, StringComparison.Ordinal));
        AssertFalse(File.Exists(locator));
    }
}
