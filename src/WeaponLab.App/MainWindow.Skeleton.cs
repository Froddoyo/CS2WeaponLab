using Avalonia.Interactivity;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.Skeletons;

namespace WeaponLab.App;

/// <summary>The weapon panel's custom skeleton: made from a model's bones, kept in the sandbox and the profile.</summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Opens the Build .blend window for a slot, filled with the profile's
    /// choices (or the best guesses: the addon's clips and skeleton when it has
    /// them). Null when cancelled.
    /// </summary>
    private async Task<BuildBlendChoices?> ShowBuildBlendDialogAsync(WeaponLab.Core.Game.GameConnection connection, WeaponLab.Core.Weapons.WeaponEntry weapon)
    {
        var target = Target();
        var settings = _profile.Weapon(weapon.Id);
        var addonClips = target is null ? new Dictionary<string, string>() : await Task.Run(() => WeaponLab.Core.Sandbox.SandboxStore.AddonClips(connection, weapon, target));
        var addonSkeleton = target is null ? null : CustomSkeletons.FindInAddon(connection, target.Name, weapon.Id);
        var addonVData = AddonCombo.SelectedItem is WeaponLab.Core.Game.AddonInfo addon ? TryReadAddonVData(addon) : null;
        var slot = WeaponLab.Core.Weapons.WeaponSlots.For(connection, weapon, addonVData, target?.Name);
        var ownSkeleton = weapon.ClipPaths.Count == 0 ? null : connection.ReadClip(weapon.ClipPaths[0]).SecondarySkeletons.FirstOrDefault();
        var hasAddonWork = addonClips.Count > 0 || addonSkeleton is not null;
        var fromAddon = settings.BuildFromAddon ?? hasAddonWork;
        var skeleton = settings.CustomSkeleton is { } custom
            ? addonSkeleton is { } found && custom.Path == found.Skeleton ? SkeletonSource.Addon : SkeletonSource.Custom
            : fromAddon && addonSkeleton is not null ? SkeletonSource.Addon : SkeletonSource.Own;
        var (sleeves, gloves) = await Task.Run(() => ArmsPreview.Choices(connection));
        var stockModel = ownSkeleton is null ? null : await Task.Run(() => PreviewModels.StockModelFor(connection, ownSkeleton));

        string Describe(string key)
        {
            if (!_sandboxes.Exists(weapon.Id))
            {
                return "The bones are compared once the sandbox exists.";
            }

            try
            {
                var preview = CustomSkeletons.Preview(_sandboxes.Open(weapon.Id), connection, key, target?.Name);
                var extra = preview.ExtraBones.Count == 0 ? "none" : string.Join(", ", preview.ExtraBones);
                return $"{preview.SharedBones.Count} bone(s) shared with {Path.GetFileName(preview.FromSkeleton)}. New bones: {extra}" + SkeletonNotes(preview);
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                return "Cannot use this model: " + error.Message;
            }
        }

        string DescribeFile(string file)
        {
            if (!_sandboxes.Exists(weapon.Id))
            {
                return "The bones are compared once the sandbox exists.";
            }

            try
            {
                var preview = CustomSkeletons.PreviewFile(_sandboxes.Open(weapon.Id), connection, file);
                return $"{preview.SharedBones.Count} bone(s) shared with {Path.GetFileName(preview.FromSkeleton)}. New bones: {(preview.ExtraBones.Count == 0 ? "none" : string.Join(", ", preview.ExtraBones))}";
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                return "Cannot use this skeleton: " + error.Message;
            }
        }

        return await BuildBlendDialog.ShowAsync(this, new BuildBlendSetup(
            weapon.Id,
            WeaponLab.Core.Weapons.WeaponSlots.Describe(slot),
            target?.Name,
            addonClips.Count,
            weapon.ClipPaths.Count,
            addonSkeleton,
            ownSkeleton,
            settings.CustomSkeleton is { } current && skeleton == SkeletonSource.Custom ? current : null,
            CustomSkeletons.DefaultPath(weapon.Id),
            settings.PreviewModel,
            sleeves,
            gloves,
            settings.ArmsModel,
            settings.GlovesModel,
            fromAddon,
            skeleton,
            text => PreviewModels.FromFile(connection, text),
            Describe,
            DescribeFile,
            target?.GamePath,
            key => PreviewModels.Freshness(connection, key)?.Describe(Path.GetFileNameWithoutExtension(key)),
            stockModel,
            WeaponBones(connection, weapon),
            settings.PropBones,
            ModelProblems(weapon)));
    }

    /// <summary>The details a skeleton preview adds below the bone counts: corrected, re-parented and skipped bones.</summary>
    private static string SkeletonNotes(CustomSkeletonPreview preview)
    {
        var notes = new List<string>();
        if (preview.Corrected.Count > 0)
        {
            notes.Add($"Placed relative to the weapon body (parent rests differently in this model): {string.Join(", ", preview.Corrected)}");
        }

        if (preview.Reparented.Count > 0)
        {
            notes.Add($"The model hangs these under another parent than CS2 (kept as CS2 unless you use the model's hierarchy): {string.Join("; ", preview.Reparented)}");
        }

        if (preview.SkippedBones.Count > 0)
        {
            notes.Add($"Left out (Blender FBX leaf bones; untick Add Leaf Bones when exporting): {string.Join(", ", preview.SkippedBones)}");
        }

        return notes.Count == 0 ? "" : "\n" + string.Join("\n", notes);
    }

    /// <summary>
    /// Puts the sandbox on a skeleton and records it in the profile: CS2's default
    /// for the slot, the addon's own (from its vdata), one built from a model, or a
    /// skeleton file (a choice whose model starts with <see cref="CustomSkeletons.FilePrefix"/>).
    /// Does nothing when the sandbox already has it.
    /// </summary>
    private async Task ApplySkeletonAsync(WeaponLab.Core.Game.GameConnection connection, WeaponLab.Core.Weapons.WeaponEntry weapon, WeaponLab.Core.Sandbox.SandboxInfo sandbox,
        SkeletonSource source, CustomSkeletonChoice? custom, IProgress<string> progress)
    {
        var target = Target();
        var addonName = _profile.TargetAddon;
        var settings = _profile.EnsureWeapon(weapon.Id);
        var setup = CustomSkeletons.Load(sandbox);
        var made = false; // A skeleton made or adopted now, not one kept from before.
        switch (source)
        {
            case SkeletonSource.Own:
                if (setup is not null)
                {
                    await Task.Run(() => CustomSkeletons.Remove(sandbox, connection, progress));
                    Log($"{weapon.Id} is back on CS2's default skeleton for this slot.");
                }

                settings.CustomSkeleton = null;
                if (settings.PreviewModel is not null && settings.PreviewModel == ViewportModels.ModelOf(setup))
                {
                    settings.PreviewModel = null; // The skeleton's model goes with it; the viewport shows CS2's again.
                }

                break;
            case SkeletonSource.Addon when CustomSkeletons.FindInAddon(connection, target?.Name, weapon.Id) is { } found:
                if (setup is null || setup.Path != found.Skeleton || !setup.FromAddon)
                {
                    made = true;
                    setup = await Task.Run(() => CustomSkeletons.Adopt(sandbox, connection, target!.Name, found.Skeleton, found.Model, progress));
                    Log($"{weapon.Id}: using {target!.Name}'s skeleton {setup.Path} ({setup.Bones.Count} bones, {setup.ExtraBones.Count} beyond CS2's).");
                }

                settings.CustomSkeleton = new CustomSkeletonChoice(setup.SourceModel, setup.Path);
                break;
            case SkeletonSource.Custom when custom is not null && custom.Model.StartsWith(CustomSkeletons.FilePrefix, StringComparison.Ordinal):
                if (setup is null || setup.SourceModel != custom.Model)
                {
                    var file = custom.Model[CustomSkeletons.FilePrefix.Length..];
                    setup = await Task.Run(() => CustomSkeletons.UseFile(sandbox, connection, addonName, file, progress));
                    Log($"{weapon.Id}: skeleton file {setup.Path} ({setup.Bones.Count} bones, {setup.ExtraBones.Count} beyond CS2's){(setup.FromAddon ? ", the addon's own" : "; delivered into the addon")}.");
                }

                settings.CustomSkeleton = new CustomSkeletonChoice(setup.SourceModel, setup.Path);
                break;
            case SkeletonSource.Custom when custom is not null:
                var recompiled = setup is not null && setup.SourceModel == custom.Model && CustomSkeletons.ModelChangedSince(connection, setup);
                if (setup is null || setup.FromAddon || setup.Path != custom.Path || setup.SourceModel != custom.Model || setup.ModelHierarchy != custom.ModelHierarchy || recompiled)
                {
                    made = true;
                    setup = await Task.Run(() => CustomSkeletons.Create(sandbox, connection, custom.Model, addonName, custom.Path, progress, custom.ModelHierarchy));
                    if (recompiled)
                    {
                        Log($"{weapon.Id}: {Path.GetFileName(custom.Model)} was compiled again since its skeleton was made, so the skeleton is made again from it (bones added in ModelDoc come in).");
                    }

                    Log($"{weapon.Id}: custom skeleton {setup.Path} built from {custom.Model} ({setup.Bones.Count} bones, {setup.ExtraBones.Count} new{(setup.ModelHierarchy ? ", the model's hierarchy" : "")})."
                        + (setup.UnweightedBones is { Count: > 0 } empty ? $" No mesh is weighted to {string.Join(", ", empty)}: often bones from animation files the .vmdl lists; they are hidden in Blender." : "")
                        + (setup.SkippedBones is { Count: > 0 } skipped ? $" Left out FBX leaf bones: {string.Join(", ", skipped)}." : ""));
                }

                settings.CustomSkeleton = custom;
                break;
        }

        // A skeleton just built from a model shows that model unless another was picked (default is CS2's model since 0.21).
        if (made)
        {
            settings.PreviewModel ??= ViewportModels.ModelOf(setup);
        }

        SaveProfile();
    }

    private static WeaponLab.Core.VData.VDataFile? TryReadAddonVData(WeaponLab.Core.Game.AddonInfo addon)
    {
        try
        {
            return WeaponLab.Core.Game.GameConnection.ReadAddonVData(addon);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes the sandbox match the Build .blend choices: templates from the game
    /// or the addon (re-taken when that changed), then the chosen skeleton.
    /// Saves the choices in the addon's profile. Null when it failed.
    /// </summary>
    private async Task<WeaponLab.Core.Sandbox.SandboxInfo?> ApplyBuildChoicesAsync(WeaponLab.Core.Game.GameConnection connection, WeaponLab.Core.Weapons.WeaponEntry weapon, BuildBlendChoices choices)
    {
        var target = Target();
        var source = choices.FromAddon ? target : null;
        var settings = _profile.EnsureWeapon(weapon.Id);
        settings.ResetToStock = false; // New work on the weapon replaces a pending Set to CS2 default.
        settings.BuildFromAddon = choices.FromAddon;
        settings.PreviewModel = choices.PreviewModel;
        settings.ArmsModel = choices.SleevesModel;
        settings.GlovesModel = choices.GlovesModel;
        settings.PropBones = new SortedSet<string>(choices.PropBones, StringComparer.Ordinal);
        SaveProfile();

        var progress = new Progress<string>(SetStatus);
        var addonName = _profile.TargetAddon;
        try
        {
            WeaponLab.Core.Sandbox.SandboxInfo sandbox;
            if (!_sandboxes.Exists(weapon.Id))
            {
                sandbox = await Task.Run(() => _sandboxes.Create(connection, weapon, addonName, progress, source));
                Log($"Created the sandbox for {weapon.Id} from {(source is null ? "the game files" : source.Name)} ({sandbox.Manifest.Clips.Count} clips).");
            }
            else
            {
                sandbox = _sandboxes.Open(weapon.Id);
                var wanted = source is null ? null : WeaponLab.Core.Sandbox.SandboxStore.AddonClips(connection, weapon, source).Count > 0 ? source.Name : null;
                if (sandbox.Manifest.SourceAddon != wanted)
                {
                    var existing = sandbox;
                    sandbox = await Task.Run(() => _sandboxes.Rebuild(existing, connection, weapon, addonName, source, progress));
                    Log($"{weapon.Id}'s sandbox now starts from {wanted ?? "the game files"}.");
                }
            }

            await ApplySkeletonAsync(connection, weapon, sandbox, choices.Skeleton, choices.Custom, progress);

            SaveProfile();
            return sandbox;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            await Dialogs.ShowAsync(this, "Build .blend", $"The sandbox for {weapon.Id} could not be prepared: {error.Message}");
            return null;
        }
    }

    private (string? Addon, DateTime Written, WeaponLab.Core.VData.VDataFile? VData) _slotVData;

    /// <summary>The weapon's animation slot and every weapon that plays it, for the weapon panel.</summary>
    private string SlotSummary(WeaponLab.Core.Weapons.WeaponEntry weapon)
    {
        if (_connection is not { } connection)
        {
            return "";
        }

        var (addon, vdata) = SelectedAddonVData();
        return "Slot: " + WeaponLab.Core.Weapons.WeaponSlots.Describe(WeaponLab.Core.Weapons.WeaponSlots.For(connection, weapon, vdata, addon));
    }

    /// <summary>The selected addon and its compiled vdata, read again only when the file changed.</summary>
    private (string? Addon, WeaponLab.Core.VData.VDataFile? VData) SelectedAddonVData()
    {
        var addon = AddonCombo.SelectedItem as WeaponLab.Core.Game.AddonInfo;
        var file = addon is null ? null : Path.Combine(addon.GamePath, "scripts", "weapons.vdata_c");
        var written = file is not null && File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
        if (_slotVData.Addon != addon?.Name || _slotVData.Written != written)
        {
            _slotVData = (addon?.Name, written, addon is null ? null : TryReadAddonVData(addon));
        }

        return (addon?.Name, _slotVData.VData);
    }

    /// <summary>The gun whose timing the game runs this weapon with: the profile's pick, else what the vdata gives (the addon's where it has the block); null when neither.</summary>
    private WeaponLab.Core.Weapons.TimingGun? TimingGunOf(WeaponLab.Core.Weapons.WeaponEntry weapon)
    {
        if (_connection is not { } connection)
        {
            return null;
        }

        try
        {
            var (addon, vdata) = SelectedAddonVData();
            return WeaponLab.Core.Weapons.GameTimings.Resolve(connection, weapon, vdata, addon, _profile.Weapon(weapon.Id).TimingGun);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>"the Nova's reload (from the vdata)" plus its warnings, for the weapon panel.</summary>
    private string TimingSummary(WeaponLab.Core.Weapons.WeaponEntry weapon) =>
        TimingGunOf(weapon) is { } gun
            ? $"{gun.Describe()}. Pick another in Anim Events (display only)." + string.Concat(gun.Warnings().Select(warning => " " + warning))
            : "None: no vdata block plays this slot.";

    /// <summary>Writes the game's timing marks and the clips' lengths for the Blender add-on; a failure only costs those.</summary>
    private void WriteGameTiming(WeaponLab.Core.Sandbox.SandboxInfo sandbox)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        try
        {
            var (addon, vdata) = SelectedAddonVData();
            WeaponLab.Core.Weapons.GameTimings.Write(sandbox, connection, vdata, addon, _profile.Weapon(sandbox.Manifest.WeaponId).TimingGun);
            WeaponLab.Core.Blender.ClipLengths.Write(sandbox);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log($"{sandbox.Manifest.WeaponId}: the game timing for Blender could not be written: {error.Message}");
        }
    }

    /// <summary>The weapon panel's summary of the Build .blend choices: animations, skeleton, model, arms.</summary>
    private string BuildSummary(string weaponId)
    {
        var settings = _profile.Weapon(weaponId);
        var setup = _sandboxes.Exists(weaponId) ? CustomSkeletons.Load(_sandboxes.Open(weaponId)) : null;
        var fromAddon = settings.BuildFromAddon ?? (_sandboxes.Exists(weaponId) && _sandboxes.Open(weaponId).Manifest.SourceAddon is not null);
        var animations = fromAddon ? "Animations from the addon" : "Animations from the game files";
        var skeleton = setup is null
            ? settings.CustomSkeleton is { } choice ? $"skeleton {Path.GetFileName(choice.Path)} (not made yet)" : "CS2's default skeleton"
            : $"{(setup.FromAddon ? "the addon's" : "custom")} skeleton {Path.GetFileName(setup.Path)} ({setup.Bones.Count} bones, {setup.ExtraBones.Count} new)";
        var model = settings.PreviewModel is { } preview ? Path.GetFileNameWithoutExtension(preview) : setup is not null ? Path.GetFileNameWithoutExtension(setup.ModelCopy) : "CS2's model";
        var sleeves = settings.ArmsModel switch { null => "default sleeves", "" => "no sleeves", var path => Path.GetFileNameWithoutExtension(path) };
        return $"{animations}, {skeleton}, model {model}, {sleeves}.";
    }
}
