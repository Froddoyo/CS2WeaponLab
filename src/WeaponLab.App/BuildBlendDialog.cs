using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using WeaponLab.Core.Profiles;

namespace WeaponLab.App;

public enum SkeletonSource
{
    /// <summary>The slot's own CS2 skeleton.</summary>
    Own,

    /// <summary>The skeleton the target addon's vdata gives the slot, used as it is.</summary>
    Addon,

    /// <summary>A skeleton Weapon Lab builds from a model's bones.</summary>
    Custom,
}

/// <summary>What the Build .blend window starts from: the current choices and what is available.</summary>
/// <param name="ResolveModel">Turns a picked .vmdl/.vmdl_c file (or a typed key) into a model key; throws with a readable message when it cannot.</param>
/// <param name="DescribeModel">How a model key's bones compare with the slot's skeleton.</param>
/// <param name="DescribeFile">How a skeleton file's bones compare with the slot's skeleton.</param>
/// <param name="BrowseFolder">Where the file picker starts (the target addon's folder).</param>
/// <param name="ModelNote">For a model key: when it was compiled, and a warning when its .vmdl source is newer; null for CS2's models.</param>
/// <param name="StockModel">The model the slot's own skeleton shows by default (CS2's), e.g. weapon_snip_ssg08; null if none was found.</param>
/// <param name="WeaponBones">The current weapon skeleton's bones besides its root, offered as props.</param>
/// <param name="PropBones">Bones ticked "Keep apart from the gun" (the profile's PropBones, any skeleton).</param>
/// <param name="ModelProblems">What the model check found about the model the weapon uses in game (<see cref="WeaponLab.Core.Skeletons.ModelCheck"/>).</param>
public sealed record BuildBlendSetup(
    string WeaponId,
    string SlotText,
    string? AddonName,
    int AddonClipCount,
    int ClipCount,
    (string Skeleton, string? Model)? AddonSkeleton,
    string? OwnSkeleton,
    CustomSkeletonChoice? Custom,
    string DefaultCustomPath,
    string? PreviewModel,
    IReadOnlyList<string> Sleeves,
    IReadOnlyList<string> Gloves,
    string? SleevesModel,
    string? GlovesModel,
    bool FromAddon,
    SkeletonSource Skeleton,
    Func<string, string> ResolveModel,
    Func<string, string> DescribeModel,
    Func<string, string> DescribeFile,
    string? BrowseFolder,
    Func<string, string?> ModelNote,
    string? StockModel,
    IReadOnlyList<string> WeaponBones,
    IReadOnlyCollection<string> PropBones,
    IReadOnlyList<string> ModelProblems);

/// <summary>What the user picked in the Build .blend window. A null preview model means the default.</summary>
public sealed record BuildBlendChoices(
    bool FromAddon,
    SkeletonSource Skeleton,
    CustomSkeletonChoice? Custom,
    string? PreviewModel,
    string? SleevesModel,
    string? GlovesModel,
    IReadOnlyCollection<string> PropBones);

/// <summary>
/// The Build .blend window: which animations fill the slot, the skeleton they
/// drive, the weapon model, and the arm preview. Weapons are animation slots
/// (the root graph switches on weapon_type), so the slot is fixed and named
/// up front; everything that fills it is the user's choice. Models are
/// CS2's default or a file picked on disk.
/// </summary>
public static class BuildBlendDialog
{
    private const string DefaultLabel = "Default";
    private const string NoneLabel = "None";

