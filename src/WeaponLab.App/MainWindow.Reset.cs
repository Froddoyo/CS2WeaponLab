using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Export;
using WeaponLab.Core.Profiles;

namespace WeaponLab.App;

/// <summary>
/// Set a weapon to CS2 default (right-click in the outliner or the Deliver
/// tree). The next apply deletes every file the addon has for it, whoever
/// made it, and points the addon's other graphs back at CS2's for it. VData
/// is not touched: the VData tab resets its own blocks.
/// </summary>
public sealed partial class MainWindow
{
    private async void OnResetWeaponMenuClicked(object? sender, RoutedEventArgs e)
    {
        var weaponId = (sender as MenuItem)?.DataContext switch
        {
            ExplorerNode node => node.Weapon?.Id,
            DeliveryTreeNode row => row.WeaponId,
            _ => null,
        };
        if (weaponId is not null)
        {
            await ResetWeaponAsync(weaponId);
        }
    }

    private async Task ResetWeaponAsync(string weaponId)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        var target = Target();
        var files = target is null ? null : await Task.Run(() => StockReset.For(connection, target, weaponId));
        var listed = files is null ? [] : files.Files.Concat(files.Skeletons).ToList();
        var removal = listed.Count == 0
            ? "The addon has no clips or graphs of its own for it."
            : $"The next apply deletes these {listed.Count} file(s) from the addon, whoever made them, and points the addon's other graphs back at CS2's for it:\n"
              + string.Join("\n", listed.Take(12).Select(key => "  " + key)) + (listed.Count > 12 ? $"\n  … and {listed.Count - 12} more" : "")
              + (files!.Skeletons.Count > 0 ? "\n(A skeleton is kept if a model still uses it.)" : "");
        var settings = new CheckBox { Content = Plain("Settings: commit on, no clips left out, CS2's skeleton, default Build .blend choices"), IsChecked = true };
        var graphs = new CheckBox { Content = Plain("Graph edits on this weapon's own graphs"), IsChecked = true };
        var sandbox = new CheckBox
        {
            Content = Plain("Sandbox work: exported clips and the .blend (moved to workspace\\backups, not deleted)"),
            IsChecked = false,
            IsEnabled = _sandboxes.Exists(weaponId),
        };
        var confirmed = false;
        var dialog = new Window
        {
            Title = $"Set {weaponId} to CS2 default",
            Icon = Icon,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var reset = new Button { Content = "Set to CS2 default", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        reset.Classes.Add("danger");
        var cancel = new Button { Content = "Cancel", IsCancel = true, IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        reset.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 10,
            Children =
            {
                Plain($"Make {weaponId} play CS2's own animations in {_profile.TargetAddon ?? "this addon"}. Everything is backed up first, and Undo last push restores it. VData is not changed: reset it in the VData tab."),
                Plain(removal),
                settings,
                graphs,
                sandbox,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, reset } },
            },
        };
        await dialog.ShowDialog(this);
        if (!confirmed)
        {
            return;
        }

        var done = new List<string>();
        try
        {
            if (sandbox.IsChecked == true && _sandboxes.Exists(weaponId))
            {
                var backup = Path.Combine(_sandboxes.WorkspacePath, "backups", "sandboxes", $"{weaponId}-{DateTime.Now:yyyyMMdd-HHmmss}");
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(_sandboxes.FolderFor(weaponId), backup);
                done.Add($"sandbox moved to {backup}");
            }
            else if (settings.IsChecked == true && _sandboxes.Exists(weaponId)
                && WeaponLab.Core.Skeletons.CustomSkeletons.Load(_sandboxes.Open(weaponId)) is not null)
            {
                var open = _sandboxes.Open(weaponId);
                await Task.Run(() => WeaponLab.Core.Skeletons.CustomSkeletons.Remove(open, connection));
                done.Add("custom skeleton taken out of the sandbox (rebuild the .blend)");
            }

            if (settings.IsChecked == true && _profile.Weapons.Remove(weaponId))
            {
                done.Add("settings back to default");
            }

            if (graphs.IsChecked == true)
            {
                var own = _profile.Graphs.Keys
                    .Where(graph => connection.Graphs.TryGetValue(graph, out var info) && DeliveryPlanner.GraphOwners(connection, info).Contains(weaponId))
                    .ToList();
                foreach (var graph in own)
                {
                    _profile.Graphs.Remove(graph);
                }

                if (own.Count > 0)
                {
                    done.Add($"edits on {own.Count} graph(s) removed");
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await Dialogs.ShowAsync(this, "Reset weapon", $"The reset stopped part way: {error.Message}");
        }

        _profile.EnsureWeapon(weaponId).ResetToStock = true;
        if (listed.Count > 0)
        {
            done.Add($"{listed.Count} addon file(s) deleted on the next apply");
        }

        SaveProfile();
        Log($"{weaponId} set to CS2 default{(done.Count == 0 ? "" : ": " + string.Join("; ", done))}. Apply the profile in the Deliver tab to update the addon.");
        RebuildTree();
        await RefreshStatusAsync();
        RefreshCurrentTool();
    }

    /// <summary>
    /// Set one animation to CS2 default (right-click a clip, 0.22): its sandbox work is set aside and the
    /// next push takes the addon's copy out (or, on a custom skeleton, pushes CS2's clip re-targeted to it).
    /// </summary>
    private async void OnResetClipMenuClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not ExplorerNode { Weapon: { } weapon, ClipPath: { } path })
        {
            return;
        }

