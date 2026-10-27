using Nexa.Desktop.Ui;
using Nexa.Services.Resources;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ResourceContinuousListAppendsOnceAndRetainsRowsScrollAndDraft()
    {
        using var f = new ContinuousResourceHarness();
        AssertTrue(f.Scene.Nodes.Any(node => node.ProgressRing is { Indeterminate: true }));
        var searchDuringLoad = FindByKey(f.Fixture.Shell, f.Scene, "ResourceSearch");
        AssertTrue(searchDuringLoad.IsEnabled);
        foreach (string key in new[] { "ResourceGame", "ResourceLoader", "ResourceProvider", "ResourceTag" })
            AssertTrue(FindByKey(f.Fixture.Shell, f.Scene, key).IsClickable);
        var region = FindByKey(f.Fixture.Shell, f.Scene, "ResourceResults").Rect;
        var filters = FindByKey(f.Fixture.Shell, f.Scene, "ResourceFilters").Rect;
        AssertTrue(region.Y >= filters.Y + filters.Height);
        var loading = FindByKey(f.Fixture.Shell, f.Scene, "ResourceInitialLoading").Rect;
        var initialRing = f.Scene.Nodes.Single(node => node.ProgressRing is not null).Rect;
        AssertClose(region.X + region.Width / 2, loading.X + loading.Width / 2);
        AssertClose(region.Y + region.Height / 2, loading.Y + loading.Height / 2);
        AssertClose(40, initialRing.Width); AssertClose(40, initialRing.Height);
        f.Click("ResourceProvider");
        f.RenderFrames(1);
        AssertTrue(f.Scene.Nodes.Any(node => f.Fixture.Shell.Tree.Name(node.Entity) == "ResourceFilterCard"));
        f.Click("ResourceFilterDismiss");
        AssertEqual(1, f.Reads.Count);
        f.Complete(0, ContinuousProjects("First", 20), hasMore: true);
        f.Pump(() => f.Rows.Length > 0);
        double readyViewportHeight = FindByKey(f.Fixture.Shell, f.Scene, "ResourceResults").Rect.Height;
        var search = FindByKey(f.Fixture.Shell, f.Scene, "ResourceSearch").Entity;
        f.Fixture.Shell.Renderer.SetTextInputValue(search, "unsubmitted draft");
        f.Fixture.Shell.Renderer.SetTextInputValue(FindByKey(f.Fixture.Shell, f.Scene, "ResourceGame").Entity, "unsubmitted game");
        f.Fixture.Shell.Renderer.SetTextInputValue(FindByKey(f.Fixture.Shell, f.Scene, "ResourceLoader").Entity, "unsubmitted loader");
        f.ScrollToBottom();
        f.Pump(() => f.Reads.Count == 2);
        AssertTrue(f.Scene.Nodes.Any(node => node.ProgressRing is { Indeterminate: true }));
        var footer = FindByKey(f.Fixture.Shell, f.Scene, "ResourcePagination").Rect;
        var append = FindByKey(f.Fixture.Shell, f.Scene, "ResourceAppendLoading");
        var appendRing = f.Scene.Nodes.Single(node => node.ProgressRing is not null).Rect;
        var appendHint = f.Scene.Nodes.Single(node => node.Text is not null && f.Fixture.Shell.Tree.Parent(node.Entity) == append.Entity).Rect;
        AssertTrue(footer.Height <= 34);
        AssertClose(readyViewportHeight, FindByKey(f.Fixture.Shell, f.Scene, "ResourceResults").Rect.Height);
        AssertTrue(appendRing.Y >= footer.Y && appendRing.Y + appendRing.Height <= footer.Y + footer.Height);
        AssertTrue(appendHint.Y >= footer.Y && appendHint.Y + appendHint.Height <= footer.Y + footer.Height);
        AssertTrue(appendRing.X + appendRing.Width <= appendHint.X);
        var anchor = f.Row("First19");
        double offset = f.Snapshot.OffsetY;
        double height = f.Snapshot.ContentHeight;
        AssertTrue(offset > 0);
        AssertEqual(1, f.Reads[1].Query.Page);
        AssertEqual(f.Reads[0].Query with { Page = 1 }, f.Reads[1].Query);
        AssertEqual("", f.Reads[1].Query.Text);
        AssertEqual("", f.Reads[1].Query.GameVersion);
        AssertEqual("", f.Reads[1].Query.Loader);
        f.RenderFrames(6);
        AssertEqual(2, f.Reads.Count);
        f.Complete(1, ContinuousProjects("Second", 20), hasMore: false);
        f.Pump(() => f.Snapshot.ContentHeight > height);
        AssertEqual(anchor, f.Row("First19"));
        AssertClose(offset, f.Snapshot.OffsetY);
        AssertEqual(search, FindByKey(f.Fixture.Shell, f.Scene, "ResourceSearch").Entity);
        AssertEqual("unsubmitted draft", f.Fixture.Shell.Tree.GetComponent<XsrUiTextInput>(search)!.ReadDraft());
        AssertFalse(f.NextEnabled);

        f.Click("ResourceDetails.First19");
        f.Pump(() => f.Fixture.Shell.Stage.Navigation.Current == f.Page.DetailPage);
        f.RenderFrames(6);
        AssertEqual(2, f.Reads.Count);
        f.Fixture.Shell.Stage.Navigation.Pop();
        f.RenderFrames(2);
        AssertEqual(anchor, f.Row("First19"));
        AssertClose(offset, f.Snapshot.OffsetY);
        AssertEqual("unsubmitted draft", f.Fixture.Shell.Tree.GetComponent<XsrUiTextInput>(search)!.ReadDraft());

        // The window could fit three guessed columns, but its measured list only fits two.
        f.Size = new(960, 600); f.RenderFrames(3);
        var list = f.Page.Find("ResourceList");
        f.Fixture.Shell.Tree.GetComponent<XsrUiScroll>(list)!.OffsetY = 1320;
        f.Fixture.Shell.Tree.MarkDirty(list, XsrUiDirtyKinds.Layout); f.RenderFrames(3);
        double returnOffset = f.Snapshot.OffsetY;
        string retained = f.Fixture.Shell.Tree.Name(f.Rows.Last()).Replace("ResourceProject.", "ResourceDetails.", StringComparison.Ordinal);
        f.Click(retained); f.RenderFrames(3);
        f.Fixture.Shell.Stage.Navigation.Pop(); f.RenderFrames(3);
        AssertClose(returnOffset, f.Snapshot.OffsetY);
    }

    private static void ResourceContinuousListPreservesRowsAndExplicitlyRetriesTheSamePage()
    {
        using var f = new ContinuousResourceHarness();
        f.Complete(0, ContinuousProjects("Kept", 20), hasMore: true);
        f.Pump(() => f.Rows.Length > 0);
        f.ScrollToBottom();
        f.Pump(() => f.Reads.Count == 2);
        var anchor = f.Row("Kept19");
        double offset = f.Snapshot.OffsetY;
        f.Reads[1].Reply.SetResult(XsrResult.Failure<ResourceSearchResult>(new(XsrErrorKind.Rejected,
            XsrSemanticId.Parse("fixture.resource.offline"), "Provider unavailable")));
        f.Pump(() => f.NextEnabled);
        f.RenderFrames(8);
        AssertEqual(2, f.Reads.Count);
        AssertEqual(anchor, f.Row("Kept19"));
        AssertClose(offset, f.Snapshot.OffsetY);
        f.Click("ResourceNext");
        f.Pump(() => f.Reads.Count == 3);
        AssertEqual(f.Reads[1].Query, f.Reads[2].Query);
        // A provider response for another page cannot move this cursor or replace retained rows.
        f.Reads[2].Reply.SetResult(XsrResult.Success(new ResourceSearchResult(ContinuousProjects("Wrong", 20), 100, 2) { HasMore = true }));
        f.Pump(() => f.NextEnabled);
        AssertFalse(f.Scene.Nodes.Any(node => node.Text?.StartsWith("Wrong", StringComparison.Ordinal) == true));
        AssertEqual(anchor, f.Row("Kept19"));
        f.RenderFrames(6);
        AssertEqual(3, f.Reads.Count);
        f.Click("ResourceNext");
        f.Pump(() => f.Reads.Count == 4);
        AssertEqual(1, f.Reads[3].Query.Page);
        double retainedHeight = f.Snapshot.ContentHeight;
        f.Complete(3, ContinuousProjects("Recovered", 20), hasMore: false);
        f.Pump(() => f.Snapshot.ContentHeight > retainedHeight);
        AssertFalse(f.NextEnabled);
        AssertEqual(anchor, f.Row("Kept19"));
        AssertClose(offset, f.Snapshot.OffsetY);
        f.ScrollToBottom();
        f.Pump(() => f.Scene.Nodes.Any(node => node.Text == "Recovered19"));
        f.RenderFrames(6);
        AssertEqual(4, f.Reads.Count);
    }

    private static void ResourceContinuousListRetiresSearchAndSourceGenerations()
    {
        using var f = new ContinuousResourceHarness();
        f.Complete(0, ContinuousProjects("Old", 20), hasMore: true);
        f.Pump(() => f.Rows.Length > 0);
        f.ScrollToBottom();
        f.Pump(() => f.Reads.Count == 2);
        var search = FindByKey(f.Fixture.Shell, f.Scene, "ResourceSearch").Entity;
        f.Fixture.Shell.Renderer.SetTextInputValue(search, "replacement");
        f.Click("ResourceSearchButton");
        f.Pump(() => f.Reads.Count == 3);
        AssertTrue(f.Reads[1].Token.IsCancellationRequested);
        AssertEqual(0, f.Reads[2].Query.Page);
        AssertEqual("replacement", f.Reads[2].Query.Text);
        AssertClose(0, f.Fixture.Shell.Tree.GetComponent<XsrUiScroll>(f.Page.Find("ResourceList"))!.OffsetY);
        f.Complete(1, ContinuousProjects("LateOld", 20), hasMore: false);
        f.RenderFrames(3);
        AssertFalse(f.Scene.Nodes.Any(node => node.Text?.StartsWith("LateOld", StringComparison.Ordinal) == true));
        // The replacement view is empty while loading; source policy can still supersede it.
        AssertTrue(f.Fixture.Foundation.Commands.TryResolve(SettingsPolicyContract.SetCommand, out var sourcePolicy));
        AssertTrue(f.Fixture.Foundation.Commands.Dispatch(sourcePolicy, new SettingsMutation("network.resource-source", SettingsLayer.Global,
            new(SettingsOverrideMode.Custom, "official-first"))).Completion.GetAwaiter().GetResult().IsSuccess);
        f.Pump(() => f.Reads.Count == 4);
        AssertTrue(f.Reads[2].Token.IsCancellationRequested);
        AssertEqual(0, f.Reads[3].Query.Page);
        AssertEqual("replacement", f.Reads[3].Query.Text);
        AssertTrue(f.Reads[3].Query.MirrorFirst); // Source policy changes preserve the submitted request preference.
        f.Complete(2, ContinuousProjects("LateMirror", 20), hasMore: false);
        f.Complete(3, ContinuousProjects("Official", 20), hasMore: true);
        f.Pump(() => f.Scene.Nodes.Any(node => node.Text == "Official0"));
        AssertFalse(f.Scene.Nodes.Any(node => node.Text?.StartsWith("LateMirror", StringComparison.Ordinal) == true));
        AssertFalse(f.Scene.Nodes.Any(node => node.Text?.StartsWith("Old", StringComparison.Ordinal) == true));
        AssertTrue(f.NextEnabled);
        f.RenderFrames(6);
        AssertEqual(4, f.Reads.Count);
        f.ScrollToBottom();
        f.Pump(() => f.Reads.Count == 5);
        AssertEqual(1, f.Reads[4].Query.Page);
        AssertTrue(f.Fixture.Foundation.Commands.TryResolve(SettingsPolicyContract.SetCommand, out var set));
        AssertTrue(f.Fixture.Foundation.Commands.Dispatch(set, new SettingsMutation("network.resource-source", SettingsLayer.Global,
            new(SettingsOverrideMode.Custom, "mirrors-first"))).Completion.GetAwaiter().GetResult().IsSuccess);
        f.Pump(() => f.Reads.Count == 6);
        AssertTrue(f.Reads[4].Token.IsCancellationRequested);
        AssertEqual(0, f.Reads[5].Query.Page);
        AssertEqual("replacement", f.Reads[5].Query.Text);
        AssertTrue(f.Reads[5].Query.MirrorFirst); // The new global policy supersedes the earlier override.
        AssertFalse(f.Fixture.Shell.Tree.GetComponent<XsrUiInput>(f.Page.Find("ResourceNetwork.1"))!.Enabled);
        AssertTrue(f.Fixture.Shell.Tree.GetComponent<XsrUiSelection>(f.Page.Find("ResourceNetwork.0"))!.IsSelected);
        f.Complete(4, ContinuousProjects("LatePolicy", 20), hasMore: false);
        f.Complete(5, ContinuousProjects("GlobalMirror", 20), hasMore: false);
        f.Pump(() => f.Scene.Nodes.Any(node => node.Text == "GlobalMirror0"));
        AssertFalse(f.Scene.Nodes.Any(node => node.Text?.StartsWith("LatePolicy", StringComparison.Ordinal) == true));
        AssertFalse(f.NextEnabled);
        f.RenderFrames(6);
        AssertEqual(6, f.Reads.Count);
    }

    private static void ResourceContinuousListKeepsHiddenBatchesAndSourceQualifiedIdentities()
    {
        using var f = new ContinuousResourceHarness();
        var libraries = ContinuousProjects("Library", 20).Select(project => project with { IsLibrary = true }).ToArray();
        f.Complete(0, libraries, hasMore: true);
        f.Pump(() => f.Rows.Length > 0);
        f.Click("ResourceHideLibraries");
        f.Pump(() => f.Reads.Count == 2);
        AssertEqual(1, f.Reads[1].Query.Page);
        var a = ContinuousProject("123", "Unrelated equal title", ResourceProvider.Modrinth);
        var b = ContinuousProject("123", "Unrelated equal title", ResourceProvider.CurseForge);
        var c = ContinuousProject("Different", "Unrelated equal title", ResourceProvider.Modrinth);
        f.Complete(1, [a, a, b, c], hasMore: true);
        f.Pump(() => f.Scene.Nodes.Count(node => node.Text == "Unrelated equal title") == 3);
        AssertEqual(3, f.Rows.Length);
        // A short visible projection may admit one more page automatically.
        if (f.Reads.Count == 2) f.Click("ResourceNext");
        f.Pump(() => f.Reads.Count == 3);
        AssertEqual(2, f.Reads[2].Query.Page);
        // Empty raw provider data terminates even when the provider advertises HasMore.
        f.Complete(2, [], hasMore: true);
        f.Pump(() => !f.NextEnabled);
        f.RenderFrames(6);
        AssertEqual(3, f.Reads.Count);
        AssertEqual(3, f.Rows.Length);
        var sourceCaption = f.Scene.Nodes.Single(node => node.Text?.StartsWith("CurseForge  ·", StringComparison.Ordinal) == true).Entity;
        var sourceRow = sourceCaption;
        while (!f.Rows.Contains(sourceRow)) sourceRow = f.Fixture.Shell.Tree.Parent(sourceRow);
        XsrUiEntityId details = default;
        f.Fixture.Shell.Tree.Walk(sourceRow, entity =>
        {
            if (f.Fixture.Shell.Tree.Name(entity).StartsWith("ResourceDetails.", StringComparison.Ordinal)) details = entity;
            return true;
        });
        AssertTrue(details.IsAssigned);
        Emit(f.Fixture.Intents, "ui.resources.action", details);
        f.Pump(() => f.Details.Count == 1);
        AssertEqual("123", f.Details[0].ProjectId);
        AssertEqual(new ResourceReference(ResourceProvider.CurseForge, "123"), f.Details[0].Sources.Single());
        f.Fixture.Shell.Stage.Navigation.Pop();
        f.RenderFrames(2);
        f.Click("ResourceHideLibraries");
        f.RenderFrames(2);
        AssertTrue(f.Snapshot.ContentHeight > f.Snapshot.ViewportHeight);
        AssertEqual(3, f.Reads.Count);
        AssertFalse(f.NextEnabled);

        using var fallback = new ContinuousResourceHarness();
        fallback.Reads[0].Reply.SetResult(XsrResult.Success(new ResourceSearchResult(ContinuousProjects("TotalA", 20), 41, 0)));
        fallback.Pump(() => fallback.Rows.Length > 0);
        AssertTrue(fallback.NextEnabled);
        fallback.Click("ResourceNext");
        fallback.Pump(() => fallback.Reads.Count == 2);
        double firstHeight = fallback.Snapshot.ContentHeight;
        fallback.Reads[1].Reply.SetResult(XsrResult.Success(new ResourceSearchResult(ContinuousProjects("TotalB", 20), 41, 1)));
        fallback.Pump(() => fallback.Snapshot.ContentHeight > firstHeight);
        AssertTrue(fallback.NextEnabled);
        fallback.Click("ResourceNext");
        fallback.Pump(() => fallback.Reads.Count == 3);
        fallback.ScrollToBottom();
        fallback.Pump(() => fallback.Scene.Nodes.Any(node => node.Text == "TotalB19"));
        var finalBatchAnchor = fallback.Row("TotalB19");
        double finalBatchOffset = fallback.Snapshot.OffsetY;
        int columns = fallback.Scene.Nodes.Where(node => fallback.Rows.Contains(node.Entity))
            .GroupBy(node => Math.Round(node.Rect.Y, 3)).Max(line => line.Count());
        AssertTrue(fallback.Fixture.Shell.Renderer.TryGetVirtualLayoutMetrics(fallback.Page.Find("ResourceList"), out var metrics));
        fallback.Reads[2].Reply.SetResult(XsrResult.Success(new ResourceSearchResult([ContinuousProject("TotalLast", "Total last")], 41, 2)));
        // A final item can fill an existing partial grid row without growing its extent.
        fallback.Pump(() => !fallback.NextEnabled);
        int finalRows = (41 + columns - 1) / columns;
        AssertClose(finalRows * metrics.ItemHeight + (finalRows - 1) * metrics.Spacing, fallback.Snapshot.ContentHeight);
        AssertEqual(finalBatchAnchor, fallback.Row("TotalB19"));
        AssertClose(finalBatchOffset, fallback.Snapshot.OffsetY);
        if (fallback.Snapshot.MaximumOffsetY - fallback.Snapshot.OffsetY > .01) fallback.ScrollToBottom();
        fallback.Pump(() => fallback.Scene.Nodes.Any(node => node.Text == "Total last"));
        AssertFalse(fallback.NextEnabled);
        fallback.RenderFrames(6);
        AssertEqual(3, fallback.Reads.Count);
    }

    private static void ResourceContinuousListBoundsRowsProjectsAndViewportMedia()
    {
        using var f = new ContinuousResourceHarness();
        for (int page = 0; page < 56; page++)
        {
            if (page > 0) f.Click("ResourceNext");
            int current = page;
            f.Pump(() => f.Reads.Count > current);
            f.Complete(page, ContinuousProjects("Page" + page + ".", 20), hasMore: page < 55);
            f.RenderFrames(3);
            AssertTrue(f.Rows.Length <= 48);
        }
        AssertEqual(56, f.Reads.Count);
        AssertFalse(f.NextEnabled);
        AssertFalse(HasKey(f.Fixture.Shell, f.Scene, "ResourceListBudget"));
        var viewport = FindByKey(f.Fixture.Shell, f.Scene, "ResourceList").Rect;
        f.Fixture.Shell.Renderer.PointerScroll(new(viewport.X + 20, viewport.Y + 20), 1000000);
        f.Pump(() => f.Scene.Nodes.Any(node => node.Text == "Page55.19"));
        f.Click("ResourcePrevious");
        f.Pump(() => f.Scene.Nodes.Any(node => node.Text == "Page0.0"));
    }

    private static void ResourceContinuousListRenewsEachRetainedPageWithoutReplacingOthers()
    {
        using var f = new ContinuousResourceHarness();
        f.Complete(0, ContinuousProjects("CachedA", 20), hasMore: true, stale: true);
        f.Pump(() => f.Reads.Any(read => read.Query.WaitForRefresh && read.Query.Page == 0));
        var renewalA = f.Reads.FindIndex(read => read.Query.WaitForRefresh && read.Query.Page == 0);
        var search = FindByKey(f.Fixture.Shell, f.Scene, "ResourceSearch").Entity;
        f.Fixture.Shell.Renderer.SetTextInputValue(search, "unsent");
        f.Click("ResourceNext");
        f.Pump(() => f.Reads.Any(read => !read.Query.WaitForRefresh && read.Query.Page == 1));
        var pageB = f.Reads.FindIndex(read => !read.Query.WaitForRefresh && read.Query.Page == 1);
        AssertEqual("", f.Reads[pageB].Query.Text);
        f.Complete(pageB, ContinuousProjects("CachedB", 20), hasMore: false, stale: true);
        f.Pump(() => f.Reads.Any(read => read.Query.WaitForRefresh && read.Query.Page == 1));
        var renewalB = f.Reads.FindIndex(read => read.Query.WaitForRefresh && read.Query.Page == 1);
        f.ScrollToBottom();
        f.Pump(() => f.Scene.Nodes.Any(node => node.Text == "CachedB19"));
        var anchor = f.Row("CachedB19");
        double offset = f.Snapshot.OffsetY;
        var freshA = ContinuousProjects("CachedA", 20).Select(project => project with { Title = "Fresh " + project.Title }).ToArray();
        f.Complete(renewalA, freshA, hasMore: true);
        f.RenderFrames(3);
        AssertEqual(anchor, f.Row("CachedB19"));
        AssertClose(offset, f.Snapshot.OffsetY);
        f.Reads[renewalB].Reply.SetResult(XsrResult.Failure<ResourceSearchResult>(new(XsrErrorKind.Rejected,
            XsrSemanticId.Parse("fixture.resource.renewal_failed"), "Refresh failed")));
        f.RenderFrames(8);
        AssertEqual(anchor, f.Row("CachedB19"));
        AssertTrue(f.Scene.Nodes.Any(node => node.Text == "CachedB19"));
        AssertEqual(1, f.Reads.Count(read => read.Query.WaitForRefresh && read.Query.Page == 0));
        AssertEqual(1, f.Reads.Count(read => read.Query.WaitForRefresh && read.Query.Page == 1));
        AssertEqual("", f.Reads[renewalA].Query.Text);
        AssertEqual("", f.Reads[renewalB].Query.Text);
        f.ScrollToTop();
        f.Pump(() => f.Scene.Nodes.Any(node => node.Text == "Fresh CachedA0"));
        AssertEqual("unsent", f.Fixture.Shell.Tree.GetComponent<XsrUiTextInput>(search)!.ReadDraft());
        AssertEqual(4, f.Reads.Count);
    }

    private static void ResourceContinuousListRenewsOverlappingPagesWithoutLosingSourceFacts()
    {
        var modrinth = new ResourceReference(ResourceProvider.Modrinth, "A");
        var curseforge = new ResourceReference(ResourceProvider.CurseForge, "123");
        var a = ContinuousProject("A", "Original A");
        var b = ContinuousProject("123", "Retained B", ResourceProvider.CurseForge);
        int RequestPage(ContinuousResourceHarness harness, int page)
        {
            int index = harness.Reads.FindIndex(read => !read.Query.WaitForRefresh && read.Query.Page == page);
            if (index >= 0) return index;
            harness.Click("ResourceNext");
            harness.Pump(() => harness.Reads.Any(read => !read.Query.WaitForRefresh && read.Query.Page == page));
            return harness.Reads.FindIndex(read => !read.Query.WaitForRefresh && read.Query.Page == page);
        }
        ResourceDetailQuery Open(ContinuousResourceHarness harness, string id)
        {
            int before = harness.Details.Count;
            harness.Click("ResourceDetails." + id);
            harness.Pump(() => harness.Details.Count == before + 1);
            return harness.Details[^1];
        }

        using var bridged = new ContinuousResourceHarness();
        bridged.Complete(0, [a], hasMore: true, stale: true);
        bridged.Pump(() => bridged.Reads.Any(read => read.Query.WaitForRefresh && read.Query.Page == 0));
        int renewal = bridged.Reads.FindIndex(read => read.Query.WaitForRefresh && read.Query.Page == 0);
        bridged.Complete(RequestPage(bridged, 1), [a, b], hasMore: true);
        bridged.Pump(() => bridged.Rows.Length == 2);
        var owner = bridged.Row("A");
        var bridge = ContinuousProject("Bridge", "Bridge metadata") with { Sources = [modrinth, curseforge] };
        bridged.Complete(RequestPage(bridged, 2), [bridge], hasMore: false);
        bridged.Pump(() => bridged.Rows.Length == 1);
        AssertEqual(owner, bridged.Rows.Single());
        // Removing the first owner cannot discard either later page's real membership.
        bridged.Complete(renewal, [], hasMore: true);
        bridged.RenderFrames(2);
        AssertEqual(1, bridged.Rows.Length);
        AssertEqual(owner, bridged.Rows.Single());
        var associated = Open(bridged, "A");
        AssertEqual("A", associated.ProjectId);
        AssertEqual(2, associated.Sources.Count);
        AssertTrue(associated.Sources.Contains(modrinth));
        AssertTrue(associated.Sources.Contains(curseforge));
        AssertEqual(1, bridged.Reads.Count(read => read.Query.WaitForRefresh && read.Query.Page == 0));
        AssertEqual(4, bridged.Reads.Count);

        using var restored = new ContinuousResourceHarness();
        restored.Complete(0, [a], hasMore: true, stale: true);
        restored.Pump(() => restored.Reads.Any(read => read.Query.WaitForRefresh && read.Query.Page == 0));
        int removedOwner = restored.Reads.FindIndex(read => read.Query.WaitForRefresh && read.Query.Page == 0);
        restored.Complete(RequestPage(restored, 1), [a with { Title = "Retained A" }, b], hasMore: false);
        restored.Pump(() => restored.Rows.Length == 2);
        var aRow = restored.Row("A");
        var bRow = restored.Row("123");
        restored.Complete(removedOwner, [], hasMore: true);
        restored.Pump(() => restored.Scene.Nodes.Any(node => node.Text == "Retained A")
            && restored.Scene.Nodes.Any(node => node.Text == "Retained B"));
        AssertEqual(2, restored.Rows.Length);
        AssertEqual(aRow, restored.Row("A"));
        AssertEqual(bRow, restored.Row("123"));
        AssertFalse(restored.NextEnabled);
        var restoredA = Open(restored, "A");
        AssertEqual("A", restoredA.ProjectId);
        AssertEqual(modrinth, restoredA.Sources.Single());
        restored.Fixture.Shell.Stage.Navigation.Pop();
        restored.RenderFrames(2);
        var restoredB = Open(restored, "123");
        AssertEqual("123", restoredB.ProjectId);
        AssertEqual(curseforge, restoredB.Sources.Single());
        AssertEqual(1, restored.Reads.Count(read => read.Query.WaitForRefresh && read.Query.Page == 0));
        AssertEqual(3, restored.Reads.Count);

    }

    private static ResourceProject ContinuousProject(string id, string title, ResourceProvider provider = ResourceProvider.Modrinth) =>
        new(id, title, "Description " + id, "Author", 1, (provider == ResourceProvider.CurseForge
            ? "https://www.curseforge.com/minecraft/mc-mods/" : "https://modrinth.com/project/") + id)
        { Sources = [new(provider, id)] };

    private static void ResourceCardsReflowAcrossWidthsAndKeepVisibleAnchorFocusAndAppendIdentity()
    {
        using var f = new ContinuousResourceHarness();
        f.Size = new(1400, 700);
        f.Complete(0, ContinuousProjects("Grid", 80), hasMore: true);
        f.Pump(() => f.Rows.Length > 0); f.RenderFrames(2);
        var shell = f.Fixture.Shell; var list = f.Page.Find("ResourceList");
        AssertTrue(shell.Renderer.TryGetVirtualLayoutMetrics(list, out var metrics));
        AssertEqual(XsrUiVirtualLayoutKind.Waterfall, metrics.Kind);
        XsrUiSceneNode[] Cards() => f.Scene.Nodes.Where(node => f.Rows.Contains(node.Entity)).ToArray();
        int Columns() => Cards().GroupBy(node => Math.Round(node.Rect.Y, 3)).Max(line => line.Count());
        void AssertGeometry(int columns)
        {
            var cards = Cards(); var bounds = FindByKey(shell, f.Scene, "ResourceList").Rect;
            foreach (var card in cards)
            {
                AssertTrue(card.Rect.X >= bounds.X - .01 && card.Rect.X + card.Rect.Width <= bounds.X + bounds.Width + .01);
                AssertClose(metrics.ItemHeight, card.Rect.Height);
            }
            foreach (var line in cards.GroupBy(node => Math.Round(node.Rect.Y, 3)))
            {
                var ordered = line.OrderBy(node => node.Rect.X).ToArray();
                AssertTrue(ordered.Length <= columns);
                for (int index = 1; index < ordered.Length; index++)
                    AssertClose(ordered[index - 1].Rect.X + ordered[index - 1].Rect.Width + metrics.Spacing, ordered[index].Rect.X);
            }
            int rows = (80 + columns - 1) / columns;
            AssertClose(rows * metrics.ItemHeight + (rows - 1) * metrics.Spacing, f.Snapshot.ContentHeight);
            AssertTrue(f.Rows.Length <= metrics.RealizationLimit);
        }
        int wideColumns = Columns(); AssertTrue(wideColumns >= 3); AssertGeometry(wideColumns);
        var scroll = shell.Tree.GetComponent<XsrUiScroll>(list)!;
        scroll.OffsetY = 4.25 * (metrics.ItemHeight + metrics.Spacing);
        shell.Tree.MarkDirty(list, XsrUiDirtyKinds.Layout); f.RenderFrames(2);
        int anchorIndex = 4 * wideColumns; string anchorId = "Grid" + anchorIndex;
        var anchor = f.Row(anchorId); var details = f.Page.Find("ResourceDetails." + anchorId);
        AssertTrue(shell.Renderer.Focus(details, showIndicator: false));
        var search = f.Page.Find("ResourceSearch"); shell.Renderer.SetTextInputValue(search, "unsubmitted grid draft");
        f.Size = new(690, 700); f.RenderFrames(3);
        int narrowColumns = Columns(); AssertEqual(1, narrowColumns); AssertGeometry(narrowColumns);
        AssertEqual(anchor, f.Row(anchorId)); AssertEqual(details, shell.Renderer.Focused);
        AssertClose((anchorIndex + .25) * (metrics.ItemHeight + metrics.Spacing), f.Snapshot.OffsetY);
        AssertEqual(1, f.Reads.Count);
        f.Size = new(1400, 700); f.RenderFrames(3);
        AssertEqual(wideColumns, Columns()); AssertGeometry(wideColumns);
        AssertEqual(anchor, f.Row(anchorId)); AssertEqual(details, shell.Renderer.Focused);
        AssertClose(4.25 * (metrics.ItemHeight + metrics.Spacing), f.Snapshot.OffsetY);
        AssertEqual("unsubmitted grid draft", shell.Tree.GetComponent<XsrUiTextInput>(search)!.ReadDraft());
        shell.Renderer.ApplyTheme(new VirtualMetricsTheme(71, "ResourceList", new() { VirtualItemColumns = 2 }), 71);
        f.RenderFrames(1); AssertEqual(2, Columns()); AssertGeometry(2);
        AssertEqual(anchor, f.Row(anchorId)); AssertEqual(details, shell.Renderer.Focused);
        AssertTrue(shell.Renderer.TryGetVirtualLayoutMetrics(list, out var themedMetrics));
        AssertEqual(71L, themedMetrics.ActivationGeneration); AssertTrue(themedMetrics.HasColumnsOverride);
        shell.Renderer.ClearTheme(72); f.RenderFrames(1);
        AssertEqual(wideColumns, Columns()); AssertGeometry(wideColumns);
        AssertEqual(anchor, f.Row(anchorId)); AssertEqual(details, shell.Renderer.Focused);
        f.ScrollToBottom(); f.Pump(() => f.Reads.Count == 2);
        var retained = f.Row("Grid79"); double offset = f.Snapshot.OffsetY;
        f.Complete(1, ContinuousProjects("Appended", 20), hasMore: false);
        f.Pump(() => !f.NextEnabled); f.RenderFrames(2);
        AssertEqual(retained, f.Row("Grid79")); AssertClose(offset, f.Snapshot.OffsetY);
        AssertEqual(2, f.Reads.Count); AssertTrue(f.Rows.Length <= metrics.RealizationLimit);
        f.ScrollToTop(); f.RenderFrames(2);
        var first = f.Row("Grid0"); var second = f.Row("Grid1");
        var firstRect = f.Scene.Nodes.Single(node => node.Entity == first).Rect;
        var secondRect = f.Scene.Nodes.Single(node => node.Entity == second).Rect;
        AssertClose(firstRect.Y, secondRect.Y); AssertTrue(secondRect.X > firstRect.X);
    }

    private static ResourceProject[] ContinuousProjects(string prefix, int count) =>
        Enumerable.Range(0, count).Select(index => ContinuousProject(prefix + index, prefix + index)).ToArray();

    private sealed class ContinuousResourceRead(ResourceSearchQuery query, CancellationToken token)
    {
        internal ResourceSearchQuery Query { get; } = query;
        internal CancellationToken Token { get; } = token;
        // Deterministic owner-thread completion; the controller still publishes only in a render frame.
        internal TaskCompletionSource<XsrResult<ResourceSearchResult>> Reply { get; } = new();
    }

    private sealed class ContinuousResourceHarness : IDisposable
    {
        internal LaunchPageFixture Fixture { get; } = new(new ImmediateInstanceSource([]));
        internal List<ContinuousResourceRead> Reads { get; } = [];
        internal List<ResourceDetailQuery> Details { get; } = [];
        internal ResourcesPageController Page { get; }
        internal XsrUiScene Scene { get; private set; }
        internal XsrUiEntityId[] Rows => ResourceCards(Fixture.Shell, Page.Find("ResourceList"));
        internal XsrUiSize Size { get; set; } = new(1100, 700);
        internal bool NextEnabled => Fixture.Shell.Tree.GetComponent<XsrUiInput>(Page.Find("ResourceNext"))!.Enabled;
        internal XsrUiScrollSnapshot Snapshot
        {
            get
            {
                AssertTrue(Fixture.Shell.Renderer.TryGetScrollSnapshot(Page.Find("ResourceList"), out var snapshot));
                return snapshot;
            }
        }
        internal ContinuousResourceHarness()
        {
            var queries = new XsrQueryRouterBuilder();
            queries.Register<ResourceSearchQuery, ResourceSearchResult>(ResourceCatalogContract.Search, async (query, token) =>
            {
                var read = new ContinuousResourceRead(query, token); Reads.Add(read);
                return await read.Reply.Task; // Late replies deliberately do not honor cancellation.
            });
            queries.Register<ResourceDetailQuery, ResourceDetail>(ResourceCatalogContract.Detail, (query, _) =>
            {
                Details.Add(query);
                return ValueTask.FromResult(XsrResult.Success(new ResourceDetail(ContinuousProject(query.ProjectId, "Detail " + query.ProjectId), "MIT", [])));
            });
            queries.Register<ResourceNetworkPolicyQuery, ResourceNetworkPolicySnapshot>(ResourceCatalogContract.NetworkPolicy, (_, _) =>
            {
                var values = Fixture.Foundation.Host.SettingsPolicy.Read(new()).Value!.Values;
                string priority = values.Single(value => value.Key == "network.resource-source").Value.Value!;
                return ValueTask.FromResult(XsrResult.Success(new ResourceNetworkPolicySnapshot(priority, true, null)));
            });
            Page = new(Fixture.Shell, Fixture.Intents, queries.Build(new NoopDispatchObserver()), Fixture.Store, _ => { });
            Fixture.Shell.Renderer.ReducedMotion = true; Fixture.Shell.Stage.Navigation.Replace(Page.Page);
            Scene = Fixture.Shell.Render(new(1100, 700));
            Pump(() => Reads.Count == 1);
        }
        internal void Complete(int index, IReadOnlyList<ResourceProject> projects, bool hasMore, bool stale = false) =>
            Reads[index].Reply.SetResult(XsrResult.Success(new ResourceSearchResult(projects, 10000, Reads[index].Query.Page)
            { HasMore = hasMore, IsStale = stale, Notice = stale ? "缓存资料" : null }));
        internal void Pump(Func<bool> done) => AssertTrue(SpinWait.SpinUntil(() =>
        { Scene = Fixture.Shell.Render(Size); return done(); }, TimeSpan.FromSeconds(5)));
        internal void RenderFrames(int count)
        { for (int index = 0; index < count; index++) Scene = Fixture.Shell.Render(Size); }
        internal void Click(string key) => Emit(Fixture.Intents, "ui.resources.action", Page.Find(key));
        internal XsrUiEntityId Row(string id) => Rows.Single(entity => Fixture.Shell.Tree.Name(entity) == "ResourceProject." + id);
        internal void ScrollToBottom() => Scroll(1000000);
        internal void ScrollToTop() => Scroll(-1000000);
        private void Scroll(double amount)
        {
            var viewport = FindByKey(Fixture.Shell, Scene, "ResourceList").Rect;
            AssertTrue(Fixture.Shell.Renderer.PointerScroll(new(viewport.X + 20, viewport.Y + 20), amount));
            RenderFrames(2);
        }
        public void Dispose() { Page.Dispose(); Fixture.Dispose(); }
    }
    private static XsrUiEntityId[] ResourceCards(XsrUiShell shell, XsrUiEntityId list)
    {
        List<XsrUiEntityId> cards = [];
        shell.Tree.Walk(list, entity =>
        {
            string name = shell.Tree.Name(entity);
            if (name.StartsWith("ResourceProject.", StringComparison.Ordinal)
                && shell.Tree.Name(shell.Tree.Parent(entity)).StartsWith("ResourceGridRow.", StringComparison.Ordinal))
                cards.Add(entity);
            return true;
        });
        return cards.ToArray();
    }
}
