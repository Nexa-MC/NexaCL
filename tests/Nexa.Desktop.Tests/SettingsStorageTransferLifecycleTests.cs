using System.Text.Json.Nodes;
using Nexa.Desktop.Ui;
using Nexa.Services.Composition;
using Nexa.Services.Settings;
using Nexa.Services.Setup;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SettingsTransferExportRetainsUnsavedArgumentRowsCaretAndScroll()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        string instance = Path.Combine(fixture.TemporaryDirectory, "versions", "transfer-draft"); Directory.CreateDirectory(instance);
        File.WriteAllText(Path.Combine(instance, "transfer-draft.json"), new JsonObject
        { ["id"] = "transfer-draft", ["mainClass"] = "fixture.Main", ["_minecraftVersion"] = "1.20.1", ["libraries"] = new JsonArray() }.ToJsonString());
        var policy = fixture.Foundation.Host.SettingsPolicy;
        AssertTrue(policy.Set(new("game.jvm", SettingsLayer.Instance, new(SettingsOverrideMode.Custom, "-Dcommitted=true"), instance)).IsSuccess);
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, fixture.Foundation.Queries,
            fixture.Foundation.Commands, fixture.Store, fixture.Feedback, () => instance);
        var save = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var document = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        settings.ConfigureSettingsTransfer(_ => Task.FromResult<string?>(null), (text, _) => { document.TrySetResult(text); return save.Task; });
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        Wait(() => Find("SettingsNav.game", false).IsAssigned);
        Emit(fixture.Intents, "ui.settings.section", Find("SettingsNav.game")); fixture.Shell.Render(new(1100, 1100));
        settings.PrepareVisibleStartupAsync(CancellationToken.None).GetAwaiter().GetResult();
        Wait(() => Find("SettingsArgument.game.jvm.0", false).IsAssigned && fixture.Shell.Tree.GetComponent<XsrUiInput>(Find("SettingsExport"))!.Enabled);
        Emit(fixture.Intents, "ui.settings.argument.add", Find("SettingsArgumentAdd.game.jvm")); fixture.Shell.Render(new(1100, 1100));
        var first = Find("SettingsArgument.game.jvm.0"); var added = Find("SettingsArgument.game.jvm.1");
        var export = Find("SettingsExport"); var transfer = Find("SettingsTransfer");
        const string firstDraft = "-Dfirst=unsaved", addedDraft = "-Dliteral=<xml>{data value}";
        fixture.Shell.Renderer.SetTextInputValue(first, firstDraft); fixture.Shell.Renderer.SetTextInputValue(added, addedDraft);
        AssertTrue(fixture.Shell.Renderer.Focus(added, showIndicator: false)); fixture.Shell.Renderer.EditText(XsrUiTextEdit.Left);
        var input = fixture.Shell.Tree.GetComponent<XsrUiTextInput>(added)!;
        int caretStart = input.SelectionStart, caretEnd = input.SelectionEnd;
        var transition = new XsrUiTransition(); fixture.Shell.Tree.SetComponent(added, transition);
        var viewport = ScrollAncestor(added); var scroll = fixture.Shell.Tree.GetComponent<XsrUiScroll>(viewport)!;
        scroll.OffsetY = 96; fixture.Shell.Tree.MarkDirty(viewport, XsrUiDirtyKinds.Layout); fixture.Shell.Render(new(1100, 1100));
        double offset = scroll.OffsetY;
        AssertTrue(fixture.Shell.Tree.GetComponent<NxmlDynamicSlot>(Find("SettingsArgumentRows.game.jvm")) is not null);
        Emit(fixture.Intents, "ui.settings.data.export", export);
        Wait(() => document.Task.IsCompleted);
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiInput>(export)!.Enabled); AssertRetained();
        AssertFalse(document.Task.Result.Contains(firstDraft, StringComparison.Ordinal)); AssertFalse(document.Task.Result.Contains(addedDraft, StringComparison.Ordinal));
        save.SetResult(false);
        Wait(() => fixture.Shell.Tree.GetComponent<XsrUiInput>(export)!.Enabled); AssertRetained();
        AssertEqual("-Dcommitted=true", policy.Read(new(instance)).Value!.Values.Single(value => value.Key == "game.jvm").Value.Value);

        void AssertRetained()
        {
            AssertEqual(first, Find("SettingsArgument.game.jvm.0")); AssertEqual(added, Find("SettingsArgument.game.jvm.1"));
            AssertEqual(export, Find("SettingsExport")); AssertEqual(transfer, Find("SettingsTransfer")); AssertEqual(added, fixture.Shell.Renderer.Focused);
            AssertEqual(firstDraft, fixture.Shell.Tree.GetComponent<XsrUiTextInput>(first)!.ReadDraft()); AssertEqual(addedDraft, input.ReadDraft());
            AssertEqual(caretStart, input.SelectionStart); AssertEqual(caretEnd, input.SelectionEnd);
            AssertTrue(ReferenceEquals(input, fixture.Shell.Tree.GetComponent<XsrUiTextInput>(added)));
            AssertTrue(ReferenceEquals(transition, fixture.Shell.Tree.GetComponent<XsrUiTransition>(added)));
            AssertTrue(ReferenceEquals(scroll, fixture.Shell.Tree.GetComponent<XsrUiScroll>(viewport))); AssertEqual(offset, scroll.OffsetY);
        }
        XsrUiEntityId ScrollAncestor(XsrUiEntityId entity)
        {
            for (var current = entity; current.IsAssigned; current = fixture.Shell.Tree.Parent(current))
                if (fixture.Shell.Tree.GetComponent<XsrUiScroll>(current) is not null) return current;
            throw new InvalidOperationException("The settings editor requires its actual scroll viewport.");
        }
        void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        { fixture.Shell.Render(new(1100, 1100)); return condition(); }, TimeSpan.FromSeconds(5)));
        XsrUiEntityId Find(string name, bool required = true)
        {
            XsrUiEntityId found = default;
            fixture.Shell.Tree.Walk(settings.Page, entity => { if (fixture.Shell.Tree.Name(entity) == name) found = entity; return true; });
            if (required) AssertTrue(found.IsAssigned); return found;
        }
    }

    private static void StorageBusyReloadRetainsCatalogDraftCaretAndOwnedDirectoryRows()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        fixture.Controller.WaitUntilIdle().GetAwaiter().GetResult();
        string data = Path.Combine(fixture.TemporaryDirectory, "storage-draft"); Directory.CreateDirectory(data);
        using var preferences = new StoragePreferencesService(new(data), Path.Combine(fixture.TemporaryDirectory, "storage-draft.json"), isIdle: () => true);
        var runtime = FoundationRuntimeComposer.ComposeWithStorage(fixture.Foundation.Host, preferences);
        using var settings = new SettingsPageController(fixture.Shell, fixture.Intents, runtime.Queries, runtime.Commands, fixture.Store, fixture.Feedback);
        var selected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int picks = 0, closeRequests = 0;
        settings.ConfigureStoragePreferences(_ => { Interlocked.Increment(ref picks); return selected.Task; }, () => closeRequests++);
        fixture.Shell.Renderer.ReducedMotion = true; fixture.Shell.Stage.Navigation.Replace(settings.Page);
        Wait(() => Find("SettingsNav.storage", false).IsAssigned);
        Emit(fixture.Intents, "ui.settings.section", Find("SettingsNav.storage")); fixture.Shell.Render(new(1100, 1000));
        Wait(() => Find("SettingsInput.storage.backup-keep-count", false).IsAssigned && fixture.Shell.Tree.GetComponent<XsrUiInput>(Find("SettingsStorageMove"))!.Enabled
            && DirectoryValue() == Path.Combine(data, "settings"));
        var inputEntity = Find("SettingsInput.storage.backup-keep-count"); var move = Find("SettingsStorageMove"); var cleanup = Find("SettingsStorageCleanTemporary");
        var location = Find("SettingsStorageLocation"); var directories = Find("SettingsStorageDirectories"); var settingsPath = fixture.Shell.Tree.Children(Find("SettingsStorageValue.Settings")).Single();
        fixture.Shell.Renderer.SetTextInputValue(inputEntity, "117"); AssertTrue(fixture.Shell.Renderer.Focus(inputEntity, showIndicator: false));
        fixture.Shell.Renderer.EditText(XsrUiTextEdit.Left);
        var input = fixture.Shell.Tree.GetComponent<XsrUiTextInput>(inputEntity)!; int caretStart = input.SelectionStart, caretEnd = input.SelectionEnd;
        var transition = new XsrUiTransition(); fixture.Shell.Tree.SetComponent(inputEntity, transition);
        var viewport = ScrollAncestor(inputEntity); var scroll = fixture.Shell.Tree.GetComponent<XsrUiScroll>(viewport)!;
        scroll.OffsetY = 96; fixture.Shell.Tree.MarkDirty(viewport, XsrUiDirtyKinds.Layout); fixture.Shell.Render(new(1100, 1000)); double offset = scroll.OffsetY;
        Emit(fixture.Intents, "ui.settings.storage.move", move); Wait(() => Volatile.Read(ref picks) == 1);
        AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiInput>(move)!.Enabled); AssertFalse(fixture.Shell.Tree.GetComponent<XsrUiInput>(cleanup)!.Enabled); AssertRetained();
        selected.SetResult(null); Wait(() => fixture.Shell.Tree.GetComponent<XsrUiInput>(move)!.Enabled); AssertRetained();
        AssertEqual(0, closeRequests); AssertTrue(fixture.Feedback.Snapshot().Dialog is null);
        AssertEqual("32", fixture.Foundation.Host.SettingsPolicy.Read(new()).Value!.Values.Single(value => value.Key == "storage.backup-keep-count").Value.Value);

        void AssertRetained()
        {
            AssertEqual(inputEntity, Find("SettingsInput.storage.backup-keep-count")); AssertEqual(move, Find("SettingsStorageMove")); AssertEqual(cleanup, Find("SettingsStorageCleanTemporary"));
            AssertEqual(location, Find("SettingsStorageLocation")); AssertEqual(directories, Find("SettingsStorageDirectories"));
            AssertEqual(settingsPath, fixture.Shell.Tree.Children(Find("SettingsStorageValue.Settings")).Single()); AssertEqual(inputEntity, fixture.Shell.Renderer.Focused);
            AssertEqual("117", input.ReadDraft()); AssertEqual(caretStart, input.SelectionStart); AssertEqual(caretEnd, input.SelectionEnd);
            AssertTrue(ReferenceEquals(input, fixture.Shell.Tree.GetComponent<XsrUiTextInput>(inputEntity)));
            AssertTrue(ReferenceEquals(transition, fixture.Shell.Tree.GetComponent<XsrUiTransition>(inputEntity)));
            AssertTrue(ReferenceEquals(scroll, fixture.Shell.Tree.GetComponent<XsrUiScroll>(viewport))); AssertEqual(offset, scroll.OffsetY);
        }
        string? DirectoryValue()
        {
            var slot = Find("SettingsStorageValue.Settings", false);
            return slot.IsAssigned && fixture.Shell.Tree.Children(slot).Count == 1 ? fixture.Shell.Tree.GetComponent<XsrUiText>(fixture.Shell.Tree.Children(slot)[0])?.Content : null;
        }
        XsrUiEntityId ScrollAncestor(XsrUiEntityId entity)
        {
            for (var current = entity; current.IsAssigned; current = fixture.Shell.Tree.Parent(current))
                if (fixture.Shell.Tree.GetComponent<XsrUiScroll>(current) is not null) return current;
            throw new InvalidOperationException("The settings editor requires its actual scroll viewport.");
        }
        void Wait(Func<bool> condition) => AssertTrue(SpinWait.SpinUntil(() =>
        { fixture.Shell.Render(new(1100, 1000)); return condition(); }, TimeSpan.FromSeconds(5)));
        XsrUiEntityId Find(string name, bool required = true)
        {
            XsrUiEntityId found = default;
            fixture.Shell.Tree.Walk(settings.Page, entity => { if (fixture.Shell.Tree.Name(entity) == name) found = entity; return true; });
            if (required) AssertTrue(found.IsAssigned); return found;
        }
    }
}
