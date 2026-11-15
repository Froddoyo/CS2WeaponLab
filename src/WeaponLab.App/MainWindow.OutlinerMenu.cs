using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Checks;

namespace WeaponLab.App;

/// <summary>
/// The outliner's right-click menu (0.21): open a row in another tab, show
/// its files, copy its name, and the Show buttons of delivery errors and
/// checks, which select a weapon, clip or graph node the same way.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>A graph node the Graph tab selects once that graph has loaded.</summary>
    private (string Graph, int Node)? _requestedGraphNode;

    private static ExplorerNode? MenuNode(object? sender) => (sender as MenuItem)?.DataContext as ExplorerNode;

    private async void OnOpenInAnimEventsMenuClicked(object? sender, RoutedEventArgs e) => await OpenNodeInToolAsync(MenuNode(sender), SoundsTool);

    private async void OnOpenInAnimationsMenuClicked(object? sender, RoutedEventArgs e) => await OpenNodeInToolAsync(MenuNode(sender), AnimationsTool);

    private async void OnOpenInVDataMenuClicked(object? sender, RoutedEventArgs e) => await OpenNodeInToolAsync(MenuNode(sender), VDataTool);

    private async void OnOpenInDeliverMenuClicked(object? sender, RoutedEventArgs e) => await OpenNodeInToolAsync(MenuNode(sender), DeliverTool);

    private async void OnShowSkeletonMenuClicked(object? sender, RoutedEventArgs e) => await OpenNodeInToolAsync(MenuNode(sender), AnimationsTool);

    private async void OnOpenInGraphsMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender) is not { Weapon: { } weapon } node)
        {
            return;
        }

        if (node.GraphPath is { } graph)
        {
            SelectWeapon(weapon);
            OpenGraphInEditor(graph);
            return;
        }

        await OpenNodeInToolAsync(node, GraphTool);
    }

    /// <summary>Switches to a tab, then selects the row so the tab opens it.</summary>
    private async Task OpenNodeInToolAsync(ExplorerNode? node, int tool)
    {
        if (node is null)
        {
            return;
        }

        ToolTabs.SelectedIndex = tool;
        await SelectInOutlinerAsync(node);
    }

    private void OnOpenBlendMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (BlendOf(MenuNode(sender)) is not { } sandbox)
        {
            return;
        }

        if (_blender is not { } blender)
        {
            Log("Blender was not found; pick it in the Animations tab first.");
            return;
        }

        WriteGameTiming(sandbox);
        BlendBuilder.Open(sandbox, blender);
        Log($"Opened {BlendBuilder.BlendPath(sandbox)} in Blender.");
    }

    private void OnShowBlendMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (BlendOf(MenuNode(sender)) is { } sandbox)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{BlendBuilder.BlendPath(sandbox)}\"") { UseShellExecute = false });
        }
    }

    /// <summary>The weapon row's sandbox when it has a .blend; logs why not otherwise.</summary>
    private WeaponLab.Core.Sandbox.SandboxInfo? BlendOf(ExplorerNode? node)
    {
        if (node?.Weapon is not { } weapon)
        {
            return null;
        }

        if (!_sandboxes.Exists(weapon.Id) || _sandboxes.Open(weapon.Id) is not { } sandbox || !BlendBuilder.HasBlend(sandbox))
        {
            Log($"{weapon.Id} has no .blend yet. Build one in the Animations tab.");
            return null;
        }

        return sandbox;
    }

    private void OnShowSandboxMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender)?.Weapon is { } weapon)
        {
            if (_sandboxes.Exists(weapon.Id))
            {
                Process.Start(new ProcessStartInfo(_sandboxes.FolderFor(weapon.Id)) { UseShellExecute = true });
            }
            else
            {
                Log($"{weapon.Id} has no sandbox yet; it is made when you build its .blend or edit its events.");
            }
        }
    }

    private async void OnCopyNameMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender) is not { } node || Clipboard is not { } clipboard)
        {
            return;
        }

        var name = node.Kind switch
        {
            ExplorerNodeKind.Clip when node.ClipPath is { } path => ExplorerNode.ClipName(path),
            ExplorerNodeKind.Bone when node.BoneName is { } bone => bone,
            ExplorerNodeKind.Graph when node.GraphPath is { } graph => graph,
            _ => node.Label,
        };
        await clipboard.SetTextAsync(name);
        SetStatus($"Copied {name}");
    }

    /// <summary>Copies a skeleton's or clip's full resource path without <c>_c</c> (0.23), e.g. for ModelDoc's NmSkeletonReference Source File.</summary>
    private async void OnCopyAssetPathMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender) is not { Weapon: { } weapon } node || Clipboard is not { } clipboard)
        {
            return;
        }

        var path = node.Kind switch
        {
            ExplorerNodeKind.Skeleton => SkeletonAssetPath(weapon),
            ExplorerNodeKind.Clip when node.ClipPath is { } clip => ClipAssetPath(clip),
            _ => null,
        };
        if (path is null)
        {
            SetStatus("This row has no asset path to copy.");
            return;
        }

        await clipboard.SetTextAsync(path);
        SetStatus($"Copied {path}");
    }

    /// <summary>Copy asset path on a Deliver tree row: a pushed or removed file, or the skeleton a warning names.</summary>
    private async void OnCopyDeliveryAssetPathClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is DeliveryTreeNode { AssetPath: { } path } && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(path);
            SetStatus($"Copied {path}");
        }
    }

    /// <summary>The skeleton the weapon's clips drive (its custom one, else the slot's own), without <c>_c</c>.</summary>
    private string? SkeletonAssetPath(WeaponLab.Core.Weapons.WeaponEntry weapon)
    {
        if (_connection is not { } connection)
        {
            return null;
        }

        try
        {
            var (skeleton, _) = SkeletonOf(connection, weapon);
            return skeleton is null ? null : WithoutCompiledSuffix(skeleton.ResourcePath);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The clip the addon plays for a CS2 clip (its own, or the one Weapon Lab pushed), else CS2's, without <c>_c</c>.</summary>
    private string ClipAssetPath(string clipPath)
    {
        var stock = WithoutCompiledSuffix(clipPath);
        return _inventory.ClipFor(stock) ?? stock;
    }

    private static string WithoutCompiledSuffix(string path) => path.EndsWith("_c", StringComparison.Ordinal) ? path[..^2] : path;

    private void OnCollapseAllMenuClicked(object? sender, RoutedEventArgs e)
    {
        foreach (var category in _tree)
        {
            foreach (var weapon in category.Children)
            {
                weapon.IsExpanded = false;
            }

            category.IsExpanded = false;
        }
    }

    // ---- Selecting a row from elsewhere (menus, Show buttons) ----

    /// <summary>Expands the row's parents and selects it; a row already selected is opened again.</summary>
    private async Task SelectInOutlinerAsync(ExplorerNode node)
    {
        foreach (var category in _tree)
        {
            foreach (var weaponNode in category.Children)
            {
                if (weaponNode == node || weaponNode.Children.Contains(node))
                {
                    category.IsExpanded = true;
                    weaponNode.IsExpanded = weaponNode != node || weaponNode.IsExpanded;
                }
            }
        }

        if (ExplorerTree.SelectedItem == node)
        {
            await OpenSelectedNodeAsync();
        }
        else
        {
            ExplorerTree.SelectedItem = node;
        }
    }

    /// <summary>The outliner row of a weapon, or of one of its clips; clears a filter that hides it.</summary>
    private ExplorerNode? FindOutlinerNode(string weaponId, string? clipPath)
    {
        ExplorerNode? Find()
        {
            var weaponNode = _tree.SelectMany(category => category.Children).FirstOrDefault(node => node.Weapon?.Id == weaponId);
            return clipPath is null ? weaponNode : weaponNode?.Children.FirstOrDefault(node => node.ClipPath == clipPath);
        }

        if (Find() is { } found)
        {
            return found;
        }

        if (!string.IsNullOrEmpty(FilterBox.Text))
        {
            FilterBox.Text = "";
            RebuildTree();
        }

        return Find();
    }

    /// <summary>Shows what an error, warning or check is about: the graph node, the clip in Anim Events, the vdata, or the weapon.</summary>
    private async Task ShowCheckTargetAsync(CheckTarget target)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        if (target.Graph is { } graph)
        {
            if (target.WeaponId is { } owner && connection.FindWeapon(owner) is { } ownerWeapon)
            {
                SelectWeapon(ownerWeapon);
            }

            _requestedGraphNode = target.Node is { } node ? (graph, node) : null;
            OpenGraphInEditor(graph);
            return;
        }

        if (target.WeaponId is null || FindOutlinerNode(target.WeaponId, target.ClipPath) is not { } row)
        {
            return;
        }

        var tool = target.ClipPath is not null ? SoundsTool : target.VData ? VDataTool : ToolTabs.SelectedIndex;
        await OpenNodeInToolAsync(row, tool);
    }

    /// <summary>What a delivery message points at, for its Show button; null when it names nothing the app can open.</summary>
    private CheckTarget? CheckTargetOf(string message, string? subject = null) =>
        _connection is { } connection
            ? CheckTargets.Find(message, connection.Weapons, connection.Graphs.Keys, BlocksFor, subject)
            : null;

    private async void OnShowCheckClicked(object? sender, RoutedEventArgs e)
    {
        var target = (sender as Control)?.DataContext switch
        {
            PlanRow row => row.Target,
            DeliveryTreeNode node => node.Target,
            _ => null,
        };
        if (target is not null)
        {
            await ShowCheckTargetAsync(target);
        }
    }
}
