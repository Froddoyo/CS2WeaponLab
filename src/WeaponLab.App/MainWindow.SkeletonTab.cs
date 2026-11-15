using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.Skeletons;
using WeaponLab.Core.VData;

namespace WeaponLab.App;

/// <summary>
/// The skeleton panel (Animations tab, above History; the Skeleton tab is gone
/// since 0.16): which skeleton the selected weapon's clips drive, whether the
/// addon has it, and changing it (CS2 default for the slot, the addon's own,
/// one built from a model, or a skeleton file). The bones themselves are under
/// the weapon's Skeleton node in the outliner and in the viewport. A push
/// ships a custom skeleton with all of the weapon's clips.
/// </summary>
public sealed partial class MainWindow
{
    private void RefreshSkeletonPanel()
    {
        if (_connection is not { } connection || _weapon is not { } weapon)
        {
            SkeletonPanelTitle.Text = "Skeleton";
            SkeletonTabSummary.Text = "Pick a weapon in the outliner.";
            SkeletonTabState.Text = "";
            SkeletonStateDot.IsVisible = false;
            SkeletonChangeButton.IsEnabled = false;
            ViewportModelButton.IsEnabled = false;
            ViewportModelText.Text = "";
            return;
        }

        SkeletonChangeButton.IsEnabled = true;
        ViewportModelButton.IsEnabled = true;
        var preview = _profile.Weapon(weapon.Id).PreviewModel;
        ViewportModelText.Text = preview is null ? $"Viewport model: default ({DefaultViewportModelName(weapon)})" : $"Viewport model: {Path.GetFileNameWithoutExtension(preview.EndsWith("_c", StringComparison.Ordinal) ? preview[..^2] : preview)}";
        try
        {
            var (skeleton, setup) = SkeletonOf(connection, weapon);
            SkeletonPanelTitle.Text = skeleton is null ? "Skeleton: none" : $"Skeleton: {Path.GetFileName(skeleton.ResourcePath)} ({skeleton.Bones.Count} bones)";
            SkeletonTabSummary.Text = setup is null
                ? $"CS2 default for this slot ({(skeleton is null ? "none" : Path.GetFileName(skeleton.ResourcePath))})."
                : setup.SourceModel.StartsWith(CustomSkeletons.FilePrefix, StringComparison.Ordinal)
                    ? $"Skeleton file {Path.GetFileName(setup.Path)}: {setup.Bones.Count} bones, {setup.ExtraBones.Count} beyond CS2's."
                    : setup.FromAddon
                        ? $"The addon's own skeleton {Path.GetFileName(setup.Path)}: {setup.Bones.Count} bones, {setup.ExtraBones.Count} beyond CS2's."
                        : $"Custom skeleton {Path.GetFileName(setup.Path)} built from {Path.GetFileNameWithoutExtension(setup.ModelCopy)}{(setup.ModelHierarchy ? " with the model's own hierarchy" : "")}: {setup.Bones.Count} bones, {setup.ExtraBones.Count} new."
                            + (setup.UnweightedBones is { Count: > 0 } empty ? $" No mesh on {string.Join(", ", empty)} (often bones from animation files the .vmdl lists; hidden in Blender)." : "")
                            + (setup.SkippedBones is { Count: > 0 } skipped ? $" Left out FBX leaf bones: {string.Join(", ", skipped)}." : "")
                            + $" In game its bones move only with a model that uses it: in ModelDoc set your model's NmSkeletonReference Source File to {setup.Path} (Copy asset path on the Skeleton row) and compile it.";

            var (state, tip) = SkeletonState(weapon, AspectStatus(weapon, StatusAspect.Animations).Reasons);
            SkeletonStateDot.IsVisible = state != WeaponState.Untouched;
            SkeletonStateDot.Fill = (Avalonia.Media.IBrush?)Converters.StateBrush.Convert(state, typeof(Avalonia.Media.IBrush), null, System.Globalization.CultureInfo.InvariantCulture);
            // The panel is narrow: the first lines of the state, then a count (the weapon's tooltip and the Deliver tab have the rest).
            var lines = tip.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var shown = lines.Length <= 3 ? tip : string.Join("\n", lines.Take(2)) + $"\n… and {lines.Length - 2} more";
            SkeletonTabState.Text = setup is null ? "Nothing to push." : shown;
            ToolTip.SetTip(SkeletonTabState, setup is null ? null : tip + " Rebuild the .blend after changing it.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            SkeletonTabSummary.Text = "The skeleton could not be read: " + error.Message;
        }
    }

    /// <summary>What Default shows: CS2's model for the slot (0.21; before, a custom skeleton's model).</summary>
    private static string DefaultViewportModelName(WeaponLab.Core.Weapons.WeaponEntry weapon) => $"CS2's model for {weapon.Id}";

    private async void OnViewportModelClicked(object? sender, RoutedEventArgs e)
    {
        if (_connection is not { } connection || _weapon is not { } weapon)
        {
            return;
        }

        var settings = _profile.EnsureWeapon(weapon.Id);
        var (changed, model) = await BuildBlendDialog.PreviewModelAsync(this, weapon.Id, settings.PreviewModel, DefaultViewportModelName(weapon), Target()?.GamePath,
            text => PreviewModels.FromFile(connection, text),
            key => PreviewModels.Freshness(connection, key)?.Describe(Path.GetFileNameWithoutExtension(key)));
        if (!changed)
        {
            return;
        }

        settings.PreviewModel = model;
        SaveProfile();
        Log($"{weapon.Id}: the viewport shows {(model is null ? "the default model" : Path.GetFileNameWithoutExtension(model))}; the next Build .blend uses it too.");
        RefreshSkeletonPanel();
        InvalidateViewport();
    }

    private async void OnChangeSkeletonClicked(object? sender, RoutedEventArgs e)
    {
        if (_connection is not { } connection || _weapon is not { } weapon)
        {
            return;
        }

        var target = Target();
        var addonSkeleton = target is null ? null : CustomSkeletons.FindInAddon(connection, target.Name, weapon.Id);
        var current = _profile.Weapon(weapon.Id).CustomSkeleton;
        var picked = await ChangeSkeletonDialogAsync(weapon, addonSkeleton, current, target?.GamePath);
        if (picked is not { } choice || await EnsureSandboxAsync(connection, weapon) is not { } sandbox)
        {
            return;
        }

        try
        {
            SkeletonChangeButton.IsEnabled = false;
            await ApplySkeletonAsync(connection, weapon, sandbox, choice.Source, choice.Custom, new Progress<string>(SetStatus));
            Log($"{weapon.Id}: skeleton changed. Rebuild the .blend to see it in Blender; the Deliver tab ships it.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            await Dialogs.ShowAsync(this, "Change skeleton", $"The skeleton was not changed: {error.Message}");
        }

        RebuildTree();
        await RefreshStatusAsync();
        RefreshSkeletonPanel();
        InvalidateViewport();
    }

    /// <summary>The Change skeleton window: CS2 default, the addon's own, built from a model, or a skeleton file.</summary>
    private async Task<(SkeletonSource Source, CustomSkeletonChoice? Custom)?> ChangeSkeletonDialogAsync(
        WeaponLab.Core.Weapons.WeaponEntry weapon, (string Skeleton, string? Model)? addonSkeleton, CustomSkeletonChoice? current, string? browseFolder)
    {
        (SkeletonSource, CustomSkeletonChoice?)? result = null;
        var dialog = new Window
        {
            Title = $"Change {weapon.Id}'s skeleton",
            Icon = Icon,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var isFile = current?.Model.StartsWith(CustomSkeletons.FilePrefix, StringComparison.Ordinal) == true;
        var isAddon = current is not null && addonSkeleton is { } found && current.Path == found.Skeleton && !isFile;
        var stock = new RadioButton { GroupName = "skeleton", Content = Plain("CS2 default for this slot") };
        var addon = new RadioButton
        {
            GroupName = "skeleton",
            Content = Plain(addonSkeleton is { } own ? $"The addon's own: {Path.GetFileName(own.Skeleton)}{(own.Model is null ? "" : $" (model {Path.GetFileName(own.Model)})")}, played by its own clips" : "The addon's own (none: the addon's clips for this weapon use CS2's skeleton)"),
            IsEnabled = addonSkeleton is not null,
        };
        var model = new RadioButton { GroupName = "skeleton", Content = Plain("Build from a compiled model's bones (.vmdl_c)") };
        var hierarchy = new CheckBox
        {
            Content = Plain("Use the model's own hierarchy for a full custom animset (bones keep the model's parents and rest; CS2's clips keep their motion relative to each bone's rest)"),
            IsChecked = current?.ModelHierarchy == true,
        };
        var modelNote = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520, FontSize = 11, Opacity = 0.8 };
        var file = new RadioButton { GroupName = "skeleton", Content = Plain("Use a skeleton file (.vnmskel_c)") };
        (current is null ? stock : isFile ? file : isAddon ? addon : model).IsChecked = true;

        var (modelPicker, modelBox) = BuildBlendDialog.ModelPicker(dialog, current is not null && !isFile && !isAddon ? current.Model : null, browseFolder);
        var pathBox = new TextBox { Text = current is not null && !isFile && !isAddon ? current.Path : CustomSkeletons.DefaultPath(weapon.Id) };
        var (filePicker, fileBox) = BuildBlendDialog.ModelPicker(dialog, isFile ? current!.Model[CustomSkeletons.FilePrefix.Length..] : null, browseFolder, "Skeleton", ["*.vnmskel_c"],
            "A .vnmskel_c skeleton (in the addon, or anywhere: it is copied in on push)");
        var details = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520, FontSize = 11, Opacity = 0.8 };
        var error = new TextBlock { Foreground = Avalonia.Media.Brushes.IndianRed, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520, IsVisible = false };
        var modelPanel = new StackPanel { Spacing = 6, Margin = new Thickness(18, 0, 0, 0), Children = { modelPicker, modelNote, new TextBlock { Text = "Skeleton path in the addon", FontSize = 11, Opacity = 0.7 }, pathBox, hierarchy } };
        var filePanel = new StackPanel { Spacing = 6, Margin = new Thickness(18, 0, 0, 0), Children = { filePicker } };

        void Refresh()
        {
            modelPanel.IsVisible = model.IsChecked == true;
            filePanel.IsVisible = file.IsChecked == true;
            details.Text = "";
            modelNote.Text = "";
            try
            {
                if (_connection is { } noteConnection && model.IsChecked == true && modelBox.Text is { Length: > 0 } picked)
                {
                    modelNote.Text = PreviewModels.Freshness(noteConnection, PreviewModels.FromFile(noteConnection, picked))?.Describe(Path.GetFileNameWithoutExtension(picked)) ?? "";
                    if (modelNote.Text.StartsWith('⚠'))
                    {
                        modelNote.Foreground = Avalonia.Media.Brushes.Orange;
                    }
                    else
                    {
                        modelNote.ClearValue(TextBlock.ForegroundProperty);
                    }
                }

                if (_connection is { } connection && _sandboxes.Exists(weapon.Id))
                {
                    var sandbox = _sandboxes.Open(weapon.Id);
                    CustomSkeletonPreview? preview = model.IsChecked == true && modelBox.Text is { Length: > 0 } key
                        ? CustomSkeletons.Preview(sandbox, connection, PreviewModels.FromFile(connection, key), _profile.TargetAddon)
                        : file.IsChecked == true && fileBox.Text is { Length: > 0 } path ? CustomSkeletons.PreviewFile(sandbox, connection, path.Trim().Trim('"')) : null;
                    if (preview is not null)
                    {
                        details.Text = $"{preview.SharedBones.Count} bone(s) shared with {Path.GetFileName(preview.FromSkeleton)}. New bones: {(preview.ExtraBones.Count == 0 ? "none" : string.Join(", ", preview.ExtraBones))}" + SkeletonNotes(preview);
                    }
                }
            }
            catch (Exception problem) when (problem is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                details.Text = problem.Message;
            }
        }

        foreach (var radio in new[] { stock, addon, model, file })
        {
            radio.IsCheckedChanged += (_, _) => Refresh();
        }

        modelBox.TextChanged += (_, _) => Refresh();
        fileBox.TextChanged += (_, _) => Refresh();
        Refresh();

        var apply = new Button { Content = "Change skeleton", IsDefault = true, MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
        apply.Classes.Add("suggested");
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => dialog.Close();
        apply.Click += (_, _) =>
        {
            try
            {
                result = stock.IsChecked == true ? (SkeletonSource.Own, null)
                    : addon.IsChecked == true ? (SkeletonSource.Addon, null)
                    : model.IsChecked == true
                        ? (SkeletonSource.Custom, new CustomSkeletonChoice(
                            PreviewModels.FromFile(_connection!, modelBox.Text is { Length: > 0 } key ? key : throw new ArgumentException("Pick the model whose bones make the skeleton.")),
                            (pathBox.Text ?? CustomSkeletons.DefaultPath(weapon.Id)).Trim(),
                            hierarchy.IsChecked == true))
                        : (SkeletonSource.Custom, new CustomSkeletonChoice(
                            CustomSkeletons.FilePrefix + Path.GetFullPath(fileBox.Text is { Length: > 0 } path ? path.Trim().Trim('"') : throw new ArgumentException("Pick the skeleton file.")),
                            ""));
                dialog.Close();
            }
            catch (Exception problem) when (problem is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                error.Text = problem.Message;
                error.IsVisible = true;
            }
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 10,
            MinWidth = 540,
            Children =
            {
                Plain("The skeleton the weapon's clips drive. The clips move onto it right away (exported edits too); rebuild the .blend afterwards. Delivery ships a custom skeleton with all of the weapon's clips."),
                stock,
                addon,
                model,
                modelPanel,
                file,
                filePanel,
                details,
                error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, apply } },
            },
        };
        await dialog.ShowDialog(this);
        return result;
    }
}