        var clipId = ExplorerNode.ClipName(path);
        var settings = _profile.Weapon(weapon.Id);
        var sandbox = _sandboxes.Exists(weapon.Id) ? _sandboxes.Open(weapon.Id) : null;
        var hasWork = sandbox is not null && (ClipExporter.LastExport(sandbox, clipId) is not null
            || File.Exists(EventEdits.PathFor(sandbox, clipId)) || File.Exists(PushedClips.SamplePath(sandbox, clipId)));
        var message = $"{clipId} plays CS2's animation again.\n\n"
            + (hasWork ? "Its export, Blender sample and event edits move to workspace\\backups\\stock-clips (not deleted).\n" : "")
            + (settings.CustomSkeleton is not null
                ? "The next push puts CS2's clip, re-targeted to the custom skeleton, in the addon, so the gun keeps animating.\n"
                : "The next push removes the addon's copy and points its graphs back at CS2's clip (backed up first; Undo last push restores it).\n")
            + "Your .blend keeps its keys: an Export All from Blender brings them back.";
        if (!await Dialogs.ConfirmAsync(this, $"Set {clipId} to CS2 default", message, "Set to CS2 default", destructive: true))
        {
            return;
        }

        try
        {
            if (sandbox is not null && hasWork)
            {
                var backup = Path.Combine(_sandboxes.WorkspacePath, "backups", "stock-clips", $"{weapon.Id}-{clipId}-{DateTime.Now:yyyyMMdd-HHmmss}");
                PushedClips.SetAside(sandbox, clipId, backup);
                WeaponLab.Core.Blender.ClipLengths.Write(sandbox);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await Dialogs.ShowAsync(this, "Set to CS2 default", $"Could not set {clipId}'s work aside: {error.Message}");
            return;
        }

        _profile.EnsureWeapon(weapon.Id).StockClips.Add(clipId);
        SaveProfile();
        if (_events?.Weapon == weapon)
        {
            _events = null;
        }

        Log($"{weapon.Id}/{clipId} set to CS2 default; push in the Deliver tab to update the addon.");
        RebuildTree();
        await RefreshStatusAsync();
        RefreshCurrentTool();
    }

    /// <summary>New work on a weapon (an export, a Build .blend) replaces a pending Set to CS2 default.</summary>
    private void ClearStockReset(string weaponId)
    {
        if (_profile.Weapons.TryGetValue(weaponId, out var settings) && settings.ResetToStock)
        {
            settings.ResetToStock = false;
            SaveProfile();
            Log($"{weaponId}: new work, so it is no longer set to CS2 default.");
        }
    }

    // A TextBlock as content: a plain string would treat "_" as an access key and hide it.
    private static TextBlock Plain(string text) => new() { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 480 };

