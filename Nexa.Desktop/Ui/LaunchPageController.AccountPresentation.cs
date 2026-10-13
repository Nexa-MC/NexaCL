using Nexa.Services.Accounts;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{
    private void RefreshAccountPresentation()
    {
        XsrCollectionSnapshot<LaunchProfileView> roster = _store.ReadCollection<LaunchProfileView>(_accountProfilesId);
        int selected = SelectedAccountIndex;
        bool refreshSkins = roster.Revision != _accountRosterRevision || selected != _presentedAccountIndex;
        if (roster.Revision != _accountRosterRevision)
        {
            BuildAccountRows(roster.Items);
            _accountRosterRevision = roster.Revision;
            _presentedAccountIndex = -2;
            _skinRevision = -1;
        }
        if (selected != _presentedAccountIndex)
        {
            StyleAccountRows();
            _presentedAccountIndex = selected;
            LaunchProfileView profile = roster.Items.FirstOrDefault(item => item.Index == selected);
            _homeAvatarSource = LaunchProfilePresentation.Avatar(profile.Uuid ?? string.Empty);
            ReloadLaunchHomeWorkspace();
            _skinRevision = -1;
        }
        if (refreshSkins && _accountCommands?.TryResolve(AccountSkinContract.RefreshRoute, out XsrCommandId skinRoute) == true)
            _ = _accountCommands.Dispatch(skinRoute, new AccountRefreshSkinsCommand(), cancellationToken: _lifetimeCancellation.Token).Completion;
        RefreshSkinPresentation(roster.Items, selected);

        bool hasSelection = roster.Items.Any(profile => profile.Index == selected);
        bool picker = !hasSelection || _store.ReadAppliedValue(_store.Resolve(LaunchPageState.AccountPickerKey)) is true;
        bool onboarding = _store.ReadAppliedValue(_store.Resolve(AccountFormState.Open)) is true;
        string motionKey = onboarding
            ? "form:" + ReadCell(AccountFormState.Mode) + ":" + _store.Read<AccountLoginSnapshot>(_store.Resolve(AccountOnboardingState.Login)).Value?.Phase
            : picker ? "roster" : "selected:" + selected;
        Publish(LaunchPageState.AccountTransitionKey, motionKey);
        Publish(LaunchPageState.AccountRosterVisibleKey, picker && !onboarding);
        Publish(LaunchPageState.AccountSelectedVisibleKey, !picker && !onboarding);
        Publish(LaunchPageState.AccountCanReturnKey, hasSelection);
        Publish(LaunchPageState.AccountTitleKey, onboarding
            ? _store.ReadAppliedValue(_store.Resolve(AccountFormState.Key("title"))) as string ?? HomeCopy("account.add")
            : HomeCopy(picker && hasSelection ? "account.switch" : "account.title"));
        Publish(LaunchPageState.AccountBackVisibleKey, onboarding || (picker && hasSelection));
        Publish(LaunchPageState.AccountAddVisibleKey, !onboarding);
        Publish(LaunchPageState.AccountHintVisibleKey, roster.Count == 0 || roster.Availability == XsrStateAvailability.Unavailable);
        Publish(LaunchPageState.AccountHintKey, roster.Availability == XsrStateAvailability.Unavailable
            ? HomeCopy("account.unavailable") : roster.Count == 0 ? HomeCopy("account.empty") : string.Empty);
        if (_presentedAccountPicker is { } previous && previous != picker && _shell.Stage.Navigation.Current == _launchPage)
        {
            XsrUiEntityId focus = picker
                ? _accountRowEntities.GetValueOrDefault(selected, _accountRowEntities.Values.FirstOrDefault())
                : _pageEntities["AccountSwitch"];
            if (focus.IsAssigned) _shell.Renderer.Focus(focus, _accountKeyboardFocus);
        }
        _presentedAccountPicker = picker;
        PublishProfileFacts();
        UpdateLaunchButton();
    }

    private void RefreshSkinPresentation(IReadOnlyList<LaunchProfileView> profiles, int selected)
    {
        var skins = _store.ReadCollection<AccountSkinSnapshot>(_store.Resolve(AccountSkinContract.SkinsKey));
        if (_skinRevision == skins.Revision) return;
        _skinRevision = skins.Revision;
        Dictionary<string, AccountSkinSnapshot> images = skins.Items.ToDictionary(item => item.ProfileKey);
        foreach (LaunchProfileView profile in profiles)
        {
            XsrUiRasterImage? raster = images.TryGetValue(AccountSkinContract.ProfileKey(profile), out AccountSkinSnapshot? skin)
                && skin.Image is { } png ? LaunchProfilePresentation.Head(png) : null;
            if (_homeProfileAvatars.TryGetValue(profile.Index, out var avatar)) SetRaster(avatar, raster);
            if (profile.Index == selected) SetRaster(_pageEntities["AccountAvatar"], raster);
        }
    }

    // Raster construction is the bounded renderer media primitive; the template owns the image.
    private void SetRaster(XsrUiEntityId entity, XsrUiRasterImage? raster)
    {
        _shell.Tree.GetComponent<XsrUiImage>(entity)!.Raster = raster;
        _shell.Tree.MarkDirty(entity, XsrUiDirtyKinds.Paint);
    }

    private void BuildAccountRows(IReadOnlyList<LaunchProfileView> profiles)
    {
        if (!_pageEntities.TryGetValue("AccountRows", out XsrUiEntityId rowsHost)) return;
        // Compile every supplied fact before retiring or changing retained rows.
        var projections = profiles.Select(profile => (Profile: profile, Data: HomeProfileData(profile))).ToArray();
        var fragments = projections.Select(item => DesktopMarkup.Dynamic("LaunchInstallHomeProfile.d.nxml",
            item.Data, _shell.Renderer.LocalizeText)).ToArray();
        List<XsrUiEntityId> order = [];
        for (int index = 0; index < projections.Length; index++)
        {
            var item = projections[index];
            if (!_accountRowEntities.TryGetValue(item.Profile.Index, out var row))
            {
                row = HomeDynamic.Append(rowsHost, fragments[index]);
                _accountRowEntities[item.Profile.Index] = row;
                _accountRowIndexes[row] = item.Profile.Index;
                _homeProfileAvatars[item.Profile.Index] = HomeNodes(row)[item.Data["avatar_key"]];
            }
            else HomeDynamic.Reload(row, fragments[index]);
            _homeProfiles[item.Profile.Index] = item.Profile;
            _homeProjections[row] = item.Data;
            order.Add(row);
        }
        var wanted = profiles.Select(profile => profile.Index).ToHashSet();
        foreach (var pair in _accountRowEntities.ToArray())
        {
            if (wanted.Contains(pair.Key)) continue;
            _accountRowEntities.Remove(pair.Key);
            _accountRowIndexes.Remove(pair.Value);
            _homeProfiles.Remove(pair.Key);
            _homeProfileAvatars.Remove(pair.Key);
            _homeProjections.Remove(pair.Value);
            HomeDynamic.Remove(pair.Value);
        }
        HomeDynamic.Reorder(rowsHost, order);
    }

    private void StyleAccountRows()
    {
        foreach (var pair in _homeProfiles)
            ReloadHome(_accountRowEntities[pair.Key], "LaunchInstallHomeProfile.d.nxml", HomeProfileData(pair.Value));
    }

    private void NavigateToDownload()
    {
        _ = _shell.Select(DownloadNavigationId);
        ShowInstallRoot();
        _feedback.Info(HomeCopy("navigate.download"));
    }

    private void PublishProfileFacts()
    {
        IReadOnlyList<LaunchProfileView> profiles = ReadProfiles();
        int index = SelectedAccountIndex;
        if (profiles.Any(profile => profile.Index == index))
        {
            var profile = profiles.First(profile => profile.Index == index);
            Publish(LaunchPageState.ProfileNameKey, profile.Username);
            Publish(LaunchPageState.ProfileKindKey, HomeProfileDescription(profile));
        }
        else
        {
            Publish(LaunchPageState.ProfileNameKey, HomeCopy("account.none"));
            Publish(LaunchPageState.ProfileKindKey, string.Empty);
        }
    }
}