    public static async Task<BuildBlendChoices?> ShowAsync(Window owner, BuildBlendSetup setup)
    {
        BuildBlendChoices? result = null;
        var dialog = new Window
        {
            Title = $"Build .blend for {setup.WeaponId}",
            Icon = owner.Icon,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        // Animations
        var fromGame = new RadioButton { GroupName = "source", Content = Plain("From the game files: CS2's own clips for this slot") };
        var fromAddon = new RadioButton
        {
            GroupName = "source",
            Content = Plain(setup.AddonName is null ? "From the addon (no target addon)" : $"From {setup.AddonName}: {setup.AddonClipCount} of {setup.ClipCount} clips are the addon's own, the rest from the game"),
            IsEnabled = setup.AddonName is not null && (setup.AddonClipCount > 0 || setup.AddonSkeleton is not null),
        };
        (setup.FromAddon && fromAddon.IsEnabled ? fromAddon : fromGame).IsChecked = true;

        // Weapon model: the Default line names the model it stands for, which follows the skeleton choice.
        var defaultText = Plain("");
        var defaultModel = new RadioButton { GroupName = "model", Content = defaultText };
        var customModel = new RadioButton { GroupName = "model", Content = Plain("Custom model (a compiled .vmdl_c, for example one you built in Blender, with its own bones)") };
        var (previewPicker, previewModel) = ModelPicker(dialog, setup.PreviewModel, setup.BrowseFolder);
        previewPicker.IsEnabled = setup.PreviewModel is not null;
        var modelNote = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 540, FontSize = 11, Opacity = 0.8, IsVisible = false };
        (setup.PreviewModel is null ? defaultModel : customModel).IsChecked = true;

        // Skeleton: chosen here or later in the Animations tab's skeleton panel; both save the same setting.
        // Nothing reaches the addon until the next push.
        const string FilePrefix = WeaponLab.Core.Skeletons.CustomSkeletons.FilePrefix;
        var current = setup.Skeleton == SkeletonSource.Custom ? setup.Custom : null;
        var currentIsFile = current?.Model.StartsWith(FilePrefix, StringComparison.Ordinal) == true;
        var ownSkeleton = new RadioButton { GroupName = "skeleton", Content = Plain($"CS2 default for this slot ({Path.GetFileName(setup.OwnSkeleton ?? "unknown")})") };
        var addonSkeleton = new RadioButton
        {
            GroupName = "skeleton",
            Content = Plain(setup.AddonSkeleton is { } found
                ? $"{setup.AddonName}'s own: {Path.GetFileName(found.Skeleton)}{(found.Model is null ? "" : $" (model {Path.GetFileName(found.Model)})")}, played by its own clips"
                : "The addon's own (none: the addon's clips for this slot use CS2's skeleton)"),
            IsEnabled = setup.AddonSkeleton is not null,
        };
        var keepSkeleton = new RadioButton
        {
            GroupName = "skeleton",
            Content = Plain(current is { } kept && !currentIsFile ? $"Keep the current one: {Path.GetFileName(kept.Path)}, built from {Path.GetFileNameWithoutExtension(kept.Model)}" : ""),
            IsVisible = current is not null && !currentIsFile,
        };
        var modelSkeleton = new RadioButton { GroupName = "skeleton", Content = Plain("Built from the custom model above: CS2's bones plus the model's extra ones") };
        var modelHierarchy = new CheckBox
        {
            Content = Plain("Use the model's own hierarchy for a full custom animset (bones keep the model's parents and rest; CS2's clips keep their motion relative to each bone's rest)"),
            Margin = new Thickness(28, 0, 0, 0),
            IsChecked = current?.ModelHierarchy == true,
        };
        var fileSkeleton = new RadioButton { GroupName = "skeleton", Content = Plain("A skeleton file (.vnmskel_c)") };
        var (filePicker, fileBox) = ModelPicker(dialog, currentIsFile ? current!.Model[FilePrefix.Length..] : null, setup.BrowseFolder, "Skeleton", ["*.vnmskel_c"],
            "A .vnmskel_c skeleton (in the addon, or anywhere: it is copied in on push)");
        var skeletonDetails = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 540, FontSize = 11, Opacity = 0.8 };
        (setup.Skeleton switch
        {
            SkeletonSource.Addon when setup.AddonSkeleton is not null => addonSkeleton,
            SkeletonSource.Custom when currentIsFile => fileSkeleton,
            SkeletonSource.Custom when current is not null => keepSkeleton,
            _ => ownSkeleton,
        }).IsChecked = true;