    /// <summary>
    /// Set the skeleton to CS2 default (right-click the Skeleton row, 0.24): the custom skeleton leaves the
    /// sandbox, its clips and exports move back onto CS2's skeleton (motion of the extra bones is dropped).
    /// The skeleton folder and exports are copied to workspace\backups\stock-skeleton first.
    /// </summary>
    private async void OnResetSkeletonMenuClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not ExplorerNode { Weapon: { } weapon } || _connection is not { } connection)
        {
            return;
        }

        var sandbox = _sandboxes.Exists(weapon.Id) ? _sandboxes.Open(weapon.Id) : null;
        var setup = sandbox is null ? null : WeaponLab.Core.Skeletons.CustomSkeletons.Load(sandbox);
        if (sandbox is null || setup is null)
        {
            await Dialogs.ShowAsync(this, "Set skeleton to CS2 default", $"{weapon.Id} already uses CS2's skeleton.");
            return;
        }

        var extra = setup.ExtraBones.Count == 0 ? "" : $" Motion on the {setup.ExtraBones.Count} bone(s) CS2's skeleton lacks ({string.Join(", ", setup.ExtraBones.Take(6))}) is dropped.";
        var message = $"{weapon.Id} goes back to CS2's skeleton ({Path.GetFileName(setup.FromSkeleton)}). Its clips and your exports move back onto it.{extra}\n\n"
            + "The skeleton folder and the exports are copied to workspace\\backups\\stock-skeleton first. Build the .blend again afterwards. "
            + "The next push sends the clips on CS2's skeleton; your model in ModelDoc still names the custom skeleton until you change it there.";
        if (!await Dialogs.ConfirmAsync(this, "Set skeleton to CS2 default", message, "Set to CS2 default", destructive: true))
        {
            return;
        }

        try
        {
            var backup = Path.Combine(_sandboxes.WorkspacePath, "backups", "stock-skeleton", $"{weapon.Id}-{DateTime.Now:yyyyMMdd-HHmmss}");
            foreach (var folder in new[] { WeaponLab.Core.Skeletons.CustomSkeletons.Folder, ClipExporter.ExportsFolder })
            {
                CopyFolder(Path.Combine(sandbox.FolderPath, folder), Path.Combine(backup, folder));
            }

            await Task.Run(() => WeaponLab.Core.Skeletons.CustomSkeletons.Remove(sandbox, connection));
            var settings = _profile.EnsureWeapon(weapon.Id);
            settings.CustomSkeleton = null;
            if (settings.PreviewModel is not null && settings.PreviewModel == ViewportModels.ModelOf(setup))
            {
                settings.PreviewModel = null; // The skeleton's model goes with it; the viewport shows CS2's again.
            }

            SaveProfile();
            Log($"{weapon.Id} is back on CS2's skeleton; work backed up to {backup}. Build the .blend again, then push.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await Dialogs.ShowAsync(this, "Set skeleton to CS2 default", $"Could not set {weapon.Id}'s skeleton back: {error.Message}");
            return;
        }

        InvalidateViewport();
        RefreshSkeletonPanel();
        RebuildTree();
        await RefreshStatusAsync();
        RefreshCurrentTool();
    }

    /// <summary>
    /// Set a graph (or all the weapon's graphs, on the Graphs row) to CS2 default (0.24): the profile's pending edits
    /// on it go, and the weapon's own graphs are rebuilt from CS2's on the next push (<see cref="Profile.StockGraphs"/>),
    /// dropping edits the addon's version has while keeping this profile's clips routed. Shared graphs (other weapons
    /// play through them) only lose this profile's pending edits.
    /// </summary>
    private async void OnResetGraphMenuClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not ExplorerNode { Weapon: { } weapon } node || _connection is not { } connection)
        {
            return;
        }

        var manifest = Target() is { } addon ? TryReadManifest(addon) : null;
        var graphs = WeaponGraphs.For(connection, _profile, Target(), manifest, weapon.Id, weaponPending: false)
            .Where(graph => node.GraphPath is null || graph.Graph == node.GraphPath)
            .Where(graph => graph.Link != GraphLink.Route || _profile.Graphs.ContainsKey(graph.Graph))
            .ToList();
        if (graphs.Count == 0)
        {
            await Dialogs.ShowAsync(this, "Set to CS2 default", "Nothing to set back: these graphs have no edits in this profile.");
            return;
        }

        var own = graphs.Where(graph => graph.Link == GraphLink.Own).ToList();
        var shared = graphs.Where(graph => graph.Link != GraphLink.Own).ToList();
        var message = (own.Count == 0 ? "" : $"Rebuilt from CS2's graph on the next push (edits in the addon's version are dropped; this profile's clips stay routed):\n  {string.Join("\n  ", own.Select(graph => graph.Name))}\n\n")
            + (shared.Count == 0 ? "" : $"Shared with other weapons, so only this profile's pending edits go (the addon's version is kept):\n  {string.Join("\n  ", shared.Select(graph => graph.Name))}\n\n")
            + "Nothing is written to the addon until you push.";
        if (!await Dialogs.ConfirmAsync(this, node.GraphPath is null ? $"Set {weapon.Id}'s graphs to CS2 default" : $"Set {Path.GetFileNameWithoutExtension(node.GraphPath)} to CS2 default", message, "Set to CS2 default", destructive: true))
        {
            return;
        }

        foreach (var graph in graphs)
        {
            _profile.Graphs.Remove(graph.Graph);
            if (graph.Link == GraphLink.Own)
            {
                _profile.StockGraphs.Add(graph.Graph);
            }
        }

        SaveProfile();
        Log($"{weapon.Id}: {graphs.Count} graph(s) set to CS2 default; push in the Deliver tab to update the addon.");
        RebuildTree();
        await RefreshStatusAsync();
        RefreshCurrentTool();
    }

    private static void CopyFolder(string from, string to)
    {
        if (!Directory.Exists(from))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
    }
}
