
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;



namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{

    private void SelectInstallVersion(string version, string key)
    {
        if (_editingInstall) return;
        _selectedInstallVersion = version;
        ChooseInstallGame(version);
        _ = _shell.Renderer.SetTextInputValue(_javaInstallEntities["JavaInstallVersionInput"], version);

    }


    private void SelectInstallLoader(string loader, string key)
    {
        if (loader == "OptiFine" && !_region.IsMainlandChina) return;
        _selectedInstallLoader = loader;
        if (loader == LaunchInstallText.Raw("loader.vanilla")) _selectedInstallBuilds.Clear();
        _selectedInstallAddons.Clear();
        foreach (InstallLoader addon in _selectedInstallBuilds.Keys.Where(kind => QueryInstallEligibility()?.Loaders.Any(item => item.Loader == kind && item.IsAddon) == true))
            _selectedInstallAddons.Add(addon == InstallLoader.FabricApi ? "Fabric API" : addon == InstallLoader.Qsl ? "QSL" : "OptiFabric");
        foreach (string choice in JavaInstallLoaderKeys) ProjectInstallChoice(choice, choice == key);
        ProjectInstallAddon("Fabric API", "JavaFabricApiSelect", selected: false);
        ProjectInstallAddon("QSL", "JavaQslSelect", selected: false);
        JavaInstallSubpage? selectedPage = JavaInstallSubpages.FirstOrDefault(candidate => candidate.Loader == loader);
        UpdateJavaInstallSubpageVisibility(selectedPage?.PageKey ?? "JavaMinecraftPage");
    }


    private void ToggleInstallAddon(string addon, string key)
    {
        string requiredLoader = addon == "Fabric API" ? "Fabric" : "Quilt";
        if (_selectedInstallLoader != requiredLoader)
        {
            return;
        }

        bool selected = _selectedInstallAddons.Add(addon);
        if (!selected)
        {
            _selectedInstallAddons.Remove(addon);
        }

        ProjectInstallAddon(addon, key, selected);
    }


    private void NotifyInstallUnavailable()
    {
        if (!_editingInstall && !_region.AllowMinecraftDownloads) { _feedback.Warn(_shell.Renderer.LocalizeText(Nexa.Services.RegionalPolicy.MinecraftDownloadUnavailable)); return; }
        if (_editingInstall && _installEdit is null) { _feedback.Warn(InstallCopy("copy.8599bb4a4f3b")); return; }
        if (!_installGameChosen) { _feedback.Warn(InstallCopy("copy.26a1214b777a")); return; }
        if (QueryInstallEligibility()?.CommitError is { } conflict) { _feedback.Warn(conflict); return; }
        string requested = _shell.Tree.GetComponent<XsrUiTextInput>(_javaInstallEntities["JavaInstallVersionInput"])
            ?.ReadDraft().Trim() ?? string.Empty;
        if (_editingInstall && requested.Length == 0) { _feedback.Warn(InstallCopy("copy.ff4c49f3fa06")); return; }
        if (_editingInstall && _editPlan?.Kind == MinecraftInstallEditKind.Unchanged && requested == _installEdit?.InstanceId) return;
        string version = requested.Length == 0 ? _selectedInstallVersion : requested;
        string selection = _selectedInstallBuilds.Count == 0 ? _selectedInstallLoader : string.Join(LaunchInstallText.Raw("selection.separator"), _selectedInstallBuilds.Select(pair => InstallFormat("SelectionBuild", ("loader", pair.Key.ToString()), ("build", pair.Value))));
        // The selection is real: dispatch the install run and follow it in the task center.
        if (!BeginInstallRun(version)) { _feedback.Warn(InstallFormat("InstallUnavailable", ("version", version), ("selection", selection))); }
    }


    /// <summary>
    /// Dispatches the real install pipeline with the current selection. The primary loader is
    /// the first selected build the eligibility query does not classify as an addon; every
    /// other selected build rides along as an addon jar. Returns false when the install route
    /// is unavailable (tests without the runtime) or the selection is empty.
    /// </summary>
    private bool BeginInstallRun(string version)
    {
        if (_installRunCommands is null
            || !_installRunCommands.TryResolve(MinecraftInstallRoutes.Run, out XsrCommandId route))
        {
            return false;
        }

        InstallEligibilityResult? eligibility = QueryInstallEligibility();
        (InstallLoader Kind, string Build)? primary = null;
        List<MinecraftInstallAddon> addons = [];
        foreach (KeyValuePair<InstallLoader, string> build in _selectedInstallBuilds)
        {
            bool isAddon = eligibility?.Loaders.Any(
                loader => loader.Loader == build.Key && loader.IsAddon) == true;
            if (isAddon)
            {
                var catalog = _store.ReadAppliedValue(_installCatalogId) as InstallCatalogState;
                var downloads = catalog?.Catalogs.FirstOrDefault(item => item.Loader == build.Key && item.GameVersion == _selectedInstallVersion)
                    ?.Versions.FirstOrDefault(item => item.Id == build.Value)?.Downloads;
                addons.Add(new MinecraftInstallAddon(build.Key, build.Value, downloads));
            }
            else if (primary is null)
            {
                primary = (build.Key, build.Value);
            }
            else
            {
                _feedback.Warn(InstallCopy("copy.e0eac3f0bbfa"));
                return false;
            }
        }

        string root = _installEdit?.RootDirectory ?? ReadCell(LaunchPageState.InstanceDirectoryKey);
        if (string.IsNullOrWhiteSpace(root)) { _feedback.Warn(InstallCopy("copy.fe50d3e377bb")); return false; }
        _ = _installRunCommands.Dispatch(route, new MinecraftInstallCommand(
            root,
            _selectedInstallVersion,
            primary?.Kind,
            primary?.Build,
            addons, _installEdit?.InstanceId ?? (version == _selectedInstallVersion ? null : version), _installEdit?.Fingerprint)
        { NewInstanceName = _installEdit is null ? null : version });
        // Manual starts watch their task immediately (parity with the legacy task manager).
        _intents.Emit(XsrSemanticId.Parse("ui.tasks.open"), default, XsrCorrelationId.Create());
        return true;
    }


    private bool IsKeyboardIntent(XsrUiEntityId source) => source.IsAssigned
        && _shell.Tree.IsAlive(source) && _shell.Tree.GetComponent<XsrUiInput>(source)?.IsFocusVisible == true;


    private void ClearSubpageHistory()
    {
        while (_shell.Stage.Navigation.Depth > 1) _shell.Stage.Navigation.Pop();
        _returnFocus.Clear();
        UpdateTitleBar();
    }


    private void OpenSubpage(XsrUiEntityId page, XsrUiEntityId source)
    {
        if (_shell.Stage.Navigation.Current == page) return;
        bool keyboard = IsKeyboardIntent(source);
        _returnFocus.Push(source);
        _shell.Stage.Navigation.Push(page);
        UpdateTitleBar();
        _shell.Renderer.Focus(_titleEntities["TitleBack"], keyboard);
    }

    internal void OpenWardrobeLibraryPage(XsrUiEntityId page, XsrUiEntityId source)
    {
        if (_shell.Stage.Navigation.Current == _wardrobePage)
            OpenSubpage(page, source);
    }

    private void OnShellStyleChanged(object? sender, EventArgs e)
    {
        UpdateTitleBar();
        RefreshLaunchHomePresentation();
    }


    private void UpdateTitleBar()
    {
        int depth = _shell.Stage.Navigation.Depth;
        if (_titleTransitionPage != _shell.Stage.Navigation.Current)
        {
            _titleTransitionPage = _shell.Stage.Navigation.Current;
            string epoch = "title-navigation:" + ++_titleTransitionEpoch;
            foreach (string key in new[] { "TitleMain", "TitleBack", "TitleSubpage" })
            {
                var transition = _shell.Tree.GetComponent<XsrUiTransition>(_titleEntities[key])!;
                // A title can leave and return with the same caption. Give actual navigation
                // an explicit epoch instead of treating unrelated scene revisions as entry.
                transition.BoundKey = default;
                transition.Key = epoch;
                _shell.Tree.MarkDirty(_titleEntities[key], XsrUiDirtyKinds.Paint);
            }
        }
        XsrUiTransition main = _shell.Tree.GetComponent<XsrUiTransition>(_titleEntities["TitleMain"])!;
        XsrUiTransition title = _shell.Tree.GetComponent<XsrUiTransition>(_titleEntities["TitleSubpage"])!;
        main.Source = _titleEntities["TitleSubpage"];
        title.Source = _titleEntities["TitleMain"];
        if (depth != _titleNavigationDepth)
        {
            main.OffsetX = title.OffsetX = (depth > _titleNavigationDepth ? 1 : -1) * LaunchInstallText.Metric("title-motion").Width!.Value;
            _titleNavigationDepth = depth;
        }
        bool subpage = _shell.Stage.Navigation.Depth > 1;
        string caption = subpage ? _shell.Stage.Navigation.Current == _javaInstallPage
            ? InstallCopy(_editingInstall ? "edit.title" : "install.title")
            : _shell.Stage.Navigation.Current == _javaLauncherGuidePage
                ? InstallCopy("java-guide.title")
            : _shell.Tree.GetComponent<XsrUiSemantic>(_shell.Stage.Navigation.Current)?.Label ?? string.Empty : string.Empty;
        Publish(LaunchPageState.TitleRootKey, !subpage);
        Publish(LaunchPageState.TitleSubpageKey, subpage);
        Publish(LaunchPageState.TitleCaptionKey, caption);
        Publish(LaunchPageState.TitleTransitionKey, subpage ? caption : "NexaCL");
    }


    private (XsrUiEntityId Page, Dictionary<string, XsrUiEntityId> Entities) LoadInstallPage() => LoadStandalonePage("InstallPage.nxml");

    private (XsrUiEntityId Page, Dictionary<string, XsrUiEntityId> Entities) LoadJavaInstallPage()
    {
        var page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("JavaInstallPage.nxml"), _shell.Tree, _store);
        var slot = _shell.Tree.Children(page).Single();
        _installWorkspace = AppendInstall(slot, "LaunchInstallJavaWorkspace.d.nxml", JavaInstallWorkspaceData());
        var entities = InstallNodes(page);
        Publish(LaunchPageState.JavaInstallTitleKey, InstallCopy("install.title"));
        _ = _shell.Renderer.SetTextInputValue(entities["JavaInstallVersionInput"], _selectedInstallVersion);
        return (page, entities);
    }

    private XsrUiEntityId LoadBedrockInstallPage() => BedrockInstallPageController.LoadPlaceholder(_shell, _store);

    private Dictionary<string, XsrUiEntityId> InstallNodes(XsrUiEntityId page)
    {
        Dictionary<string, XsrUiEntityId> entities = [];
        _shell.Tree.Walk(page, entity => { entities[_shell.Tree.Name(entity)] = entity; return true; });
        return entities;
    }

    private (XsrUiEntityId Page, Dictionary<string, XsrUiEntityId> Entities) LoadStandalonePage(string resource)
    {
        var page = NxmlUiLoader.LoadDetached(DesktopMarkup.Load(resource), _shell.Tree, _store);
        return (page, InstallNodes(page));
    }

    private void UpdateJavaInstallSubpageVisibility(string? preferredPage = null)
    {
        XsrUiEntityId pagerEntity = _javaInstallEntities["JavaInstallPager"];
        XsrUiEntityId[] previousPages = VisibleJavaInstallPages(pagerEntity);
        var visibility = JavaInstallSubpages.Select(subpage => (Subpage: subpage, Visible: ShouldShowJavaInstallSubpage(subpage))).ToArray();
        ReloadInstall(_installWorkspace, visibility.Select(item => (item.Subpage.PageKey + "_visible", InstallFlag(item.Visible))).ToArray());
        foreach (var item in visibility)
            _shell.Renderer.SetSegmentExpanded(_javaInstallEntities[item.Subpage.TabKey], item.Visible, immediate: !_attached);

        if (_selectedInstallLoader != "Fabric")
        {
            _selectedInstallAddons.Remove("Fabric API");
            ProjectInstallAddon("Fabric API", "JavaFabricApiSelect", selected: false);
        }
        if (_selectedInstallLoader != "Quilt")
        {
            _selectedInstallAddons.Remove("QSL");
            ProjectInstallAddon("QSL", "JavaQslSelect", selected: false);
        }

        string target = preferredPage ?? _activeJavaInstallPage;
        if (!_javaInstallEntities.TryGetValue(target, out XsrUiEntityId targetEntity)
            || !IsInstallEntityVisible(targetEntity))
        {
            target = "JavaMinecraftPage";
        }

        var nextPages = VisibleJavaInstallPages(pagerEntity);
        if (!previousPages.SequenceEqual(nextPages))
            _shell.Renderer.RebasePagerPage(pagerEntity, Array.IndexOf(nextPages, _javaInstallEntities[target]));
        ShowJavaInstallSubpage(target);
    }


    private bool ShouldShowJavaInstallSubpage(JavaInstallSubpage subpage)
    {
        if (subpage.PageKey == "JavaOptiFinePage" && !_region.IsMainlandChina) return false;
        if (subpage.PageKey == "JavaMinecraftPage") return true;
        if (!_installGameChosen) return false;
        InstallLoader? loader = subpage.PageKey switch
        {
            "JavaFabricApiPage" => InstallLoader.FabricApi,
            "JavaOptiFabricPage" => InstallLoader.OptiFabric,
            "JavaQslPage" => InstallLoader.Qsl,
            _ => ParseInstallLoader(subpage.Loader)
        };
        return QueryInstallEligibility()?.Loaders.Any(item => item.Loader == loader && item.Visible) == true;
    }


    private bool IsInstallEntityVisible(XsrUiEntityId entity) =>
        _shell.Tree.GetComponent<XsrUiElement>(entity)?.IsVisible ?? true;


    private XsrUiEntityId[] VisibleJavaInstallPages(XsrUiEntityId pager) =>
        [.. _shell.Tree.Children(pager).Where(IsInstallEntityVisible)];


    private void RefreshJavaInstallPresentation()
    {
        if (!_javaInstallEntities.TryGetValue("JavaInstallPager", out XsrUiEntityId pagerEntity)
            || _shell.Tree.GetComponent<XsrUiPager>(pagerEntity) is not { } pager)
        {
            return;
        }

        XsrUiEntityId[] visiblePages = VisibleJavaInstallPages(pagerEntity);
        if (visiblePages.Length == 0)
        {
            return;
        }

        int current = Math.Clamp(pager.PageIndex, 0, visiblePages.Length - 1);
        JavaInstallSubpage? active = JavaInstallSubpages.FirstOrDefault(subpage =>
            _javaInstallEntities.TryGetValue(subpage.PageKey, out XsrUiEntityId page)
            && page == visiblePages[current]);
        if (active is null)
        {
            return;
        }

        if (current == _presentedJavaInstallPage && _activeJavaInstallPage == active.PageKey)
        {
            return;
        }

        _presentedJavaInstallPage = current;
        _activeJavaInstallPage = active.PageKey;
        ProjectJavaInstallTabs(active.PageKey);
    }

    private void ProjectJavaInstallTabs(string activePage)
    {
        List<(string Key, string Value)> values = [];
        foreach (var subpage in JavaInstallSubpages)
        {
            bool active = subpage.PageKey == activePage;
            values.Add((subpage.TabKey + "_selected", InstallFlag(active)));
            values.Add((subpage.TabKey + "_style", LaunchInstallText.Raw(active ? "style.tab-active" : "style.tab")));
            values.Add((subpage.TabKey + "_label", InstallFormat(active ? "TabCurrent" : "TabView", ("name", InstallCopy("tab." + subpage.TabKey)))));
        }
        ReloadInstall(_installWorkspace, values.ToArray());
        var selected = JavaInstallSubpages.First(subpage => subpage.PageKey == activePage);
        _shell.Tree.GetComponent<XsrUiSegmentedTrack>(_javaInstallEntities["JavaInstallPagerTabs"])!.Selected = _javaInstallEntities[selected.TabKey];
    }

}