        string? PickedModel()
        {
            try
            {
                return customModel.IsChecked == true && previewModel.Text is { Length: > 0 } text ? setup.ResolveModel(text) : null;
            }
            catch (Exception problem) when (problem is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        static string Name(string? model) => model is null ? "" : Path.GetFileNameWithoutExtension(model.EndsWith("_c", StringComparison.Ordinal) ? model[..^2] : model);

        void RefreshSkeleton(bool modelChanged)
        {
            previewPicker.IsEnabled = customModel.IsChecked == true;
            var picked = PickedModel();
            modelSkeleton.IsEnabled = picked is not null;
            if (modelSkeleton.IsChecked == true && picked is null)
            {
                ownSkeleton.IsChecked = true;
            }

            modelHierarchy.IsVisible = modelSkeleton.IsChecked == true;
            modelNote.Text = customModel.IsChecked == true && picked is not null ? setup.ModelNote(picked) ?? "" : "";
            modelNote.IsVisible = modelNote.Text.Length > 0;
            if (modelNote.Text.StartsWith('⚠'))
            {
                modelNote.Foreground = Avalonia.Media.Brushes.Orange;
            }
            else
            {
                modelNote.ClearValue(TextBlock.ForegroundProperty);
            }
            // 0.21: Default is always CS2's model; a skeleton built from a model stores that model as the pick when it is made.
            defaultText.Text = "Default: " + (setup.StockModel is { } stock ? $"{Name(stock)} (CS2)" : "CS2's model for this slot");

            filePicker.IsVisible = fileSkeleton.IsChecked == true;
            var description = picked is null ? null : setup.DescribeModel(picked);

            // A freshly picked model with bones CS2's skeleton lacks suggests building the skeleton from it (shown, not hidden).
            if (modelChanged && description is not null && !description.Contains("New bones: none", StringComparison.Ordinal) && ownSkeleton.IsChecked == true)
            {
                modelSkeleton.IsChecked = true;
            }

            skeletonDetails.Text = modelSkeleton.IsChecked == true ? description ?? ""
                : fileSkeleton.IsChecked == true && fileBox.Text is { Length: > 0 } file ? setup.DescribeFile(file.Trim().Trim('"'))
                : "";
        }

        foreach (var radio in new[] { ownSkeleton, addonSkeleton, keepSkeleton, modelSkeleton, fileSkeleton })
        {
            radio.IsCheckedChanged += (_, _) => RefreshSkeleton(modelChanged: false);
        }

        customModel.IsCheckedChanged += (_, _) => RefreshSkeleton(modelChanged: true);
        previewModel.TextChanged += (_, _) => RefreshSkeleton(modelChanged: true);
        fileBox.TextChanged += (_, _) => RefreshSkeleton(modelChanged: false);
        RefreshSkeleton(modelChanged: false);

        // Props: bones kept apart from the gun (0.23). The current skeleton's bones; ticks of bones another skeleton has are kept.
        var propBoxes = setup.WeaponBones
            .Select(bone => new CheckBox { Content = Plain(bone), IsChecked = setup.PropBones.Contains(bone), Margin = new Thickness(0, 0, 14, 0), MinWidth = 0 })
            .ToList();
        var propPanel = new WrapPanel { MaxWidth = 540 };
        foreach (var box in propBoxes)
        {
            propPanel.Children.Add(box);
        }

        var props = new ScrollViewer { MaxHeight = 110, Content = propPanel, IsVisible = propBoxes.Count > 0 };
        var modelProblems = new TextBlock
        {
            Text = string.Join("\n", setup.ModelProblems.Select(problem => "⚠ " + problem)),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 540, FontSize = 11, Foreground = Avalonia.Media.Brushes.Orange,
            IsVisible = setup.ModelProblems.Count > 0,
        };
        var skeletonPath = setup.ModelProblems
            .Select(problem => System.Text.RegularExpressions.Regex.Match(problem, @"animation/skeletons/[^\s,;:()""]+?\.vnmskel(?!_c)"))
            .FirstOrDefault(match => match.Success)?.Value;
        var copyPath = new Button { Content = "Copy skeleton path (for ModelDoc)", Padding = new Thickness(8, 1), IsVisible = skeletonPath is not null };
        copyPath.Click += async (_, _) =>
        {
            if (skeletonPath is not null && dialog.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(skeletonPath);
                copyPath.Content = "Copied " + skeletonPath;
            }
        };

        // Arms
        var (sleevesCombo, sleevesKeys) = PartCombo(setup.Sleeves, setup.SleevesModel);
        var (glovesCombo, glovesKeys) = PartCombo(setup.Gloves, setup.GlovesModel);

        var error = new TextBlock { Foreground = Avalonia.Media.Brushes.IndianRed, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 540, IsVisible = false };
        var build = new Button { Content = "Build .blend", IsDefault = true, MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
        build.Classes.Add("suggested");
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => dialog.Close();
        build.Click += (_, _) =>
        {
            try
            {
                string? preview = null;
                if (customModel.IsChecked == true)
                {
                    preview = previewModel.Text is { Length: > 0 } text ? setup.ResolveModel(text) : throw new ArgumentException("Pick the custom weapon model, or choose Default.");
                }

                var (source, custom) = ownSkeleton.IsChecked == true ? (SkeletonSource.Own, (CustomSkeletonChoice?)null)
                    : addonSkeleton.IsChecked == true ? (SkeletonSource.Addon, null)
                    : keepSkeleton.IsChecked == true ? (SkeletonSource.Custom, current)
                    : modelSkeleton.IsChecked == true
                        ? (SkeletonSource.Custom, new CustomSkeletonChoice(preview ?? throw new ArgumentException("Pick the custom model whose bones make the skeleton."), setup.DefaultCustomPath, modelHierarchy.IsChecked == true))
                        : (SkeletonSource.Custom, new CustomSkeletonChoice(
                            FilePrefix + Path.GetFullPath(fileBox.Text is { Length: > 0 } file ? file.Trim().Trim('"') : throw new ArgumentException("Pick the skeleton file.")), ""));

                var listed = setup.WeaponBones.ToHashSet(StringComparer.Ordinal);
                var propBones = setup.PropBones.Where(bone => !listed.Contains(bone))
                    .Concat(setup.WeaponBones.Where((_, index) => propBoxes[index].IsChecked == true))
                    .ToList();
                result = new BuildBlendChoices(
                    fromAddon.IsChecked == true,
                    source,
                    custom,
                    preview,
                    sleevesKeys[Math.Max(0, sleevesCombo.SelectedIndex)],
                    glovesKeys[Math.Max(0, glovesCombo.SelectedIndex)],
                    propBones);
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
            MinWidth = 560,
            Children =
            {
                Label("Animation slot"),
                new TextBlock { Text = setup.SlotText, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 540 },
                new TextBlock
                {
                    Text = "The game picks animations by slot (the weapon's class), so this is what you edit and deliver to. What fills the slot is chosen below.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 540, FontSize = 11, Opacity = 0.7,
                },
                Label("Animations"),
                fromGame,
                fromAddon,
                Label("Weapon model shown in Blender"),
                defaultModel,
                customModel,
                previewPicker,
                modelNote,
                Label("Skeleton the clips drive (also in the Animations tab's skeleton panel; reaches the addon on your next push)"),
                ownSkeleton,
                addonSkeleton,
                keepSkeleton,
                modelSkeleton,
                modelHierarchy,
                fileSkeleton,
                filePicker,
                skeletonDetails,
                modelProblems,
                copyPath,
                Label("Keep apart from the gun: props that hold still while the gun moves, e.g. a spare magazine held in the other hand (off = a normal gun part)"),
                props,
                Label("Arms in Blender: sleeves (agent model) and gloves"),
                sleevesCombo,
                glovesCombo,
                error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { cancel, build } },
            },
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>
    /// Picks the gun model the viewport and Build .blend show: Default (named)
    /// or a compiled model. The same setting as the Build .blend window's model.
    /// </summary>
    /// <param name="resolve">Turns a typed or browsed path into the profile's model key; throws when it cannot be used.</param>
    /// <param name="note">A line about a picked model (its compile date, a newer source), or null.</param>
    /// <returns>Changed, and the model key (null for Default); unchanged when cancelled.</returns>
    public static async Task<(bool Changed, string? Model)> PreviewModelAsync(Window owner, string weaponId, string? current, string defaultName, string? browseFolder,
        Func<string, string> resolve, Func<string, string?> note)
    {
        (bool, string?) result = (false, current);
        var dialog = new Window
        {
            Title = $"Viewport gun model for {weaponId}",
            Icon = owner.Icon,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var defaultModel = new RadioButton { GroupName = "model", Content = Plain($"Default ({defaultName})") };
        var customModel = new RadioButton { GroupName = "model", Content = Plain("Custom model (a compiled .vmdl_c)") };
        var (picker, box) = ModelPicker(dialog, current, browseFolder);
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 540, FontSize = 11, Opacity = 0.8 };
        (current is null ? defaultModel : customModel).IsChecked = true;
        picker.IsEnabled = current is not null;
        customModel.IsCheckedChanged += (_, _) => picker.IsEnabled = customModel.IsChecked == true;
        box.TextChanged += (_, _) =>
        {
            try
            {
                status.Text = box.Text is { Length: > 0 } text ? note(resolve(text)) ?? "" : "";
            }
            catch (Exception problem) when (problem is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                status.Text = "Cannot use this model: " + problem.Message;
            }
        };

        var ok = new Button { Content = "Use it", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) =>
        {
            if (defaultModel.IsChecked == true)
            {
                result = (current is not null, null);
                dialog.Close();
                return;
            }

            try
            {
                var key = resolve(box.Text ?? "");
                result = (key != current, key);
                dialog.Close();
            }
            catch (Exception problem) when (problem is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                status.Text = "Cannot use this model: " + problem.Message;
            }
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 8,
            Children =
            {
                Plain("Preview only: what the viewport shows and the next Build .blend uses. The game shows the model the weapon's vdata m_szWorldModel names."),
                defaultModel,
                customModel,
                picker,
                status,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            },
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    // A TextBlock as content: a plain string would treat "_" as an access key and hide it.
    private static TextBlock Plain(string text) => new() { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520 };

    /// <summary>A path box (typing a key works too) with a Browse… button, for compiled models (.vmdl_c) or other files.</summary>
    internal static (Control Picker, TextBox Box) ModelPicker(Window dialog, string? current, string? startFolder, string kind = "Compiled Source 2 model", string[]? patterns = null, string hint = "A compiled .vmdl_c in the addon's game folder")
    {
        var box = new TextBox { Text = current ?? "", PlaceholderText = hint, MinWidth = 440 };
        var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += async (_, _) =>
        {
            var start = startFolder is not null && Directory.Exists(startFolder) ? await dialog.StorageProvider.TryGetFolderFromPathAsync(new Uri(startFolder)) : null;
            var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Pick a " + kind.ToLowerInvariant(),
                AllowMultiple = false,
                SuggestedStartLocation = start,
                FileTypeFilter = [new FilePickerFileType(kind) { Patterns = patterns ?? ["*.vmdl_c"] }],
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            {
                box.Text = path;
            }
        };

        var row = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(box);
        return (row, box);
    }

    /// <summary>A picker for a preview part: Default, None, then every model; keys are null, "" and the model path.</summary>
    private static (ComboBox Combo, IReadOnlyList<string?> Keys) PartCombo(IReadOnlyList<string> models, string? current)
    {
        var keys = new List<string?> { null, "" };
        keys.AddRange(models);
        var labels = new List<string> { DefaultLabel, NoneLabel };
        labels.AddRange(models.Select(model => Path.GetFileNameWithoutExtension(model)));
        var combo = new ComboBox { ItemsSource = labels, HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = Math.Max(0, keys.IndexOf(current)) };
        return (combo, keys);
    }
}
