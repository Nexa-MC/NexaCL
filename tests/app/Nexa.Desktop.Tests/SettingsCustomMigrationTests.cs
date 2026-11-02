using System.Text.Json.Nodes;
using Nexa.Desktop.Ui;
using Nexa.Services.Composition;
using Nexa.Services.Logging;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;
using Nexa.Services.Setup;
using Nexa.Services.Updates;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void CustomIdentityProjectionPreservesLiveDraftsAndOwnedNotes()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        string instance = Path.Combine(fixture.TemporaryDirectory, "versions", "identity-projection"); Directory.CreateDirectory(instance);
        File.WriteAllText(Path.Combine(instance, "identity-projection.json"), new JsonObject
        { ["id"] = "identity-projection", ["mainClass"] = "fixture.Main", ["_minecraftVersion"] = "1.20.1", ["libraries"] = new JsonArray() }.ToJsonString());
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback, () => instance);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        Wait(() => Find("InstanceNoteInput.0", false).IsAssigned);
        settings.PrepareVisibleStartupAsync(CancellationToken.None).GetAwaiter().GetResult();
        Wait(() => Find("InstanceNoteInput.0", false).IsAssigned);
        var field = Find("InstanceIdentityInput.description"); var note = Find("InstanceNoteInput.0");
        var star = Find("InstanceIdentityStar"); var notesSlot = Find("InstanceNotes.Rows");
        AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(notesSlot) is not null);
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiTextInput>(field)!.PreserveTabs);
        AssertEqual(8192, fixture.Shell.Tree.GetComponent<XsrUiTextInput>(field)!.MaximumLength);
        const string draft = "Literal <Text /> {data name}\tvalue";
        fixture.Shell.Renderer.SetTextInputValue(field, draft); fixture.Shell.Renderer.SetTextInputValue(note, "Unsaved note");
        fixture.Shell.Renderer.Focus(field, showIndicator: false);
        fixture.Shell.Renderer.EditText(XsrUiTextEdit.Left);
        var input = fixture.Shell.Tree.GetComponent<XsrUiTextInput>(field)!;
        int selectionStart = input.SelectionStart, selectionEnd = input.SelectionEnd;
        var transition = new XsrUiTransition(); fixture.Shell.Tree.SetComponent(field, transition);
        Emit(fixture.Intents, "ui.settings.instance-identity.action", star);
        fixture.Shell.Render(new(960, 720));
        AssertEqual(field, Find("InstanceIdentityInput.description")); AssertEqual(note, Find("InstanceNoteInput.0"));
        AssertEqual(star, Find("InstanceIdentityStar")); AssertEqual(field, fixture.Shell.Renderer.Focused);
        AssertTrue(ReferenceEquals(input, fixture.Shell.Tree.GetComponent<XsrUiTextInput>(field)));
        AssertTrue(ReferenceEquals(transition, fixture.Shell.Tree.GetComponent<XsrUiTransition>(field)));
        AssertEqual(draft, input.ReadDraft()); AssertEqual(selectionStart, input.SelectionStart); AssertEqual(selectionEnd, input.SelectionEnd);
        AssertEqual("Unsaved note", fixture.Shell.Tree.GetComponent<XsrUiTextInput>(note)!.ReadDraft());
        AssertEqual("已收藏 · 点击取消", fixture.Shell.Tree.GetComponent<XsrUiText>(star)!.Content);
        AssertFalse(InstanceIdentityService.ReadAsync(new(instance)).GetAwaiter().GetResult().Fields.Starred);
        Emit(fixture.Intents, "ui.settings.instance-identity.action", Find("InstanceNoteAdd"));
        fixture.Shell.Render(new(960, 720));
        AssertEqual(note, Find("InstanceNoteInput.0")); AssertTrue(Find("InstanceNoteInput.1", false).IsAssigned);
        AssertEqual("Unsaved note", fixture.Shell.Tree.GetComponent<XsrUiTextInput>(note)!.ReadDraft());

        void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        { fixture.Shell.Render(new(960, 720)); return condition(); }, TimeSpan.FromSeconds(5)));
        XsrUiEntityId Find(string name, bool required = true)
        {
            XsrUiEntityId found = default;
            fixture.Shell.Tree.Walk(settings.Page, entity => { if (fixture.Shell.Tree.Name(entity) == name) found = entity; return true; });
            if (required) AssertTrue(found.IsAssigned); return found;
        }
    }

    private static void CustomUpdateProjectionRetainsCardAndRejectsRetiredActions()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var policy = fixture.Foundation.Host.SettingsPolicy;
        AssertTrue(policy.Set(new("updates.auto-check", SettingsLayer.Global, new(SettingsOverrideMode.Custom, "false"))).IsSuccess);
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback);
        var builder = new XsrQueryRouterBuilder();
        List<TaskCompletionSource<XsrResult<NexaUpdateStatus>>> calls = [];
        builder.Register<NexaUpdateQuery, NexaUpdateStatus>(NexaUpdateContract.Check, (_, _) =>
        {
            var completion = new TaskCompletionSource<XsrResult<NexaUpdateStatus>>(TaskCreationOptions.RunContinuationsAsynchronously);
            calls.Add(completion); return new(completion.Task);
        });
        List<Uri> opened = [];
        settings.ConfigureUpdates(builder.Build(new NoopDispatchObserver()), new("2.0.0", "win-x64", "stable"), opened.Add);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        Wait(() => Find("SettingsNav.advanced", false).IsAssigned);
        Emit(fixture.Intents, "ui.settings.section", Find("SettingsNav.advanced")); fixture.Shell.Render(new(1000, 800));
        var card = Find("SettingsUpdateCard"); var check = Find("SettingsCheckUpdate");
        AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(Find("SettingsUpdateActions")) is not null);
        var transition = new XsrUiTransition(); fixture.Shell.Tree.SetComponent(check, transition);
        fixture.Shell.Renderer.Focus(check, showIndicator: false);
        Emit(fixture.Intents, "ui.settings.update.check", check); fixture.Shell.Render(new(1000, 800));
        AssertEqual(1, calls.Count); AssertEqual(card, Find("SettingsUpdateCard")); AssertEqual(check, Find("SettingsCheckUpdate"));
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiInput>(check)!.Enabled);
        const string literalVersion = "literal <version> {state private}";
        calls[0].SetResult(XsrResult.Success(new NexaUpdateStatus(new(literalVersion, "https://example.com/installer", "https://example.com/portable", "https://example.com/notes"))));
        Wait(() => Find("SettingsDownloadUpdate", false).IsAssigned);
        var download = Find("SettingsDownloadUpdate");
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiText>(Find("SettingsUpdateStatus"))!.Content.Contains(literalVersion, StringComparison.Ordinal));
        AssertEqual(card, Find("SettingsUpdateCard")); AssertEqual(check, Find("SettingsCheckUpdate"));
        AssertTrue(ReferenceEquals(transition, fixture.Shell.Tree.GetComponent<XsrUiTransition>(check)));
        // A live card member cannot impersonate another member's command.
        Emit(fixture.Intents, "ui.settings.update.download", check);
        Emit(fixture.Intents, "ui.settings.update.notes", download);
        Emit(fixture.Intents, "ui.settings.update.check", download);
        fixture.Shell.Render(new(1000, 800));
        AssertEqual(0, opened.Count); AssertEqual(1, calls.Count);
        Emit(fixture.Intents, "ui.settings.update.download", download); fixture.Shell.Render(new(1000, 800));
        AssertEqual("https://example.com/installer", opened.Single().AbsoluteUri);
        opened.Clear();
        Emit(fixture.Intents, "ui.settings.update.check", check); fixture.Shell.Render(new(1000, 800)); AssertEqual(2, calls.Count);
        calls[1].SetResult(XsrResult.Success(new NexaUpdateStatus(null)));
        Wait(() => !fixture.Shell.Tree.IsAlive(download));
        Emit(fixture.Intents, "ui.settings.update.download", download); fixture.Shell.Render(new(1000, 800));
        AssertEqual(0, opened.Count); AssertEqual(card, Find("SettingsUpdateCard")); AssertEqual(check, Find("SettingsCheckUpdate"));

        void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        { fixture.Shell.Render(new(1000, 800)); return condition(); }, TimeSpan.FromSeconds(5)));
        XsrUiEntityId Find(string name, bool required = true)
        {
            XsrUiEntityId found = default;
            fixture.Shell.Tree.Walk(settings.Page, entity => { if (fixture.Shell.Tree.Name(entity) == name) found = entity; return true; });
            if (required) AssertTrue(found.IsAssigned); return found;
        }
    }
    private static async ValueTask CustomContentWorkspaceRetainsAiDraftsThroughConfirmationAndClearsHiddenSecrets()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        AssertTrue(fixture.Foundation.Host.SettingsPolicy.Set(new("diagnostics.ai.enabled", SettingsLayer.Global,
            new(SettingsOverrideMode.Custom, "true"))).IsSuccess);
        string directory = Path.Combine(fixture.TemporaryDirectory, "custom-workspace"); Directory.CreateDirectory(directory);
        using var storage = new StoragePreferencesService(new(directory), Path.Combine(fixture.TemporaryDirectory, "storage.json"));
        var backups = new ContentBackupService(Path.Combine(directory, "backups"));
        await using var history = new DurableDiagnosticHistorySink(Path.Combine(directory, "history"));
        var runtime = FoundationRuntimeComposer.ComposeWithStorageAndMedia(fixture.Foundation.Host, storage,
            static (pixels, _, _, _, _) => pixels.ToArray(),
            configureRoutes: (commands, queries) => ContentWorkspaceRuntime.Register(commands, queries, backups, new LegacyMigrationService(), history));
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, runtime.Queries, runtime.Commands, fixture.Store, fixture.Feedback);
        const string raw = "<Text /> {data endpoint}\nLiteral raw preview";
        settings.ConfigureContentWorkspace(_ => Task.FromResult<string?>(directory), _ => Task.FromResult<string?>(null), () => raw);
        settings.ConfigureDiagnosticAi(true);
        var outsidePage = fixture.Shell.Stage.Navigation.Current;
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        Wait(() => Find("SettingsNav.storage", false).IsAssigned);
        Emit(fixture.Intents, "ui.settings.section", Find("SettingsNav.storage"));
        Wait(() => Find("DiagnosticAi.endpoint", false).IsAssigned && fixture.Shell.Tree.GetComponent<XsrUiInput>(Find("ContentWorkspace.Capture"))!.Enabled);
        var endpoint = Find("DiagnosticAi.endpoint"); var key = Find("DiagnosticAi.key"); var captureAction = Find("ContentWorkspace.Capture");
        fixture.Shell.Renderer.SetTextInputValue(endpoint, "https://literal.example/{data endpoint}");
        fixture.Shell.Renderer.SetTextInputValue(key, "ephemeral-custom-secret"); fixture.Shell.Renderer.Focus(endpoint, showIndicator: false);
        fixture.Shell.Renderer.EditText(XsrUiTextEdit.Left);
        var input = fixture.Shell.Tree.GetComponent<XsrUiTextInput>(endpoint)!;
        int start = input.SelectionStart, end = input.SelectionEnd;
        var transition = new XsrUiTransition(); fixture.Shell.Tree.SetComponent(endpoint, transition);
        Emit(fixture.Intents, "ui.settings.storage.workspace", captureAction);
        Wait(() => fixture.Feedback.Snapshot().Dialog is not null);
        AssertEqual(endpoint, Find("DiagnosticAi.endpoint")); AssertEqual(captureAction, Find("ContentWorkspace.Capture"));
        AssertEqual(key, Find("DiagnosticAi.key")); AssertEqual(endpoint, fixture.Shell.Renderer.Focused);
        AssertEqual("https://literal.example/{data endpoint}", input.ReadDraft()); AssertEqual(start, input.SelectionStart); AssertEqual(end, input.SelectionEnd);
        AssertTrue(ReferenceEquals(transition, fixture.Shell.Tree.GetComponent<XsrUiTransition>(endpoint)));
        var dialog = fixture.Feedback.Snapshot().Dialog!;
        AssertFalse(dialog.LocalizeMessage); AssertTrue(dialog.Message.Contains(directory, StringComparison.Ordinal));
        fixture.Feedback.ResolveDialog(dialog.Id, false);
        Wait(() => fixture.Shell.Tree.GetComponent<XsrUiInput>(captureAction)!.Enabled);
        AssertEqual(endpoint, Find("DiagnosticAi.endpoint")); AssertEqual("ephemeral-custom-secret", fixture.Shell.Tree.GetComponent<XsrUiTextInput>(key)!.ReadDraft());
        fixture.Shell.Stage.Navigation.Replace(outsidePage); fixture.Shell.Render(new(1000, 800));
        AssertEqual("", fixture.Shell.Tree.GetComponent<XsrUiTextInput>(key)!.ReadDraft());
        fixture.Shell.Stage.Navigation.Replace(settings.Page); fixture.Shell.Render(new(1000, 800));
        AssertEqual("", fixture.Shell.Tree.GetComponent<XsrUiTextInput>(Find("DiagnosticAi.key"))!.ReadDraft());
        AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiInput>(Find("ContentWorkspace.Capture"))!.Enabled);

        void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        { fixture.Shell.Render(new(1000, 800)); return condition(); }, TimeSpan.FromSeconds(5)));
        XsrUiEntityId Find(string name, bool required = true)
        {
            XsrUiEntityId found = default;
            fixture.Shell.Tree.Walk(settings.Page, entity => { if (fixture.Shell.Tree.Name(entity) == name) found = entity; return true; });
            if (required) AssertTrue(found.IsAssigned); return found;
        }
    }

}
