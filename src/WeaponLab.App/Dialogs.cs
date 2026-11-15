using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.VData;

namespace WeaponLab.App;

/// <summary>Small modal dialogs in the app's style: confirmation and text input.</summary>
public static class Dialogs
{
    /// <returns>True when the user confirmed.</returns>
    public static async Task<bool> ConfirmAsync(Window owner, string title, string message, string confirmText, bool destructive = false)
    {
        var result = false;
        var dialog = Create(title);
        var confirm = new Button { Content = confirmText, IsDefault = !destructive, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, IsDefault = destructive, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (destructive)
        {
            confirm.Classes.Add("danger");
        }

        confirm.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = Layout(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 460 }, cancel, confirm);
        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>A message with several answers (the first is the default) and Cancel.</summary>
    /// <returns>The index of the answer picked, or -1 for Cancel.</returns>
    public static async Task<int> ChooseAsync(Window owner, string title, string message, params string[] answers)
    {
        var result = -1;
        var dialog = Create(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => dialog.Close();
        buttons.Children.Add(cancel);
        for (var index = 0; index < answers.Length; index++)
        {
            var picked = index;
            var button = new Button { Content = answers[index], IsDefault = index == 0, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) =>
            {
                result = picked;
                dialog.Close();
            };
            buttons.Children.Add(button);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 500 }, buttons },
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>A message with one OK button, for failures that must not go unnoticed in History.</summary>
    public static async Task ShowAsync(Window owner, string title, string message)
    {
        var dialog = Create(title);
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => dialog.Close();
        var body = new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520 };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children = { body, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok } } },
        };
        await dialog.ShowDialog(owner);
    }

    /// <returns>The entered text, or null when cancelled.</returns>
    public static async Task<string?> AskAsync(Window owner, string title, string prompt, string initial)
    {
        string? result = null;
        var dialog = Create(title);
        var box = new TextBox { Text = initial, MinWidth = 320 };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) =>
        {
            result = box.Text?.Trim();
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        var body = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = prompt }, box } };
        dialog.Content = Layout(body, cancel, ok);
        dialog.Opened += (_, _) => box.Focus();
        await dialog.ShowDialog(owner);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    public enum ClassChoice
    {
        Cancel,
        Apply,
        Remove,
    }

    /// <summary>Picks a donor weapon and the parts to copy from it.</summary>
    /// <param name="donors">Block name → readable label.</param>
    public static async Task<(ClassChoice Choice, VDataClassSwap? Swap)> ClassSwapAsync(
        Window owner, string block, IReadOnlyList<(string Block, string Label)> donors, VDataClassSwap? current)
    {
        var choice = ClassChoice.Cancel;
        var dialog = Create($"Base {block} on another weapon");
        var combo = new ComboBox
        {
            ItemsSource = donors.Select(donor => donor.Label).ToList(),
            MinWidth = 320,
            SelectedIndex = Math.Max(0, donors.ToList().FindIndex(donor => donor.Block == current?.From)),
        };
        var boxes = VDataClassSwap.AllParts.Select(part => new CheckBox
        {
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = part.Label, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new TextBlock { Text = part.Description, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420, Opacity = 0.7, FontSize = 11 },
                },
            },
            IsChecked = current is null ? part.OnByDefault : current.Parts.Contains(part.Id),
            Tag = part.Id,
        }).ToList();

        var apply = new Button { Content = "Apply to profile", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var remove = new Button { Content = "Remove class change", IsVisible = current is not null, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        remove.Classes.Add("danger");
        apply.Click += (_, _) =>
        {
            choice = ClassChoice.Apply;
            dialog.Close();
        };
        remove.Click += (_, _) =>
        {
            choice = ClassChoice.Remove;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = $"{block} keeps its name and its place in the game; the parts you tick come from the weapon you pick (CS2's values). " +
                           "Single values you edit in the VData tab still apply on top. Nothing changes in the addon until you save or apply.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                combo,
            },
        };
        foreach (var box in boxes)
        {
            body.Children.Add(box);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children =
            {
                body,
                new DockPanel
                {
                    Children =
                    {
                        remove,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, apply } },
                    },
                },
            },
        };
        await dialog.ShowDialog(owner);

        if (choice != ClassChoice.Apply || combo.SelectedIndex < 0)
        {
            return (choice, null);
        }

        var parts = boxes.Where(box => box.IsChecked == true).Select(box => (string)box.Tag!).ToList();
        return parts.Count == 0
            ? (ClassChoice.Remove, null)
            : (ClassChoice.Apply, new VDataClassSwap { From = donors[combo.SelectedIndex].Block, Parts = parts });
    }

    /// <summary>Asks the user to pick one of several actions; returns its index, or -1 when cancelled.</summary>
    public static async Task<int> ChooseAsync(Window owner, string title, string message, IReadOnlyList<string> choices, int suggested = 0)
    {
        var picked = -1;
        var dialog = Create(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => dialog.Close();
        buttons.Children.Add(cancel);
        for (var index = 0; index < choices.Count; index++)
        {
            var choice = index;
            var button = new Button { Content = choices[index], IsDefault = index == suggested, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (index == suggested)
            {
                button.Classes.Add("suggested");
            }

            button.Click += (_, _) =>
            {
                picked = choice;
                dialog.Close();
            };
            buttons.Children.Add(button);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 480 },
                buttons,
            },
        };
        await dialog.ShowDialog(owner);
        return picked;
    }

    /// <summary>
    /// Picks events to copy from another CS2 clip: a weapon, one of its
    /// clips, its gameplay and sound events (gameplay ticked), and whether to
    /// take its sync track (the loop sections) too.
    /// </summary>
    /// <returns>The source clip, the events picked and whether to copy the sync track; null when cancelled.</returns>
    public static async Task<(Core.Clips.ClipInfo Source, IReadOnlyList<int> Events, bool SyncTrack)?> CopyEventsAsync(
        Window owner, Core.Game.GameConnection connection, string suggestedWeapon, string suggestedClip)
    {
        (Core.Clips.ClipInfo, IReadOnlyList<int>, bool)? result = null;
        var dialog = Create("Copy events from another clip");
        var weapons = connection.Weapons.OrderBy(weapon => weapon.Id, StringComparer.Ordinal).ToList();
        var weaponBox = new ComboBox { ItemsSource = weapons.Select(weapon => weapon.Id).ToList(), MinWidth = 220 };
        var clipBox = new ComboBox { MinWidth = 260 };
        var events = new StackPanel { Spacing = 2 };
        var sync = new CheckBox { Content = "Also take its sync track (the loop sections)" };
        var info = new TextBlock { Classes = { "muted" }, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520 };
        Core.Clips.ClipInfo? source = null;

        void ShowClip()
        {
            events.Children.Clear();
            if (weaponBox.SelectedIndex < 0 || clipBox.SelectedItem is not string path)
            {
                return;
            }

            source = connection.ReadClip(path);
            foreach (var clipEvent in source.Events)
            {
                var copyable = clipEvent.Kind is Core.Clips.ClipEventKind.Gameplay or Core.Clips.ClipEventKind.Sound;
                events.Children.Add(new CheckBox
                {
                    Tag = clipEvent.Index,
                    IsEnabled = copyable,
                    IsChecked = clipEvent.Kind == Core.Clips.ClipEventKind.Gameplay,
                    Content = $"{clipEvent.Kind,-9} {clipEvent.Name}  @ frame {clipEvent.StartFrame(source):0.#}{(clipEvent.Duration > 0 ? $"–{(clipEvent.StartTime + clipEvent.Duration) * source.LastFrame:0.#}" : "")}",
                });
            }

            var named = source.SyncTrack.Where(section => section.Id.Length > 0).ToList();
            sync.IsChecked = named.Count > 0;
            sync.IsEnabled = named.Count > 0;
            info.Text = $"{source.Name}: {source.FrameCount} frames, {source.DurationSeconds:0.00} s. Events land at the same point of your clip (same fraction of its length)."
                + (named.Count > 0 ? $" Sync track: {string.Join(", ", named.Select(section => $"{section.Id} {section.StartTime * source.LastFrame:0}-{section.EndTime * source.LastFrame:0}"))}." : "");
        }

        weaponBox.SelectionChanged += (_, _) =>
        {
            var weapon = weaponBox.SelectedIndex >= 0 ? weapons[weaponBox.SelectedIndex] : null;
            var clips = weapon?.ClipPaths.ToList() ?? [];
            clipBox.ItemsSource = clips;
            clipBox.SelectedItem = clips.FirstOrDefault(path => path.Contains(suggestedClip, StringComparison.Ordinal)) ?? clips.FirstOrDefault();
        };
        clipBox.SelectionChanged += (_, _) => ShowClip();
        weaponBox.SelectedIndex = Math.Max(0, weapons.FindIndex(weapon => weapon.Id == suggestedWeapon));

        var copy = new Button { Content = "Copy", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        copy.Click += (_, _) =>
        {
            if (source is not null)
            {
                var picked = events.Children.OfType<CheckBox>().Where(box => box.IsChecked == true && box.IsEnabled).Select(box => (int)box.Tag!).ToList();
                result = (source, picked, sync.IsChecked == true);
            }

            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Weapon", VerticalAlignment = VerticalAlignment.Center }, weaponBox } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Clip", VerticalAlignment = VerticalAlignment.Center }, clipBox } },
                info,
                new ScrollViewer { MaxHeight = 320, Content = events },
                sync,
            },
        };
        dialog.Content = Layout(body, cancel, copy);
        await dialog.ShowDialog(owner);
        return result;
    }

    private static Window Create(string title) => new()
    {
        Title = title,
        SizeToContent = SizeToContent.WidthAndHeight,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false,
    };

    private static Control Layout(Control body, Button cancel, Button confirm)
    {
        return new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children =
            {
                body,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, confirm } },
            },
        };
    }
}
