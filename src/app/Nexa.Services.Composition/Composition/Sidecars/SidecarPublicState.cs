using Nexa.Services.Accounts;
using Nexa.Services.Foundation;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Composition.Sidecars;

/// <summary>Public domain projections only. Credentials and private account records are never included.</summary>
public sealed record SidecarPublicState(
    long LibraryRevision, MinecraftLibrarySnapshot? Library,
    long AccountsRevision, IReadOnlyList<LaunchProfileView>? Accounts,
    long ProcessesRevision, IReadOnlyList<MinecraftProcessSnapshot>? Processes,
    long InstallRevision, InstallCatalogState? Install,
    long LoginRevision, AccountLoginSnapshot? Login,
    long CharactersRevision, IReadOnlyList<AccountCharacterChoice>? Characters,
    long SettingsRevision);

public sealed partial class SidecarBusinessApi
{
    private XsrStateStore? _state;
    public void BindState(XsrStateStore state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            if (_ready || _state is not null) throw new InvalidOperationException("Host state publication already bound or sealed.");
            _state = state;
        }
    }

    private SidecarPublicState ReadPublicState()
    {
        var store = _state ?? throw new InvalidOperationException("Host state is unavailable.");
        (long Revision, T? Value) Read<T>(XsrSemanticId key) where T : class
        {
            if (!store.TryResolve(key, out var id)) return (-1, null);
            var value = store.Read<T>(id);
            return (value.Revision, value.IsAvailable ? value.Value : null);
        }
        (long Revision, IReadOnlyList<T>? Items) Collection<T>(XsrSemanticId key)
        {
            if (!store.TryResolve(key, out var id)) return (-1, null);
            var value = store.ReadCollection<T>(id);
            return (value.Revision, value.Availability == XsrStateAvailability.Available ? value.Items : null);
        }
        var library = Read<MinecraftLibrarySnapshot>(MinecraftLibraryContract.StateKey);
        var accounts = Collection<LaunchProfileView>(AccountStateContract.ProfilesKey);
        var processes = Collection<MinecraftProcessSnapshot>(MinecraftProcessStateComposition.SessionsKey);
        var install = Read<InstallCatalogState>(InstallCatalogStateContract.StateKey);
        var login = Read<AccountLoginSnapshot>(AccountOnboardingState.Login);
        var characters = Collection<AccountCharacterChoice>(AccountOnboardingState.Characters);
        long settingsRevision = -1;
        if (store.TryResolve(SettingsPolicyContract.RevisionKey, out var settings))
        {
            var value = store.Read<long>(settings);
            if (value.IsAvailable) settingsRevision = value.Value;
        }
        return new(library.Revision, library.Value, accounts.Revision, accounts.Items, processes.Revision, processes.Items,
            install.Revision, install.Value, login.Revision, login.Value, characters.Revision, characters.Items, settingsRevision);
    }
}
