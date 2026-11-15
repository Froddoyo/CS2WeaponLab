using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using WeaponLab.Core.Addons;
using WeaponLab.Core.Checks;
using WeaponLab.Core.Export;
using WeaponLab.Core.Weapons;

namespace WeaponLab.App;

/// <summary>
/// The per-weapon Checks list (0.24): every known problem with the selected weapon in plain words, each with a
/// Show or Fix button. The same rules feed Deliver's warnings; here they are gathered per weapon, before a push.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How many checks each weapon with a sandbox has, for the outliner's ⚠ count (worked out after a status refresh).</summary>
    private Dictionary<string, int> _checkCounts = new(StringComparer.Ordinal);

    /// <summary>Everything known to be wrong with a weapon; empty when nothing is.</summary>
    private IReadOnlyList<WeaponCheck> ChecksFor(WeaponEntry weapon)
    {
        if (_connection is not { } connection)
        {
            return [];
        }

        var checks = new List<WeaponCheck>();
        var sandbox = _sandboxes.Exists(weapon.Id) ? _sandboxes.Open(weapon.Id) : null;

        // The model the weapon shows in game against the custom skeleton (0.23 model check).
        foreach (var problem in ModelProblems(weapon))
        {
            checks.Add(new WeaponCheck(CheckArea.Model, problem,
                problem.Contains("does not use the skeleton", StringComparison.Ordinal) ? CheckFix.CopySkeletonPath : CheckFix.ShowSkeleton));
        }

        // Custom clips reach the game only through the addon's player models (Workshop route).
        var hasWork = sandbox is not null && weapon.ClipPaths.Any(path => ClipExporter.LastExport(sandbox, ExplorerNode.ClipName(path)) is { IdenticalToTemplate: false });
        // Third-person edits (0.26) need player models whose third-person graphs are worldmodel_custom.
        var hasWorldWork = sandbox is not null && sandbox.Manifest.WorldClips.Any(clip => ClipExporter.LastExport(sandbox, ExplorerNode.ClipName(clip.ResourcePath)) is { IdenticalToTemplate: false });
        hasWork |= hasWorldWork;
        if (hasWork || _profile.Graphs.Keys.Any(graph => graph.Contains(WeaponSlotsSuffix(weapon), StringComparison.OrdinalIgnoreCase)))
        {
            checks.AddRange(AddonChecks.Hookup(_inventory, true, hasWorldWork).Select(warning => new WeaponCheck(CheckArea.Hookup, warning.Message, CheckFix.OpenDeliver)));
        }

        // The weapon's model in vdata (set and present).
        if (Target() is { } target)
        {
            var blocks = BlocksFor(weapon).ToHashSet(StringComparer.Ordinal);
            try
            {
                checks.AddRange(AddonChecks.Models(connection, target, _profile)
                    .Where(warning => blocks.Contains(warning.Subject))
                    .Select(warning => new WeaponCheck(CheckArea.VData, $"{warning.Subject} {warning.Message}", CheckFix.OpenVData)));
            }
            catch (Exception error) when (error is IOException or InvalidDataException)
            {
                // The vdata could not be read: the VData tab says so.
            }
        }

        // The game's timing against the reloads as they will play, and the .blend against the sandbox.
        try
        {
            checks.AddRange(WeaponChecks.Timing(connection, weapon, sandbox, TimingGunOf(weapon),
                path => AddonInventory.ClipFile(Target(), _inventory, path)));
            if (sandbox is not null)
            {
                checks.AddRange(WeaponChecks.Blender(sandbox));
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            checks.Add(new WeaponCheck(CheckArea.Blender, "Some checks could not run: " + error.Message));
        }

        return checks;
    }

    /// <summary>rifle_ssg08 → ssg08: graph files of a weapon end with it (…+ssg08_legacy.vnmgraph).</summary>
    private static string WeaponSlotsSuffix(WeaponEntry weapon) => "+" + weapon.Id[(weapon.Id.IndexOf('_') + 1)..];

    /// <summary>Counts the checks of every weapon with a sandbox (the outliner shows them); a failure only costs the counts.</summary>
    private void RefreshCheckCounts()
    {
        if (_connection is not { } connection)
        {
            return;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var sandbox in _sandboxes.List())
        {
            if (connection.FindWeapon(sandbox.Manifest.WeaponId) is { } weapon)
            {
                try
                {
                    counts[weapon.Id] = ChecksFor(weapon).Count;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    Log($"{weapon.Id}: checks could not run: {error.Message}");
                }
            }
        }

        _checkCounts = counts;
        RefreshChecksButton();
    }

    private void RefreshChecksButton()
    {
        var count = _weapon is { } weapon ? _checkCounts.GetValueOrDefault(weapon.Id) : 0;
        ChecksButton.IsEnabled = _weapon is not null && _connection is not null;
        ChecksButton.Content = count > 0 ? $"⚠ Checks ({count})…" : "Checks…";
    }

    private async void OnChecksClicked(object? sender, RoutedEventArgs e)
    {
        if (_weapon is { } weapon)
        {
            await ShowChecksAsync(weapon);
        }
    }

    private async void OnChecksMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender) is { Weapon: { } weapon })
        {
            SelectWeapon(weapon);
            await ShowChecksAsync(weapon);
        }
    }

    /// <summary>The Checks window: each check with its area, its words, and Show or Fix where the app can act on it.</summary>
    private async Task ShowChecksAsync(WeaponEntry weapon)
    {
        var checks = ChecksFor(weapon);
        _checkCounts[weapon.Id] = checks.Count;
        RefreshChecksButton();
        ApplyStatusToTree();

        var dialog = new Window
        {
            Title = $"Checks: {weapon.Id}",
            Width = 640,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 720,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var list = new StackPanel { Spacing = 10 };
        if (checks.Count == 0)
        {
            list.Children.Add(new TextBlock { Text = $"Nothing known to be wrong with {weapon.Id}. Checks cover the model against the skeleton, the player-model hookup, the vdata model, "
                + "the game's reload timing and the .blend; not tested in game is still not tested.", TextWrapping = TextWrapping.Wrap });
        }

        foreach (var check in checks)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,Auto"), ColumnSpacing = 8 };
            var area = new TextBlock { Text = "⚠ " + AreaName(check.Area), Foreground = new SolidColorBrush(Color.Parse("#E0B050")), FontSize = 12 };
            var text = new SelectableTextBlock { Text = check.Message, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            Grid.SetColumn(text, 1);
            row.Children.Add(area);
            row.Children.Add(text);
            if (FixLabel(check) is { } label)
            {
                var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(8, 2) };
                button.Click += async (_, _) =>
                {
                    dialog.Close();
                    await ApplyCheckFixAsync(weapon, check);
                };
                Grid.SetColumn(button, 2);
                row.Children.Add(button);
            }

            list.Children.Add(row);
        }

        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(18),
            Children =
            {
                new Border { [DockPanel.DockProperty] = Dock.Bottom, Margin = new Thickness(0, 14, 0, 0), Child = close },
                new ScrollViewer { Content = list },
            },
        };
        await dialog.ShowDialog(this);
    }

    private static string AreaName(CheckArea area) => area switch
    {
        CheckArea.Model => "Model",
        CheckArea.Hookup => "Hookup",
        CheckArea.VData => "VData",
        CheckArea.Timing => "Timing",
        _ => "Blender",
    };

    private static string? FixLabel(WeaponCheck check) => check.Fix switch
    {
        CheckFix.BuildBlend => "Build .blend…",
        CheckFix.UpdateBlend => "Update for export",
        CheckFix.CopySkeletonPath => "Copy skeleton path",
        CheckFix.OpenClip => "Show clip",
        CheckFix.OpenVData => "Show in VData",
        CheckFix.OpenDeliver => "Show in Deliver",
        CheckFix.ShowSkeleton => "Show skeleton",
        _ => null,
    };

    private async Task ApplyCheckFixAsync(WeaponEntry weapon, WeaponCheck check)
    {
        switch (check.Fix)
        {
            case CheckFix.BuildBlend or CheckFix.UpdateBlend:
                SelectWeapon(weapon);
                ToolTabs.SelectedIndex = AnimationsTool;
                OnBuildBlendClicked(null, new RoutedEventArgs());
                break;
            case CheckFix.CopySkeletonPath when SkeletonAssetPath(weapon) is { } path && Clipboard is { } clipboard:
                await clipboard.SetTextAsync(path);
                SetStatus($"Copied {path}");
                break;
            case CheckFix.OpenClip when FindOutlinerNode(weapon.Id, check.ClipPath) is { } clipRow:
                await OpenNodeInToolAsync(clipRow, SoundsTool);
                break;
            case CheckFix.OpenVData when FindOutlinerNode(weapon.Id, null) is { } vdataRow:
                await OpenNodeInToolAsync(vdataRow, VDataTool);
                break;
            case CheckFix.OpenDeliver when FindOutlinerNode(weapon.Id, null) is { } deliverRow:
                await OpenNodeInToolAsync(deliverRow, DeliverTool);
                break;
            case CheckFix.ShowSkeleton when FindOutlinerNode(weapon.Id, null) is { } skeletonRow:
                await OpenNodeInToolAsync(skeletonRow, AnimationsTool);
                break;
        }
    }
}
