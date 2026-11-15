using Avalonia.Controls;
using Avalonia.Threading;
using WeaponLab.Core.Addons;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Export;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.Weapons;

namespace WeaponLab.App;

/// <summary>The outliner: weapon tree, selection, profile status colours and Blender exports arriving.</summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<string, IReadOnlyList<string>> _weaponBlocks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingExports = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _exportWatcher;
    private ProfileStatus? _status;
    private AddonInventory _inventory = AddonInventory.Empty;
    private List<ExplorerNode> _tree = [];

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        RebuildTree();
    }

    private void RebuildTree()
    {
        if (_connection is null)
        {
            return;
        }

        _tree = ExplorerNode.BuildTree(_connection.Weapons, FilterBox.Text?.Trim() ?? "", (weapon, clipId) => IsEdited(weapon, clipId));
        WatchSkeletonNodes(_tree);
        ApplyStatusToTree();
        ExplorerTree.ItemsSource = _tree;
    }

    private async void OnExplorerSelected(object? sender, SelectionChangedEventArgs e) => await OpenSelectedNodeAsync();

    /// <summary>Opens what the outliner's selected row stands for in the current tab.</summary>
    private async Task OpenSelectedNodeAsync()
    {
        if (ExplorerTree.SelectedItem is not ExplorerNode { Weapon: { } weapon } node)
        {
            return;
        }

        var weaponChanged = _weapon != weapon;
        SelectWeapon(weapon);
        if (node.Kind == ExplorerNodeKind.Graph && node.GraphPath is { } graphPath)
        {
            OpenGraphInEditor(graphPath);
            return;
        }

        if (node.Kind == ExplorerNodeKind.ThirdPerson)
        {
            node.IsExpanded = true;
            ClearClip();
            CenterTitle.Text = $"{weapon.Id} / third person";
            ShowThirdPersonProperties(weapon);
            if (weaponChanged)
            {
                RefreshCurrentTool();
            }

            return;
        }

        if (node.IsWorldClip && ToolTabs.SelectedIndex == SoundsTool)
        {
            // Third-person clips have no sounds or gameplay events to edit (0.26): show them in Animations.
            ToolTabs.SelectedIndex = AnimationsTool;
        }

        if (node.Kind == ExplorerNodeKind.Graphs)
        {
            node.IsExpanded = true;
            if (weaponChanged)
            {
                RefreshCurrentTool();
            }

            return;
        }

        if (node.Kind is ExplorerNodeKind.Skeleton or ExplorerNodeKind.Bone)
        {
            if (node.Kind == ExplorerNodeKind.Skeleton)
            {
                FillSkeletonNode(node);
                node.IsExpanded = true;
            }
            else if (node.BoneName is { } bone)
            {
                ShowBoneProperties(weapon, bone);
                WeaponView.SelectedBone = bone;
            }

            if (weaponChanged)
            {
                RefreshCurrentTool();
            }

            return;
        }

        if (node.Kind == ExplorerNodeKind.Clip && node.ClipPath is { } path)
        {
            // A clip opens in the Animations tab, unless Anim Events is open: it edits that clip's events.
            if (ToolTabs.SelectedIndex is not (AnimationsTool or SoundsTool) && !weaponChanged)
            {
                ToolTabs.SelectedIndex = AnimationsTool;
            }

            await LoadClipAsync(weapon, path);
            if (ToolTabs.SelectedIndex == SoundsTool && !weaponChanged)
            {
                await OpenEventsClipAsync(weapon, path);
            }
        }
        else
        {
            ClearClip();
            CenterTitle.Text = $"{weapon.Id} — select a clip";
            ShowWeaponProperties(weapon);
            if (ToolTabs.SelectedIndex == SoundsTool && !weaponChanged)
            {
                ShowEventsOverview(weapon);
            }
        }

        if (weaponChanged)
        {
            RefreshCurrentTool();
        }
    }

    /// <summary>Makes <paramref name="weapon"/> the subject of every tab and the weapon panel.</summary>
    private void SelectWeapon(WeaponEntry weapon)
    {
        _weapon = weapon;
        VerifyWeaponButton.IsEnabled = true;
        RefreshWeaponActions();
        RefreshExports();
        RefreshWeaponPanel();
        if (ToolTabs.SelectedIndex == AnimationsTool)
        {
            RefreshSkeletonPanel();
        }

        if (ViewportShown)
        {
            RefreshViewport();
        }
    }

    /// <param name="eventsOnly">Only an export whose events changed counts (the Anim Events tab).</param>
    private bool IsEdited(WeaponEntry weapon, string clipId, bool eventsOnly = false)
    {
        if (!_sandboxes.Exists(weapon.Id))
        {
            return false;
        }

        try
        {
            return ClipExporter.LastExport(_sandboxes.Open(weapon.Id), clipId) is { IdenticalToTemplate: false } report
                && (!eventsOnly || report.ChangedEvents is { Count: > 0 });
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    // ---- Profile status colours ----

    /// <summary>
    /// Recomputes which weapons are yellow (profile edits the addon lacks) or
    /// green (the addon matches), then recolours the tree and weapon panel.
    /// </summary>
    private async Task RefreshStatusAsync()
    {
        if (_connection is not { } connection)
        {
            return;
        }

        var target = Target();
        var profile = _profile;
        try
        {
            var (status, inventory) = await Task.Run(() =>
            {
                foreach (var weapon in connection.Weapons)
                {
                    BlocksFor(weapon);
                }

                var found = target is null ? AddonInventory.Empty : AddonInventory.Load(connection, target, AddonManifest.Read(target));
                KeepPushedClips(profile, target);
                return (ProfileStatus.Load(profile, _sandboxes, connection, target), found);
            });
            if (profile != _profile)
            {
                return; // The profile changed while computing.
            }

            _status = status;
            _inventory = inventory;
            _vdataChoices = null; // The addon's models feed the VData selectors.
            RefreshCheckCounts(); // The outliner's ⚠ counts (0.24).
            RefreshViewportClipSource(); // The viewport plays the addon's own clips; they may have just become known.
            ApplyStatusToTree();
            RefreshWeaponPanel();
            RefreshPendingText();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log($"Could not check the addon against the profile: {error.Message}");
        }
    }

    /// <summary>Copies the sandbox work behind clips the addon holds, for Revert to addon default; a failure only costs that.</summary>
    private void KeepPushedClips(Profile profile, AddonTarget? target)
    {
        if (target is null)
        {
            return;
        }

        try
        {
            PendingWork.KeepLive(profile, _sandboxes, target);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            Dispatcher.UIThread.Post(() => Log($"Could not keep a copy of the pushed clips (Revert to addon default needs it): {error.Message}"));
        }
    }

    private void ApplyStatusToTree()
    {
        if (_status is null)
        {
            return;
        }

        var aspect = CurrentAspect();
        // Anim Events colours the clips whose events changed (0.21; they stayed white before).
        var eventsOnly = aspect == StatusAspect.Sounds;
        var clipAspect = aspect is StatusAspect.Animations or StatusAspect.All or StatusAspect.Sounds;
        var manifest = Target() is { } addon ? TryReadManifest(addon) : null;
        var withBlend = aspect == StatusAspect.Animations
            ? _sandboxes.List().Where(WeaponLab.Core.Blender.BlendBuilder.HasBlend).Select(sandbox => sandbox.Manifest.WeaponId).ToHashSet(StringComparer.Ordinal)
            : [];
        // Weapons with work not pushed yet: shared graphs they will change show teal on the other weapons (0.24.1).
        var pendingWeapons = _tree.SelectMany(category => category.Children).Where(node => node.Weapon is not null)
            .Where(node => AspectStatus(node.Weapon!, StatusAspect.Animations).State == WeaponState.NeedsApply)
            .Select(node => node.Weapon!.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var category in _tree)
        {
            var worst = WeaponState.Untouched;
            foreach (var weaponNode in category.Children)
            {
                weaponNode.HasBlend = withBlend.Contains(weaponNode.Weapon!.Id);
                weaponNode.Detail = (_checkCounts.GetValueOrDefault(weaponNode.Weapon!.Id) is > 0 and var checkCount ? $"⚠{checkCount} · " : "")
                    + weaponNode.Weapon!.ClipPaths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var (state, reasons) = AspectStatus(weaponNode.Weapon!, aspect);
                var own = AddonOwnSummary(weaponNode.Weapon!.Id, aspect);
                weaponNode.State = state;
                weaponNode.StatusTip = state switch
                {
                    WeaponState.NeedsApply => $"Not in the addon yet ({AspectName(aspect)}):\n" + string.Join("\n", reasons.Take(6)),
                    WeaponState.Applied => $"In the addon ({AspectName(aspect)}); nothing pending." + (own.Length > 0 ? "\n" + own : ""),
                    _ => "",
                };
                worst = state == WeaponState.NeedsApply || worst == WeaponState.NeedsApply ? WeaponState.NeedsApply
                    : state == WeaponState.Applied ? WeaponState.Applied : worst;

                // The weapon's graphs: yellow until its change is in the addon, green when it is, blue when the addon's
                // version was changed for another weapon (or is the addon's own).
                foreach (var graphsNode in weaponNode.Children.Where(node => node.Kind == ExplorerNodeKind.Graphs))
                {
                    FillGraphsNode(graphsNode, weaponNode.Weapon!, aspect is StatusAspect.All or StatusAspect.Animations or StatusAspect.Graph, manifest, pendingWeapons);
                }

                // A custom skeleton: yellow until the addon has it, green once it does (or when it is the addon's own).
                foreach (var skeletonNode in weaponNode.Children.Where(node => node.Kind == ExplorerNodeKind.Skeleton))
                {
                    var (skeletonState, skeletonTip) = SkeletonState(weaponNode.Weapon!, clipAspect && !eventsOnly ? reasons : []);
                    var modelProblems = ModelProblems(weaponNode.Weapon!);
                    skeletonNode.State = skeletonState;
                    skeletonNode.StatusTip = modelProblems.Count == 0 ? skeletonTip
                        : (skeletonTip.Length > 0 ? skeletonTip + "\n\n" : "") + "Model check:\n" + string.Join("\n", modelProblems.Select(problem => "⚠ " + problem));
                    skeletonNode.Detail = modelProblems.Count == 0 ? "" : $"⚠ model check: {modelProblems.Count} problem(s)";
                }

                // The addon's own clips are part of what the profile starts from: green like applied edits.
                // A clip edited in Blender is yellow until the addon has that edit, green once it does; an unedited
                // clip is green when the addon plays its own version of it.
                // Where each of this profile's clips stands in the live addon (pushed, or replaced there since by someone else).
                var target = Target();
                var clipStates = clipAspect && target is not null && _sandboxes.Exists(weaponNode.Weapon!.Id)
                    ? PendingWork.Clips(_profile, _sandboxes.Open(weaponNode.Weapon.Id), target).ToDictionary(clip => clip.ClipId, clip => clip.State, StringComparer.Ordinal)
                    : [];
                var clipNodes = weaponNode.Children
                    .SelectMany(node => node.Kind == ExplorerNodeKind.ThirdPerson ? node.Children.AsEnumerable() : new[] { node })
                    .Where(node => node.ClipPath is not null);
                foreach (var clipNode in clipNodes)
                {
                    var clipId = ExplorerNode.ClipName(clipNode.ClipPath!);
                    var pending = clipAspect ? reasons.FirstOrDefault(reason => reason.StartsWith(clipId + " ", StringComparison.Ordinal)) : null;
                    var replaced = clipAspect && _inventory.ClipOverrides.TryGetValue(clipNode.ClipPath![..^2], out var replacement)
                        ? replacement
                        : null;
                    var reset = _profile.Weapon(weaponNode.Weapon!.Id).ResetToStock;
                    (clipNode.State, clipNode.StatusTip) = reset
                        ? replaced is not null
                            ? (WeaponState.NeedsApply, $"Set to CS2 default: the addon's {replaced} is removed on the next apply.")
                            : (WeaponState.Untouched, "")
                        : clipAspect && !eventsOnly && pending is not null && _profile.Weapon(weaponNode.Weapon!.Id).StockClips.Contains(clipId)
                            ? (WeaponState.NeedsApply, $"Set to CS2 default: {pending}")
                        : clipAspect && IsEdited(weaponNode.Weapon!, clipId, eventsOnly)
                        ? pending is not null
                            ? (WeaponState.NeedsApply, $"Edited, not in the addon yet: {pending}")
                            : clipStates.GetValueOrDefault(clipId) == PushState.ReplacedInAddon
                                ? (WeaponState.ChangedElsewhere, "Edited and pushed, but the addon has another version now (another profile's or a hand edit). Export it again to push yours.")
                                : (WeaponState.Applied, "Edited; the addon has this version.")
                        : replaced is not null
                            ? (WeaponState.Applied, $"The addon plays its own {replaced} here; it is kept.")
                            : (WeaponState.Untouched, "");
                }

                foreach (var thirdPerson in weaponNode.Children.Where(node => node.Kind == ExplorerNodeKind.ThirdPerson))
                {
                    FillThirdPersonNode(thirdPerson, weaponNode.Weapon!);
                }
            }

            category.State = worst;
        }
    }

    /// <summary>
    /// The "Third person" group (0.26): yellow while one of its clips waits for an apply, green when they are in the
    /// addon; its detail says whether the sandbox has the clips yet and whether the .blend does.
    /// </summary>
    private void FillThirdPersonNode(ExplorerNode node, WeaponEntry weapon)
    {
        node.State = node.Children.Any(child => child.State == WeaponState.NeedsApply) ? WeaponState.NeedsApply
            : node.Children.Any(child => child.State == WeaponState.Applied) ? WeaponState.Applied
            : WeaponState.Untouched;
        var sandbox = _sandboxes.Exists(weapon.Id) ? _sandboxes.Open(weapon.Id) : null;
        var inBlend = sandbox is not null && WeaponLab.Core.Blender.RigPackage.ReadRig(sandbox)?.Skeletons.Body is not null;
        (node.Detail, node.StatusTip) = sandbox is null
            ? ($"{node.Children.Count}", "Build .blend makes the weapon's sandbox; its third-person clips come with it.")
            : sandbox.Manifest.WorldClips.Count == 0
                ? ("not in the sandbox", "This sandbox is from before 0.26: right-click → Add third-person clips, then Build .blend with Keep my animation.")
                : !inBlend
                    ? ("not in the .blend", "Build .blend again with Keep my animation to edit them on the body rig.")
                    : ($"{node.Children.Count}", node.State == WeaponState.NeedsApply ? "Third-person edits not in the addon yet." : "");
    }

    /// <summary>The Property Browser for a weapon's "Third person" group (0.26).</summary>
    private void ShowThirdPersonProperties(WeaponEntry weapon)
    {
        _showingBone = false;
        var sandbox = _sandboxes.Exists(weapon.Id) ? _sandboxes.Open(weapon.Id) : null;
        var clips = sandbox?.Manifest.WorldClips ?? [];
        var rig = sandbox is null ? null : WeaponLab.Core.Blender.RigPackage.ReadRig(sandbox);
        var edited = clips.Count(clip => IsEdited(weapon, ExplorerNode.ClipName(clip.ResourcePath)));
        ShowProperties([
            new PropertyGroup("Third person", [
                new PropertyRow("Clips in CS2", weapon.WorldClipPaths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new PropertyRow("Folder", $"{WeaponCatalog.WorldClipRoot}{weapon.Category}/{weapon.Id}/"),
                new PropertyRow("In the sandbox", sandbox is null ? "no sandbox yet" : clips.Count == 0 ? "no (right-click → Add third-person clips)" : $"{clips.Count} ({clips.Count(clip => clip.Editable)} editable)"),
                new PropertyRow("In the .blend", rig?.Skeletons.Body is not null ? "yes, on the Body rig" : "no (Build .blend with Keep my animation)"),
                new PropertyRow("Edited", edited.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ]),
            new PropertyGroup("Notes", [
                new PropertyRow("Read-only", "idle and shoot add onto other poses (additive); how CS2 builds their base is unknown, so they are not built"),
                new PropertyRow("Events", "no sounds or ammo events: those play from first person"),
                new PropertyRow("Skeleton", "CS2's weapon skeleton; a custom skeleton is first person only"),
                new PropertyRow("Hookup", $"player models need {System.IO.Path.GetFileName(GraphTree.WorldCustomParent)} for their \"\" and \"worldmodel\" graphs (untested in game)"),
            ]),
        ]);
    }

    private async void OnAddThirdPersonMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (MenuNode(sender) is not { Weapon: { } weapon } || _connection is not { } connection)
        {
            return;
        }

        if (!_sandboxes.Exists(weapon.Id))
        {
            Log($"{weapon.Id} has no sandbox yet: Build .blend makes one, with its third-person clips.");
            return;
        }

        var before = _sandboxes.Open(weapon.Id);
        if (before.Manifest.WorldClips.Count > 0)
        {
            Log($"{weapon.Id} already has its {before.Manifest.WorldClips.Count} third-person clips.");
            return;
        }

        SetStatus($"Adding {weapon.Id}'s third-person clips…");
        try
        {
            var sandbox = await Task.Run(() => SandboxStore.AddWorldClips(before, connection, weapon));
            Log($"Added {sandbox.Manifest.WorldClips.Count} third-person clips to {weapon.Id} ({sandbox.Manifest.WorldClips.Count(clip => clip.Editable)} editable). "
                + "Build .blend with Keep my animation to edit them in Blender; your first-person keys carry over.");
            ApplyStatusToTree();
            ShowThirdPersonProperties(weapon);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log($"Could not add {weapon.Id}'s third-person clips: {error.Message}");
        }
        finally
        {
            SetStatus("Ready");
        }
    }

    /// <summary>The part of the profile the open tab is about; the outliner colours by it.</summary>
    private StatusAspect CurrentAspect() => ToolTabs.SelectedIndex switch
    {
        AnimationsTool => StatusAspect.Animations,
        VDataTool => StatusAspect.VData,
        SoundsTool => StatusAspect.Sounds,
        GraphTool => StatusAspect.Graph,
        _ => StatusAspect.All,
    };

    private static string AspectName(StatusAspect aspect) => aspect switch
    {
        StatusAspect.Animations => "animations",
        StatusAspect.Sounds => "sound events",
        StatusAspect.VData => "vdata",
        StatusAspect.Graph => "graphs",
        _ => "everything",
    };

    /// <summary>
    /// A weapon's state for one aspect. The profile starts from the addon, so
    /// the addon's own work in that aspect (custom clips, graphs; vdata on the VData tab only) with
    /// nothing pending counts as matching (green).
    /// </summary>
    private (WeaponState State, IReadOnlyList<string> Reasons) AspectStatus(WeaponEntry weapon, StatusAspect aspect)
    {
        var part = StatusFor(weapon).Part(aspect);
        return part.State == WeaponState.Untouched && HasAddonOwn(weapon, aspect)
            ? (WeaponState.Applied, part.Reasons)
            : (part.State, part.Reasons);
    }

    private bool HasAddonOwn(WeaponEntry weapon, StatusAspect aspect)
    {
        var own = _inventory.For(weapon.Id);
        var clips = own.Any(asset => asset.Kind == AddonAssetKind.Clip);
        var graphs = own.Any(asset => asset.Kind == AddonAssetKind.Graph);
        var vdata = BlocksFor(weapon).Any(_inventory.OwnVDataBlocks.Contains);
        return aspect switch
        {
            StatusAspect.Animations => clips,
            StatusAspect.Graph => graphs,
            StatusAspect.VData => vdata,
            StatusAspect.Sounds => false,
            _ => clips || graphs, // VData has no ties to the other tabs: only the VData tab colours by it.
        };
    }

    /// <summary>"8 clips (draw_aug, idle_aug, …) and 2 graphs" the addon has for a weapon that Weapon Lab did not make; "" when none.</summary>
    private string AddonOwnSummary(string weaponId, StatusAspect aspect = StatusAspect.All)
    {
        var kinds = aspect switch
        {
            StatusAspect.Animations => [AddonAssetKind.Clip],
            StatusAspect.Graph => [AddonAssetKind.Graph],
            StatusAspect.VData or StatusAspect.Sounds => Array.Empty<AddonAssetKind>(),
            _ => Enum.GetValues<AddonAssetKind>(),
        };
        var own = _inventory.OwnFor(weaponId).Where(asset => kinds.Contains(asset.Kind)).ToList();
        var vdata = aspect is StatusAspect.VData && _connection?.Weapons.FirstOrDefault(weapon => weapon.Id == weaponId) is { } entry
            ? BlocksFor(entry).Where(_inventory.OwnVDataBlocks.Contains).ToList()
            : [];
        if (own.Count == 0 && vdata.Count == 0)
        {
            return "";
        }

        var parts = own.GroupBy(asset => asset.Kind).Select(group =>
        {
            var names = group.Select(asset => asset.Name).ToList();
            var list = string.Join(", ", names.Take(3)) + (names.Count > 3 ? ", …" : "");
            return $"{names.Count} {group.Key.ToString().ToLowerInvariant()}{(names.Count == 1 ? "" : "s")} ({list})";
        }).ToList();
        if (vdata.Count > 0)
        {
            parts.Add($"vdata ({string.Join(", ", vdata)})");
        }

        return $"{Target()?.Name ?? "The addon"} has its own {string.Join(" and ", parts)} for this weapon, not made in Weapon Lab. Delivery builds on them; Set to CS2 default deletes them.";
    }

    /// <summary>
    /// The whole weapon's state. The profile starts from the addon, so a weapon
    /// whose addon has its own work and nothing pending counts as matching (green).
    /// </summary>
    private WeaponStatus StatusFor(WeaponEntry weapon)
    {
        var status = _status?.For(weapon.Id, BlocksFor(weapon)) ?? new WeaponStatus(WeaponState.Untouched, [], []);
        return status.State == WeaponState.Untouched && HasAddonOwn(weapon, StatusAspect.All)
            ? status with { State = WeaponState.Applied }
            : status;
    }

    /// <summary>The weapon's vdata blocks, found through its weapon skeleton (cached).</summary>
    private IReadOnlyList<string> BlocksFor(WeaponEntry weapon)
    {
        lock (_weaponBlocks)
        {
            if (_weaponBlocks.TryGetValue(weapon.Id, out var cached))
            {
                return cached;
            }
        }

        IReadOnlyList<string> blocks = [];
        if (_connection is { } connection)
        {
            try
            {
                blocks = connection.VDataBlocksFor(weapon);
            }
            catch (InvalidDataException)
            {
                // A clip VRF cannot read simply has no vdata link.
            }
        }

        lock (_weaponBlocks)
        {
            _weaponBlocks[weapon.Id] = blocks;
        }

        return blocks;
    }

    // ---- Exports from Blender ----

    /// <summary>Watches every sandbox's exports folder so a Blender export shows up the moment it lands.</summary>
    private void WatchExports()
    {
        try
        {
            Directory.CreateDirectory(_sandboxes.SandboxesPath);
            _exportWatcher = new FileSystemWatcher(_sandboxes.SandboxesPath, "*.export.json")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };
            _exportWatcher.Created += (_, args) => QueueExport(args.FullPath);
            _exportWatcher.Changed += (_, args) => QueueExport(args.FullPath);
            _exportWatcher.Renamed += (_, args) => QueueExport(args.FullPath);
            _exportWatcher.EnableRaisingEvents = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log($"Cannot watch for Blender exports: {error.Message}");
        }
    }

    private void QueueExport(string path)
    {
        // Atomic writes raise several events per export; handle each file once.
        lock (_pendingExports)
        {
            if (!_pendingExports.Add(path))
            {
                return;
            }
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(300);
            lock (_pendingExports)
            {
                _pendingExports.Remove(path);
            }

            await OnExportArrivedAsync(path);
        });
    }

    private async Task OnExportArrivedAsync(string reportPath)
    {

        ClipExportReport? report;
        string weaponId;
        try
        {
            // <sandbox>/exports/<clip>.export.json
            var sandbox = SandboxStore.OpenFolder(Path.GetDirectoryName(Path.GetDirectoryName(reportPath))!);
            weaponId = sandbox.Manifest.WeaponId;
            report = ClipExporter.LastExport(sandbox, Path.GetFileName(reportPath).Replace(".export.json", ""));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return; // Partly written or not ours; the next event will retry.
        }

        if (report is null)
        {
            return;
        }

        if (DateTime.UtcNow >= _quietExportsUntil) // The Anim Events tab logs the exports it writes itself.
        {
            Log($"Export: {weaponId} / {report.ClipId}. {ExportRow.Summary(report)}.");
        }
        ClearStockReset(weaponId);
        RebuildTree();
        await RefreshStatusAsync();
        if (_weapon?.Id == weaponId)
        {
            RefreshExports();
            if (_clipPath is { } path && ExplorerNode.ClipName(path) == report.ClipId)
            {
                await LoadClipAsync(_weapon, path);
            }
            else
            {
                RefreshWeaponView(_weapon);
            }
        }
    }
}
