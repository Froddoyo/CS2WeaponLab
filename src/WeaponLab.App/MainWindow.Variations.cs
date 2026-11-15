using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using WeaponLab.Core.Graphs;
using WeaponLab.Core.Weapons;

namespace WeaponLab.App;

/// <summary>
/// Variations (0.24): rare draws and inspects (a clip in a weighted option) and a random reload (two reload clips
/// picked by the reserve ammo), saved as graph edits in the profile and delivered like every graph edit.
/// </summary>
public sealed partial class MainWindow
{
    private async void OnVariationsClicked(object? sender, RoutedEventArgs e)
    {
        if (_weapon is { } weapon)
        {
            await ShowVariationsAsync(weapon);
        }
    }

    private async void OnVariationsMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender) is { Weapon: { } weapon })
        {
            SelectWeapon(weapon);
            await ShowVariationsAsync(weapon);
        }
    }

    private async Task ShowVariationsAsync(WeaponEntry weapon)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        IReadOnlyList<(string Path, GraphDocument Document)> graphs;
        try
        {
            graphs = Variations.Graphs(connection, _profile, Target(), weapon);
        }
        catch (Exception problem) when (problem is IOException or InvalidDataException)
        {
            await Dialogs.ShowAsync(this, "Variations", $"{weapon.Id}'s graphs could not be read: {problem.Message}");
            return;
        }

        var groups = Variations.Groups(graphs, _profile);
        var reload = Variations.RandomReloadFor(graphs, _profile);
        if (groups.Count == 0 && reload is null)
        {
            await Dialogs.ShowAsync(this, "Variations", $"{weapon.Id}'s graphs have no weighted draw or inspect choices and no random-reload spot.");
            return;
        }

        // The clips an option can play: the weapon's own (the addon's copy is played when it has one).
        var clips = weapon.ClipPaths.Select(path => path[..^2]).ToList();
        string Label(string? clip) => clip is null ? "(none)" : Path.GetFileNameWithoutExtension(clip);

        var dialog = new Window
        {
            Title = $"Variations: {weapon.Id}",
            Icon = Icon,
            Width = 640,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 760,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Wrapped("A weighted choice picks one option by weight among the options that have a clip, rolled again on every draw (or inspect): "
            + "weights 1 and 8 play the second 8 times in 9 (whole numbers). A rare draw or inspect is a clip in an empty option with a small weight next to big ones. Saved as graph edits; push to deliver.", muted: true));

        var rows = new List<(VariationGroup Group, int Position, ComboBox Clip, TextBox Weight, VariationOption Before)>();
        foreach (var group in groups)
        {
            body.Children.Add(new TextBlock { Text = group.Name, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            foreach (var option in group.Options)
            {
                var choices = new List<string> { option.Clip is null ? "(none)" : Label(option.Clip) + " (now)" };
                choices.AddRange(clips.Where(clip => clip != option.Clip).Select(Label));
                var combo = new ComboBox { ItemsSource = choices, SelectedIndex = 0, Width = 300 };
                var weight = new TextBox { Text = option.Weight.ToString("0.###", CultureInfo.InvariantCulture), Width = 70 };
                body.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { new TextBlock { Text = $"Option {option.Position + 1}", Width = 70, VerticalAlignment = VerticalAlignment.Center }, combo, new TextBlock { Text = "weight", VerticalAlignment = VerticalAlignment.Center }, weight },
                });
                rows.Add((group, option.Position, combo, weight, option));
            }
        }

        CheckBox? randomBox = null;
        ComboBox? reloadA = null, reloadB = null;
        TextBox? weightA = null, weightB = null;
        if (reload is not null)
        {
            var reloads = clips.Where(clip => Path.GetFileName(clip).Contains("reload", StringComparison.OrdinalIgnoreCase)).Concat(clips).Distinct().ToList();
            body.Children.Add(new TextBlock { Text = "Random reload", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
            body.Children.Add(Wrapped("Plays one of two reload clips. The pick follows the reserve ammo, which changes on every reload, so it looks random "
                + "(verified on PC2 with the AK, 2026-10-29). It uses the graph's second draw choice, so both draws then play the first draw choice's options.", muted: true));
            randomBox = new CheckBox { Content = "Pick between two reloads", IsChecked = reload.On };
            reloadA = new ComboBox { ItemsSource = reloads.Select(Label).ToList(), Width = 300, SelectedIndex = Math.Max(0, reloads.IndexOf(reload.Clips[0] ?? reloads[0])) };
            reloadB = new ComboBox { ItemsSource = reloads.Select(Label).ToList(), Width = 300, SelectedIndex = Math.Max(0, reloads.IndexOf(reload.Clips[1] ?? reloads[Math.Min(1, reloads.Count - 1)])) };
            weightA = new TextBox { Text = reload.Weights[0].ToString("0.###", CultureInfo.InvariantCulture), Width = 70 };
            weightB = new TextBox { Text = reload.Weights[1].ToString("0.###", CultureInfo.InvariantCulture), Width = 70 };
            body.Children.Add(randomBox);
            body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Reload A", Width = 70, VerticalAlignment = VerticalAlignment.Center }, reloadA, new TextBlock { Text = "weight", VerticalAlignment = VerticalAlignment.Center }, weightA } });
            body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Reload B", Width = 70, VerticalAlignment = VerticalAlignment.Center }, reloadB, new TextBlock { Text = "weight", VerticalAlignment = VerticalAlignment.Center }, weightB } });
            ReloadVariationTip(reloads, reloadA, reloadB);
        }

        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var save = new Button { Content = "Save", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += (_, _) =>
        {
            try
            {
                var changed = 0;
                foreach (var (group, position, combo, weightBox, before) in rows)
                {
                    var weight = ParseWeight(weightBox.Text);
                    var clip = combo.SelectedIndex <= 0 ? null : clips.Where(item => item != before.Clip).ElementAt(combo.SelectedIndex - 1);
                    if (clip is null && Math.Abs(weight - before.Weight) < 1e-9)
                    {
                        continue;
                    }

                    if (clip is null && before.Clip is null && weight > 0)
                    {
                        throw new ArgumentException($"{group.Name}, option {position + 1}: give it a clip before a weight (an option with no clip is skipped in game).");
                    }

                    Merge(group.Graph, Variations.EditsFor(group, position, clip, weight));
                    changed++;
                }

                if (reload is not null && randomBox is not null)
                {
                    var reloads = clips.Where(item => Path.GetFileName(item).Contains("reload", StringComparison.OrdinalIgnoreCase)).Concat(clips).Distinct().ToList();
                    if (randomBox.IsChecked == true)
                    {
                        Merge(reload.Graph, Variations.RandomReloadEdits(reload, reloads[reloadA!.SelectedIndex], reloads[reloadB!.SelectedIndex], ParseWeight(weightA!.Text), ParseWeight(weightB!.Text)));
                        changed++;
                    }
                    else if (reload.On && _profile.Graphs.TryGetValue(reload.Graph, out var held))
                    {
                        foreach (var key in Variations.RandomReloadKeys(reload))
                        {
                            held.Remove(key);
                        }

                        changed++;
                    }
                }

                SaveProfile();
                Log(changed == 0 ? $"{weapon.Id}: variations unchanged." : $"{weapon.Id}: variations saved ({changed} change(s)); push in the Deliver tab to deliver them.");
                dialog.Close();
            }
            catch (Exception problem) when (problem is ArgumentException or InvalidOperationException)
            {
                error.Text = problem.Message;
                error.IsVisible = true;
            }
        };

        body.Children.Add(error);
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(18),
            Children =
            {
                new StackPanel { [DockPanel.DockProperty] = Dock.Bottom, Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { cancel, save } },
                new ScrollViewer { Content = body },
            },
        };
        await dialog.ShowDialog(this);
        RebuildTree();
        await RefreshStatusAsync();
        RefreshCurrentTool();
    }

    private void Merge(string graph, IReadOnlyDictionary<string, string> edits)
    {
        if (!_profile.Graphs.TryGetValue(graph, out var held))
        {
            _profile.Graphs[graph] = held = new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        foreach (var (key, value) in edits)
        {
            held[key] = value;
        }
    }

    /// <summary>CS2 stores option weights as whole numbers.</summary>
    private static double ParseWeight(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) && weight >= 0
            ? weight
            : throw new ArgumentException("A weight is a whole number of 0 or more, e.g. 1 or 8 (CS2 stores whole numbers).");

    private static void ReloadVariationTip(IReadOnlyList<string> reloads, ComboBox a, ComboBox b)
    {
        ToolTip.SetTip(a, "The first reload clip (the weapon's own reload is a good start).");
        ToolTip.SetTip(b, "The second reload clip. Pick a different one, or the same one twice to keep a single reload.");
        if (reloads.Count > 1 && b.SelectedIndex == a.SelectedIndex)
        {
            b.SelectedIndex = (a.SelectedIndex + 1) % reloads.Count;
        }
    }

    private static TextBlock Wrapped(string text, bool muted) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = muted ? 11 : 12, Opacity = muted ? 0.75 : 1 };
}
