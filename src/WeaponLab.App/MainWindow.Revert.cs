using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.Weapons;

namespace WeaponLab.App;

/// <summary>
/// "Revert to addon default" (right-click a yellow row, 0.21): throws away the
/// work the addon does not have yet, so the row matches the addon again. It
/// looks at the open tab's part of the weapon (the part the colour shows) and
/// lists what it would undo before doing anything. Sandbox files move to
/// workspace\backups\revert; the addon and the .blend are not touched.
/// </summary>
public sealed partial class MainWindow
{
    private async void OnRevertMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender) is { Weapon: { } weapon } node)
        {
            await RevertAsync(weapon, node);
        }
    }

    private async Task RevertAsync(WeaponEntry weapon, ExplorerNode node)
    {
        if (_connection is not { } connection || Target() is not { } target)
        {
            return;
        }

        var parts = node.Kind switch
        {
            ExplorerNodeKind.Skeleton => RevertParts.Skeleton,
            ExplorerNodeKind.Clip => RevertParts.Clips,
            ExplorerNodeKind.Graphs or ExplorerNodeKind.Graph => RevertParts.Graphs,
            _ => CurrentAspect() switch
            {
                StatusAspect.Animations or StatusAspect.Sounds => RevertParts.Clips | RevertParts.Skeleton,
                StatusAspect.Graph => RevertParts.Graphs,
                StatusAspect.VData => RevertParts.VData,
                _ => RevertParts.Clips | RevertParts.Skeleton | RevertParts.Graphs,
            },
        };
        var clipId = node.ClipPath is { } path ? ExplorerNode.ClipName(path) : null;
        var graph = node.Kind == ExplorerNodeKind.Graph ? node.GraphPath : null;
        var blocks = BlocksFor(weapon);
        var profile = _profile;
        IReadOnlyList<RevertItem> items;
        try
        {
            items = await Task.Run(() => AddonRevert.Plan(profile, _sandboxes, connection, target, weapon.Id, blocks, parts, clipId, graph));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            await Dialogs.ShowAsync(this, "Revert to addon default", $"Could not compare {weapon.Id} with {target.Name}: {error.Message}");
            return;
        }

        var subject = clipId ?? (graph is not null ? Path.GetFileName(graph) : weapon.Id);
        if (items.Count == 0)
        {
            await Dialogs.ShowAsync(this, "Revert to addon default", $"Nothing of {subject} is waiting to be pushed here, so there is nothing to revert.");
            return;
        }

        if (!items.Any(item => item.CanRevert))
        {
            // A list of greyed-out rows with a greyed-out Revert button reads as broken (user, 2026-11-07): say why instead.
            await Dialogs.ShowAsync(this, "Revert to addon default",
                $"{subject} has work {target.Name} does not have yet, but none of it can be reverted from here:\n\n"
                + string.Join("\n", items.Select(item => $"• {item.Label}: {item.Detail}")));
            return;
        }

        var chosen = await ChooseRevertItemsAsync(subject, target.Name, items);
        if (chosen.Count == 0)
        {
            return;
        }

        var backup = Path.Combine(_sandboxes.WorkspacePath, "backups", "revert", $"{weapon.Id}-{DateTime.Now:yyyyMMdd-HHmmss}");
        IReadOnlyList<string> done;
        try
        {
            done = await Task.Run(() => AddonRevert.Apply(profile, _sandboxes, weapon.Id, chosen, backup, connection));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Log($"Revert of {subject} stopped part way: {error.Message}. Moved files are in {backup}.");
            await Dialogs.ShowAsync(this, "Revert to addon default", $"The revert stopped part way: {error.Message}\n\nFiles it moved are in {backup}.");
            return;
        }

        SaveProfile();
        if (_events?.Weapon == weapon)
        {
            _events = null; // Its edits may have changed on disk; the next open reads them again.
        }

        Log($"Reverted {subject} to {target.Name}'s version: {string.Join("; ", done)}." + (Directory.Exists(backup) ? $" Backup: {backup}." : ""));
        RebuildTree();
        await RefreshStatusAsync();
        RefreshCurrentTool();
    }

    /// <summary>Lists what a revert would undo, each ticked when it can be; returns the ticked ones (none when cancelled).</summary>
    private async Task<List<RevertItem>> ChooseRevertItemsAsync(string subject, string addon, IReadOnlyList<RevertItem> items)
    {
        var boxes = items.Select(item => (Item: item, Box: new CheckBox
        {
            Content = Plain($"{item.Label}: {item.Detail}"),
            IsChecked = item.CanRevert,
            IsEnabled = item.CanRevert,
        })).ToList();
        var confirmed = false;
        var dialog = new Window
        {
            Title = $"Revert {subject} to addon default",
            Icon = Icon,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var revert = new Button { Content = "Revert", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, IsEnabled = items.Any(item => item.CanRevert) };
        revert.Classes.Add("danger");
        var cancel = new Button { Content = "Cancel", IsCancel = true, IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        revert.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        var list = new StackPanel { Spacing = 6 };
        foreach (var (_, box) in boxes)
        {
            list.Children.Add(box);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 10,
            Children =
            {
                Plain($"Throw away the work on {subject} that {addon} does not have yet, so it matches the addon again. The addon and the .blend are not changed: a later Export All from Blender brings your keys back. Sandbox files move to workspace\\backups\\revert."),
                new ScrollViewer { MaxHeight = 360, Content = list },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, revert } },
            },
        };
        await dialog.ShowDialog(this);
        return confirmed ? boxes.Where(pair => pair.Box.IsChecked == true && pair.Item.CanRevert).Select(pair => pair.Item).ToList() : [];
    }
}
