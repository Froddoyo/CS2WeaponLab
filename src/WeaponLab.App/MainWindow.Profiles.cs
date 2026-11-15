using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Game;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.VData;

namespace WeaponLab.App;

/// <summary>The profile bar, the target addon, and the selected weapon's settings in the profile.</summary>
public sealed partial class MainWindow
{
    private void SaveProfile()
    {
        try
        {
            _profiles.Save(_profile);
            NoteProfileWritten();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log($"Could not save profile '{_profile.Name}': {error.Message}");
        }
    }

    /// <summary>When the profile file was last written by this window (see <see cref="ReloadProfileIfChangedAsync"/>).</summary>
    private DateTime _profileWritten;

    private void NoteProfileWritten()
    {
        try
        {
            _profileWritten = File.GetLastWriteTimeUtc(_profiles.PathFor(_profile.Name));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _profileWritten = default;
        }
    }

    /// <summary>
    /// The profile was changed outside this window (0.24: Blender's Change gun model runs the command line, which
    /// sets the weapon's model and skeleton): read it again when the window comes back to the front, so the next
    /// save here does not undo that change. The app saves every change at once, so nothing is lost by reloading.
    /// </summary>
    private async Task ReloadProfileIfChangedAsync()
    {
        var path = _profiles.PathFor(_profile.Name);
        if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) == _profileWritten)
        {
            return;
        }

        try
        {
            _profile = _profiles.Load(_profile.Name);
            _vdataOverrides = VDataOverrides.For(_profile, SaveProfile);
            NoteProfileWritten();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Log($"Profile '{_profile.Name}' changed on disk but could not be read again: {error.Message}");
            return;
        }

        Log($"Profile '{_profile.Name}' was changed outside the app (e.g. Change gun model in Blender); read it again.");
        _events = null;
        InvalidateViewport();
        RefreshSkeletonPanel();
        RebuildTree();
        await RefreshStatusAsync();
        RefreshCurrentTool();
    }

    /// <summary>Once per profile (0.21): weapons that showed their custom skeleton's model by default keep it as their pick.</summary>
    private void KeepOldViewportDefaults()
    {
        try
        {
            if (_profile.ViewportDefaultIsCs2)
            {
                return;
            }

            var kept = ViewportModels.KeepOldDefaults(_profile, _sandboxes);
            SaveProfile();
            if (kept.Count > 0)
            {
                Log($"The viewport's Default model is CS2's now. {string.Join(", ", kept)} keep their skeleton's model as their pick (Animations tab, Viewport model).");
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Log($"Could not check the viewport models: {error.Message}");
        }
    }

    /// <summary>
    /// Makes a profile current: a local workspace with its own sandboxes
    /// (Blender files, exports) and changes not pushed yet. Its last addon is
    /// picked again. The addon stays the live state either way.
    /// </summary>
    private async Task SwitchProfileAsync(Profile profile)
    {
        _profile = profile;
        _profiles.SetActive(profile.Name);
        NoteProfileWritten();
        _sandboxes = _profiles.SandboxesFor(profile.Name);
        KeepOldViewportDefaults();
        _vdataOverrides = VDataOverrides.For(_profile, SaveProfile);
        _reviewedPlan = null;
        PlanList.ItemsSource = null;
        _exportWatcher?.Dispose();
        WatchExports();
        FillProfileCombo();
        if (profile.TargetAddon is null && Target() is { } current)
        {
            // A profile with no addon yet keeps working on the one that is picked.
            profile.TargetAddon = current.Name;
            SaveProfile();
        }
        else if (_connection is not null && _connection.Addons.FirstOrDefault(addon => addon.Name == profile.TargetAddon) is { } last)
        {
            _updatingControls = true;
            AddonCombo.SelectedItem = last;
            _updatingControls = false;
            ForgetAddonCaches();
        }

        RebuildTree();
        await RefreshStatusAsync();
        RefreshWeaponPanel();
        RefreshWeaponActions();
        RefreshCurrentTool();
    }

    private void FillProfileCombo()
    {
        _updatingControls = true;
        ProfileCombo.ItemsSource = _profiles.Names();
        ProfileCombo.SelectedItem = _profile.Name;
        _updatingControls = false;
    }

    private async void OnProfileChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingControls || ProfileCombo.SelectedItem is not string name || name == _profile.Name)
        {
            return;
        }

        SaveProfile();
        await SwitchProfileAsync(_profiles.Load(name));
        Log($"Profile '{name}': {_sandboxes.List().Count} sandbox(es) in {_profiles.FolderFor(name)}.");
    }

    private async void OnNewProfileClicked(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.AskAsync(this, "New profile", "Name of the new profile (an empty workspace: no Blender files, no pending changes):", "") is not { Length: > 0 } name)
        {
            return;
        }

        try
        {
            SaveProfile();
            var profile = _profiles.Create(name, Target()?.Name ?? _profile.TargetAddon);
            await SwitchProfileAsync(profile);
            Log($"Created profile '{profile.Name}'.");
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            await Dialogs.ShowAsync(this, "New profile", error.Message);
        }
    }

    private async void OnRenameProfileClicked(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.AskAsync(this, "Rename profile", $"New name for '{_profile.Name}':", _profile.Name) is not { Length: > 0 } name || name == _profile.Name)
        {
            return;
        }

        try
        {
            SaveProfile();
            _exportWatcher?.Dispose();
            _exportWatcher = null;
            var old = _profile.Name;
            await SwitchProfileAsync(_profiles.Rename(old, name));
            Log($"Renamed profile '{old}' to '{name}'. A .blend that is open in Blender keeps working only after reopening it from the new folder.");
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            WatchExports();
            await Dialogs.ShowAsync(this, "Rename profile", error.Message + "\n\nClose any .blend of this profile in Blender and try again.");
        }
    }

    private async void OnDeleteProfileClicked(object? sender, RoutedEventArgs e)
    {
        var others = _profiles.Names().Where(name => name != _profile.Name).ToList();
        if (others.Count == 0)
        {
            await Dialogs.ShowAsync(this, "Delete profile", "This is the only profile. Make another one first.");
            return;
        }

        var pending = _connection?.Weapons.Count(weapon => StatusFor(weapon).State == WeaponState.NeedsApply) ?? 0;
        var sandboxes = _sandboxes.List().Count;
        if (!await Dialogs.ConfirmAsync(this, "Delete profile",
                $"Are you sure you want to delete profile '{_profile.Name}'?\n\nIts {sandboxes} sandbox(es) (Blender files and exports) and {(pending == 0 ? "settings" : $"{pending} weapon(s) of work not pushed yet")} go with it. " +
                "The folder is moved to workspace\\backups\\profiles, not erased. Addons keep everything already pushed to them.", "Delete", destructive: true))
        {
            return;
        }

        try
        {
            _exportWatcher?.Dispose();
            _exportWatcher = null;
            var name = _profile.Name;
            var backup = _profiles.Delete(name);
            await SwitchProfileAsync(_profiles.Load(others[0]));
            Log($"Deleted profile '{name}' (moved to {backup}).");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WatchExports();
            await Dialogs.ShowAsync(this, "Delete profile", error.Message + "\n\nClose any .blend of this profile in Blender and try again.");
        }
    }

    /// <summary>"N weapons with work not pushed yet" beside the pickers; that work stays in the profile until applied.</summary>
    private void RefreshPendingText()
    {
        if (_connection is not { } connection || _status is null)
        {
            PendingText.Text = "";
            return;
        }

        var pending = connection.Weapons.Count(weapon => StatusFor(weapon).State == WeaponState.NeedsApply);
        PendingText.Text = pending == 0 ? "Nothing pending" : $"{pending} weapon(s) with work not pushed yet";
    }

    // ---- Target addon ----

    private async void OnAddonChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingControls || AddonCombo.SelectedItem is not AddonInfo addon)
        {
            return;
        }

        // The addon is the live state; the profile stays and remembers it as its addon.
        _profile.TargetAddon = addon.Name;
        SaveProfile();
        PlanList.ItemsSource = null;
        _reviewedPlan = null;
        ForgetAddonCaches();
        await RefreshStatusAsync();
        RefreshWeaponPanel();
        RefreshCurrentTool();
        Log($"Addon: {addon.Name}. Profile '{_profile.Name}' pushes its pending work there when you apply.");
    }

    /// <summary>Re-reads what the picked addon has (its inventory and vdata) after the addon or profile changed.</summary>
    private void ForgetAddonCaches()
    {
        _addonVData = null;
        _addonVDataFor = null;
    }

    // ---- The selected weapon in the profile ----

    /// <summary>
    /// The weapon panel is gone (0.13): a weapon's status, slot and Blender
    /// build show in the Property Browser and commit is ticked in the Deliver
    /// tree. This refreshes the Property Browser when it shows the weapon.
    /// </summary>
    private void RefreshWeaponPanel()
    {
        if (_weapon is { } weapon && _clipPath is null && !_showingBone)
        {
            ShowWeaponProperties(weapon);
        }
    }

    /// <summary>The weapon's state in the addon, slot and Blender build, for the Property Browser.</summary>
    private PropertyGroup WeaponAddonProperties(WeaponLab.Core.Weapons.WeaponEntry weapon)
    {
        var status = StatusFor(weapon);
        var state = status.State switch
        {
            WeaponState.NeedsApply => "Not in the addon yet: " + string.Join(" ", status.Reasons.Take(4)),
            WeaponState.Applied => "In the addon; nothing pending",
            _ => Target() is null ? "No target addon" : "No edits",
        };
        var rows = new List<PropertyRow>
        {
            new("Status", state),
            new("Commit", _profile.Weapon(weapon.Id).Animations == AnimationMode.Custom ? "yes (untick in the Deliver tree)" : "no: left out of pushes; the addon keeps what it has"),
            new("Slot", SlotSummary(weapon).Replace("Slot: ", "", StringComparison.Ordinal)),
            new("Game timing", TimingSummary(weapon)),
            new("Blender build", BuildSummary(weapon.Id)),
        };
        if (AddonOwnSummary(weapon.Id) is { Length: > 0 } own)
        {
            rows.Add(new PropertyRow("Addon's own work", own));
        }

        return new PropertyGroup("In the addon", rows);
    }
}
